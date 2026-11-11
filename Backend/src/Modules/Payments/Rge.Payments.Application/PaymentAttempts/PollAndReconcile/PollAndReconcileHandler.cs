using System.Collections.Concurrent;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.PaymentAttempts.PollAndReconcile;

public sealed class PollAndReconcileHandler : IQueryHandler<PollAndReconcileQuery, PaymentAttemptDto>
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> GatewayCheckCooldown = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> SettledOrders = new(StringComparer.OrdinalIgnoreCase);
    public static readonly TimeSpan CooldownWindow = TimeSpan.FromSeconds(10);

    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IClock _clock;
    private readonly IPaymentStatusNotifier? _notifier;

    public PollAndReconcileHandler(
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IPaymentGateway paymentGateway,
        IOutboxWriter outboxWriter,
        IClock clock)
        : this(attemptRepository, orderRepository, paymentGateway, outboxWriter, clock, notifier: null)
    {
    }

    public PollAndReconcileHandler(
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IPaymentGateway paymentGateway,
        IOutboxWriter outboxWriter,
        IClock clock,
        IPaymentStatusNotifier? notifier)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(paymentGateway);
        ArgumentNullException.ThrowIfNull(outboxWriter);
        ArgumentNullException.ThrowIfNull(clock);

        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _outboxWriter = outboxWriter;
        _clock = clock;
        _notifier = notifier;
    }

    public static void ResetCooldowns()
    {
        GatewayCheckCooldown.Clear();
        SettledOrders.Clear();
    }

    public async Task<Result<PaymentAttemptDto>> HandleAsync(
        PollAndReconcileQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.PaymentId))
        {
            return Result.Failure<PaymentAttemptDto>(
                Error.Validation("PollAndReconcile.IdRequired", "Payment ID is required."));
        }

        string rawId = query.PaymentId.Trim();

        PaymentAttempt? attempt = await _attemptRepository.GetByIdAsync(rawId, cancellationToken).ConfigureAwait(false)
            ?? await _attemptRepository.GetByProviderPaymentIdAsync(rawId, cancellationToken).ConfigureAwait(false);

        Order? order = null;
        if (attempt is not null)
        {
            order = await _orderRepository.GetByIdAsync(attempt.OrderId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            order = await _orderRepository.GetByIdAsync(rawId, cancellationToken).ConfigureAwait(false)
                ?? await _orderRepository.GetByOrderNumberAsync(rawId, cancellationToken).ConfigureAwait(false);

            if (order is not null)
            {
                attempt = await _attemptRepository.GetLatestByOrderIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        if (attempt is null)
        {
            return Result.Failure<PaymentAttemptDto>(
                Error.NotFound("Payments.NotFound", $"Payment attempt '{query.PaymentId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;

        if (attempt.Status == PaymentAttemptStatus.Succeeded)
        {
            if (order is not null && order.Status != OrderStatus.Paid)
            {
                DateTimeOffset paidAt = attempt.PaidAt ?? nowUtc;
                order.MarkPaid(paidAt);
                await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);
            }
            return Result.Success(attempt.ToDto(nowUtc));
        }

        bool hasClientRedirect = !string.IsNullOrWhiteSpace(query.ClientReportedRedirectStatus);

        bool isWithinCooldown = false;
        if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
            GatewayCheckCooldown.TryGetValue(attempt.ProviderPaymentId, out DateTimeOffset lastCheck) &&
            (nowUtc - lastCheck) < CooldownWindow)
        {
            isWithinCooldown = true;
        }

        bool shouldCheckGateway = (query.AutoReconcile || hasClientRedirect) &&
            (!isWithinCooldown || hasClientRedirect) &&
            !string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
            !PaymentAttemptStateMachine.IsTerminal(attempt.Status);

        if (shouldCheckGateway && !string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
        {
            string providerPaymentId = attempt.ProviderPaymentId;
            GatewayCheckCooldown[providerPaymentId] = nowUtc;

            Result<GatewayPaymentStatusResult> statusResult = await _paymentGateway
                .GetCheckoutSessionStatusAsync(providerPaymentId, cancellationToken)
                .ConfigureAwait(false);

            if (statusResult.IsSuccess)
            {
                var gw = statusResult.Value;
                string gwStatus = gw.Status.ToLowerInvariant();
                PaymentAttemptStatus priorStatus = attempt.Status;

                // Re-fetch latest attempt from repository to check if concurrent webhook settled it
                PaymentAttempt? latestAttempt = await _attemptRepository.GetByIdAsync(attempt.Id, cancellationToken).ConfigureAwait(false);
                if (latestAttempt?.Status == PaymentAttemptStatus.Succeeded)
                {
                    if (order is not null && order.Status != OrderStatus.Paid)
                    {
                        DateTimeOffset paidAt = latestAttempt.PaidAt ?? nowUtc;
                        order.MarkPaid(paidAt);
                        await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);
                    }
                    return Result.Success(latestAttempt.ToDto(nowUtc));
                }

                if (gwStatus is "paid" or "succeeded")
                {
                    DateTimeOffset paidAt = gw.PaidAtUtc ?? nowUtc;
                    attempt.MarkSucceeded(paidAt, gw.PaymentMethodUsed);

                    if (order is not null && order.Status != OrderStatus.Paid && SettledOrders.TryAdd(order.Id, 0))
                    {
                        order.MarkPaid(paidAt);
                        await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                        var paidEvent = OrderPaidIntegrationEvent.Create(
                            orderId: order.Id,
                            orderNumber: order.OrderNumber,
                            paymentAttemptId: attempt.Id,
                            amountMinor: order.Amount.AmountMinor,
                            currency: order.Amount.CurrencyCode,
                            paymentMethod: attempt.PaymentMethod,
                            customerName: string.Empty,
                            customerEmail: string.Empty,
                            paidAtUtc: paidAt,
                            metadataJson: order.MetadataJson);

                        await _outboxWriter.EnqueueAsync(paidEvent, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (gwStatus is "cancelled")
                {
                    attempt.MarkFailed("source_cancelled", gw.FailureMessage ?? "Payment source was cancelled.", nowUtc);
                    TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                }
                else if (gwStatus is "failed")
                {
                    attempt.MarkFailed(gw.FailureCode ?? "source_failed", gw.FailureMessage ?? "Payment failed", nowUtc);
                    TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                }
                else if (gwStatus is "expired")
                {
                    attempt.MarkExpired(nowUtc, gw.FailureCode ?? "source_expired", gw.FailureMessage);
                    TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                }
                else if (hasClientRedirect)
                {
                    // Client reported redirect status, but gateway still shows active/awaiting_next_action
                    string reported = query.ClientReportedRedirectStatus!.Trim().ToLowerInvariant();
                    if (reported is "cancelled")
                    {
                        attempt.MarkFailed("source_cancelled", "Payment cancelled by customer during checkout redirect.", nowUtc);
                        TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                    }
                    else if (reported is "failed")
                    {
                        attempt.MarkFailed("customer_failed", "Payment failed during checkout redirect.", nowUtc);
                        TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                    }
                }

                await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

                if (priorStatus != attempt.Status && _notifier is not null)
                {
                    string statusStr = attempt.Status.ToString().ToLowerInvariant();
                    var notifyTasks = new List<Task>(2)
                    {
                        _notifier.NotifyPaymentStatusChangedAsync(
                            attempt.Id,
                            statusStr,
                            nowUtc,
                            cancellationToken)
                    };

                    if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
                        !string.Equals(attempt.ProviderPaymentId, attempt.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        notifyTasks.Add(_notifier.NotifyPaymentStatusChangedAsync(
                            attempt.ProviderPaymentId,
                            statusStr,
                            nowUtc,
                            cancellationToken));
                    }

                    await Task.WhenAll(notifyTasks).ConfigureAwait(false);
                }
            }
            else
            {
                // Double-capture race recovery:
                // 1. Check if concurrent webhook settled the attempt in the repository
                PaymentAttempt? latestAttempt = await _attemptRepository.GetByIdAsync(attempt.Id, cancellationToken).ConfigureAwait(false);
                if (latestAttempt?.Status == PaymentAttemptStatus.Succeeded)
                {
                    if (order is not null && order.Status != OrderStatus.Paid)
                    {
                        DateTimeOffset paidAt = latestAttempt.PaidAt ?? nowUtc;
                        order.MarkPaid(paidAt);
                        await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);
                    }
                    return Result.Success(latestAttempt.ToDto(nowUtc));
                }

                // 2. If statusResult failed (e.g. HTTP 400 Bad Request: source already charged/captured),
                // verify whether the source/session on PayMongo is actually paid
                var recoveryCheck = await _paymentGateway
                    .GetCheckoutSessionStatusAsync(providerPaymentId, cancellationToken)
                    .ConfigureAwait(false);

                if (recoveryCheck.IsSuccess && recoveryCheck.Value.Status.ToLowerInvariant() is "paid" or "succeeded")
                {
                    DateTimeOffset paidAt = recoveryCheck.Value.PaidAtUtc ?? nowUtc;
                    attempt.MarkSucceeded(paidAt, recoveryCheck.Value.PaymentMethodUsed);

                    if (order is not null && order.Status != OrderStatus.Paid && SettledOrders.TryAdd(order.Id, 0))
                    {
                        order.MarkPaid(paidAt);
                        await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                        var paidEvent = OrderPaidIntegrationEvent.Create(
                            orderId: order.Id,
                            orderNumber: order.OrderNumber,
                            paymentAttemptId: attempt.Id,
                            amountMinor: order.Amount.AmountMinor,
                            currency: order.Amount.CurrencyCode,
                            paymentMethod: attempt.PaymentMethod,
                            customerName: string.Empty,
                            customerEmail: string.Empty,
                            paidAtUtc: paidAt,
                            metadataJson: order.MetadataJson);

                        await _outboxWriter.EnqueueAsync(paidEvent, cancellationToken).ConfigureAwait(false);
                    }

                    await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

                    if (_notifier is not null)
                    {
                        await _notifier.NotifyPaymentStatusChangedAsync(
                            attempt.Id,
                            "succeeded",
                            nowUtc,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return Result.Success(attempt.ToDto(nowUtc));
                }
            }
        }

        if (!PaymentAttemptStateMachine.IsTerminal(attempt.Status) &&
            attempt.ExpiresAt.HasValue &&
            attempt.ExpiresAt.Value <= nowUtc)
        {
            // Authoritative Provider Verification at Deadline:
            // Never declare local expiration without verifying whether PayMongo settled the payment
            bool shouldExpire = true;
            string? expireFailureCode = null;
            string? expireFailureMsg = null;

            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
            {
                var finalCheck = await _paymentGateway
                    .GetCheckoutSessionStatusAsync(attempt.ProviderPaymentId, cancellationToken)
                    .ConfigureAwait(false);

                if (finalCheck.IsSuccess)
                {
                    string finalStatus = finalCheck.Value.Status.ToLowerInvariant();
                    if (finalStatus is "paid" or "succeeded")
                    {
                        shouldExpire = false;
                        DateTimeOffset paidAt = finalCheck.Value.PaidAtUtc ?? nowUtc;
                        attempt.MarkSucceeded(paidAt, finalCheck.Value.PaymentMethodUsed);

                        if (order is not null && order.Status != OrderStatus.Paid && SettledOrders.TryAdd(order.Id, 0))
                        {
                            order.MarkPaid(paidAt);
                            await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                            var paidEvent = OrderPaidIntegrationEvent.Create(
                                orderId: order.Id,
                                orderNumber: order.OrderNumber,
                                paymentAttemptId: attempt.Id,
                                amountMinor: order.Amount.AmountMinor,
                                currency: order.Amount.CurrencyCode,
                                paymentMethod: attempt.PaymentMethod,
                                customerName: string.Empty,
                                customerEmail: string.Empty,
                                paidAtUtc: paidAt,
                                metadataJson: order.MetadataJson);

                            await _outboxWriter.EnqueueAsync(paidEvent, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else if (finalStatus is "cancelled")
                    {
                        shouldExpire = false;
                        attempt.MarkFailed("source_cancelled", finalCheck.Value.FailureMessage ?? "Payment source was cancelled.", nowUtc);
                        TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                    }
                    else if (finalStatus is "failed")
                    {
                        shouldExpire = false;
                        attempt.MarkFailed(finalCheck.Value.FailureCode ?? "source_failed", finalCheck.Value.FailureMessage ?? "Payment failed", nowUtc);
                        TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                    }
                    else
                    {
                        expireFailureCode = finalCheck.Value.FailureCode;
                        expireFailureMsg = finalCheck.Value.FailureMessage;
                    }
                }
            }

            if (shouldExpire)
            {
                attempt.MarkExpired(nowUtc, expireFailureCode, expireFailureMsg);
                TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
            }

            await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

            if (_notifier is not null)
            {
                string statusStr = attempt.Status.ToString().ToLowerInvariant();
                await _notifier.NotifyPaymentStatusChangedAsync(
                    attempt.Id,
                    statusStr,
                    nowUtc,
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
                    !string.Equals(attempt.ProviderPaymentId, attempt.Id, StringComparison.OrdinalIgnoreCase))
                {
                    await _notifier.NotifyPaymentStatusChangedAsync(
                        attempt.ProviderPaymentId,
                        statusStr,
                        nowUtc,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (order is not null && order.Status == OrderStatus.Paid && attempt.Status != PaymentAttemptStatus.Succeeded)
        {
            attempt.MarkSucceeded(order.UpdatedAt, attempt.PaymentMethod);
            await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

            if (_notifier is not null)
            {
                await _notifier.NotifyPaymentStatusChangedAsync(
                    attempt.Id,
                    "succeeded",
                    nowUtc,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success(attempt.ToDto(nowUtc));
    }

    private void TriggerBackgroundSessionExpire(string? providerPaymentId)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId) ||
            !providerPaymentId.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string sessionId = providerPaymentId;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _paymentGateway.ExpireCheckoutSessionAsync(sessionId, timeoutCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Background housekeeping failure ignored to avoid impacting client response latency
            }
        });
    }
}

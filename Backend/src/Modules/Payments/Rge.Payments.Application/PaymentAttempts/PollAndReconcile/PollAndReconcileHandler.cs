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

    public static void ResetCooldowns() => GatewayCheckCooldown.Clear();

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

        bool isWithinCooldown = false;
        if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
            GatewayCheckCooldown.TryGetValue(attempt.ProviderPaymentId, out DateTimeOffset lastCheck) &&
            (nowUtc - lastCheck) < CooldownWindow)
        {
            isWithinCooldown = true;
        }

        if (query.AutoReconcile &&
            !isWithinCooldown &&
            !string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
            !PaymentAttemptStateMachine.IsTerminal(attempt.Status))
        {
            GatewayCheckCooldown[attempt.ProviderPaymentId] = nowUtc;

            Result<GatewayPaymentStatusResult> statusResult = await _paymentGateway
                .GetCheckoutSessionStatusAsync(attempt.ProviderPaymentId, cancellationToken)
                .ConfigureAwait(false);

            if (statusResult.IsSuccess)
            {
                var gw = statusResult.Value;
                string gwStatus = gw.Status.ToLowerInvariant();
                PaymentAttemptStatus priorStatus = attempt.Status;

                if (gwStatus is "paid" or "succeeded")
                {
                    DateTimeOffset paidAt = gw.PaidAtUtc ?? nowUtc;
                    attempt.MarkSucceeded(paidAt, gw.PaymentMethodUsed);

                    if (order is not null && order.Status != OrderStatus.Paid)
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
                else if (gwStatus is "expired" or "cancelled")
                {
                    attempt.MarkExpired(nowUtc, gw.FailureCode, gw.FailureMessage);
                    TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
                }
                else if (gwStatus is "failed")
                {
                    attempt.MarkFailed(gw.FailureCode ?? "FAILED", gw.FailureMessage ?? "Payment failed", nowUtc);
                    TriggerBackgroundSessionExpire(attempt.ProviderPaymentId);
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

                        if (order is not null && order.Status != OrderStatus.Paid)
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
                    else if (finalStatus is "failed")
                    {
                        shouldExpire = false;
                        attempt.MarkFailed(finalCheck.Value.FailureCode ?? "FAILED", finalCheck.Value.FailureMessage ?? "Payment failed", nowUtc);
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

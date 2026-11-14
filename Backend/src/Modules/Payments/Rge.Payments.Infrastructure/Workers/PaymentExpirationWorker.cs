#pragma warning disable CA1873
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Outbox;

namespace Rge.Payments.Infrastructure.Workers;

public sealed class PaymentExpirationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentExpirationWorker> _logger;

    public PaymentExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentExpirationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[PAYMONGO-WORKER] Started with 15s active reconciliation interval.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueAttemptsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PAYMONGO-WORKER-ERR] Unhandled exception during reconciliation pass.");
            }
#pragma warning restore CA1031

            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("[PAYMONGO-WORKER] Stopped.");
    }

    /// <summary>
    /// Executes a single bounded reconciliation + expiration pass.
    /// Can be called directly by deterministic integration tests.
    /// </summary>
    public Task<int> ProcessOverdueBatchOnceAsync(CancellationToken cancellationToken = default) =>
        ProcessOverdueAttemptsAsync(cancellationToken);

    public async Task<int> ProcessOverdueAttemptsAsync(CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        var attemptRepository = sp.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepository = sp.GetRequiredService<IOrderRepository>();
        var outboxPublisher = sp.GetRequiredService<IOrderPaidOutboxPublisher>();
        var gateway = sp.GetRequiredService<IPaymentGateway>();
        var clock = sp.GetRequiredService<IClock>();
        var notifier = sp.GetService<IPaymentStatusNotifier>();

        DateTimeOffset nowUtc = clock.UtcNow;

        IReadOnlyList<PaymentAttempt> nonTerminal =
            await attemptRepository.ListNonTerminalAsync(cancellationToken).ConfigureAwait(false);

        if (nonTerminal.Count == 0)
        {
            return 0;
        }

        // Identify the newest attempt per OrderId so any older superseded attempt is automatically expired
        Dictionary<string, string> latestAttemptIdByOrder = nonTerminal
            .Where(a => a.Status is not PaymentAttemptStatus.Processing && !PaymentAttemptStateMachine.IsTerminal(a.Status))
            .GroupBy(a => a.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.CreatedAt).First().Id,
                StringComparer.OrdinalIgnoreCase);

        int transitionedCount = 0;
        int inspected = 0;

        foreach (PaymentAttempt attempt in nonTerminal)
        {
            if (inspected >= BatchSize)
            {
                break;
            }

            inspected++;

            // Strict exclusion of Processing and Terminal states (F8 / R3)
            if (attempt.Status is PaymentAttemptStatus.Processing ||
                PaymentAttemptStateMachine.IsTerminal(attempt.Status))
            {
                _logger.LogInformation(
                    "[PAYMONGO-WORKER-SKIP] Skipping attempt {AttemptId} in non-sweepable status {Status}",
                    attempt.Id,
                    attempt.Status);
                continue;
            }

            double remainingSeconds = attempt.ExpiresAt.HasValue
                ? (attempt.ExpiresAt.Value - nowUtc).TotalSeconds
                : 0;
            bool isDeadlineReached = attempt.ExpiresAt.HasValue && (nowUtc >= attempt.ExpiresAt.Value || remainingSeconds <= 0);

            // Clean up superseded attempts on the same order (e.g. cs_33ab872df98fd94f8ee87338 when cs_1753341df564a3c58fb3cc49 is created)
            if (latestAttemptIdByOrder.TryGetValue(attempt.OrderId, out string? newestId) &&
                !string.Equals(newestId, attempt.Id, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "[PAYMONGO-WORKER-SUPERSEDED] Expiring older attempt {OldAttemptId} (cs={OldCsId}) superseded by {NewAttemptId} on Order {OrderId}",
                    attempt.Id,
                    attempt.ProviderPaymentId ?? "none",
                    newestId,
                    attempt.OrderId);

                if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
                {
                    _ = await gateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken).ConfigureAwait(false);
                }

                if (attempt.MarkExpired(nowUtc).IsSuccess)
                {
                    await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                    transitionedCount++;
                    await NotifyFrontendAsync(notifier, attempt.Id, attempt.ProviderPaymentId, nameof(PaymentAttemptStatus.Expired), nowUtc, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            // 1. Poll PayMongo for the live Checkout Session status
            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
            {
                var statusResult = await gateway
                    .GetCheckoutSessionStatusAsync(attempt.ProviderPaymentId, cancellationToken)
                    .ConfigureAwait(false);

                if (statusResult.IsSuccess)
                {
                    string remoteStatus = statusResult.Value.Status.Trim().ToLowerInvariant();

                    _logger.LogInformation(
                        "[PAYMONGO-WORKER-CHECK] attempt_id={AttemptId} | cs_id={CsId} | local_status={LocalStatus} | remote_status={RemoteStatus} | remaining_sec={RemSec:F0} | deadline_reached={DeadlineReached}",
                        attempt.Id,
                        attempt.ProviderPaymentId,
                        attempt.Status,
                        remoteStatus,
                        remainingSeconds,
                        isDeadlineReached);

                    // Case A: PAID / SUCCEEDED
                    if (remoteStatus is "paid" or "succeeded")
                    {
                        DateTimeOffset paidAt = statusResult.Value.PaidAtUtc ?? nowUtc;
                        string methodUsed = statusResult.Value.PaymentMethodUsed ?? attempt.PaymentMethod;

                        if (attempt.MarkSucceeded(paidAt, methodUsed).IsSuccess)
                        {
                            await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

                            Order? order = await orderRepository.GetByIdAsync(attempt.OrderId, cancellationToken).ConfigureAwait(false);
                            if (order is not null && !string.Equals(order.Status, "paid", StringComparison.OrdinalIgnoreCase))
                            {
                                order.MarkPaid(paidAt);
                                await orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                                var paidEvent = OrderPaidIntegrationEvent.Create(
                                    orderId: order.Id,
                                    orderNumber: order.OrderNumber,
                                    paymentAttemptId: attempt.Id,
                                    amountMinor: attempt.Amount.AmountMinor,
                                    currency: attempt.Amount.CurrencyCode,
                                    paymentMethod: methodUsed,
                                    customerName: string.Empty,
                                    customerEmail: string.Empty,
                                    paidAtUtc: paidAt,
                                    metadataJson: order.MetadataJson);

                                await outboxPublisher.PublishOrderPaidAsync(
                                    paidEvent,
                                    cancellationToken).ConfigureAwait(false);
                            }

                            transitionedCount++;
                            await NotifyFrontendAsync(notifier, attempt.Id, attempt.ProviderPaymentId, nameof(PaymentAttemptStatus.Succeeded), paidAt, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        continue;
                    }

                    // Case B: FAILED (including when a Source src_... expires/fails during checkout)
                    if (remoteStatus is "failed")
                    {
                        string failCode = statusResult.Value.FailureCode ?? "payment_failed";
                        string failMsg = statusResult.Value.FailureMessage ?? "Payment failed at gateway.";

                        _logger.LogWarning(
                            "[PAYMONGO-WORKER-FAIL-DETECTED] Locking failed session cs_id={CsId} for attempt={AttemptId} (code={Code}, msg={Msg})",
                            attempt.ProviderPaymentId,
                            attempt.Id,
                            failCode,
                            failMsg);

                        _ = await gateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken).ConfigureAwait(false);

                        if (attempt.MarkFailed(failCode, failMsg, nowUtc).IsSuccess)
                        {
                            await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                            transitionedCount++;

                            await NotifyFrontendAsync(notifier, attempt.Id, attempt.ProviderPaymentId, nameof(PaymentAttemptStatus.Failed), nowUtc, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        continue;
                    }

                    // Case C: EXPIRED / CANCELLED on PayMongo — or an abandoned QR/source whose
                    // Checkout Session is still remotely active (code=qrph_source_expired).
                    if (remoteStatus is "expired" or "cancelled")
                    {
                        string? expireCode = statusResult.Value.FailureCode;
                        bool remoteSessionStillActive = string.Equals(expireCode, "qrph_source_expired", StringComparison.OrdinalIgnoreCase);

                        _logger.LogInformation(
                            "[PAYMONGO-WORKER-EXPIRED-DETECTED] cs_id={CsId} is '{RemoteStatus}' (code={Code}, close_remote={CloseRemote}). Marking local attempt {AttemptId} Expired.",
                            attempt.ProviderPaymentId,
                            remoteStatus,
                            expireCode ?? "none",
                            remoteSessionStillActive,
                            attempt.Id);

                        if (remoteSessionStillActive)
                        {
                            _ = await gateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken).ConfigureAwait(false);
                        }

                        if (attempt.MarkExpired(nowUtc, expireCode, statusResult.Value.FailureMessage).IsSuccess)
                        {
                            await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                            transitionedCount++;

                            await NotifyFrontendAsync(notifier, attempt.Id, attempt.ProviderPaymentId, nameof(PaymentAttemptStatus.Expired), nowUtc, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        continue;
                    }

                    // Case D: Processing at deadline -> defer
                    if (isDeadlineReached && remoteStatus is "processing")
                    {
                        _logger.LogInformation(
                            "[PAYMONGO-WORKER-DEFER] Attempt {AttemptId} reached deadline but remote is 'processing'. Deferring.",
                            attempt.Id);
                        continue;
                    }

                    // Case E: Active session with recoverable source/QR notice (e.g. qrph_source_expired)
                    if (!isDeadlineReached && remoteStatus is "awaiting_next_action" or "active" or "pending")
                    {
                        if (!string.IsNullOrWhiteSpace(statusResult.Value.FailureCode) || attempt.FailureCode is not null)
                        {
                            attempt.RecordRecoverableNotice(
                                statusResult.Value.FailureCode,
                                statusResult.Value.FailureMessage,
                                nowUtc);
                            await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            if (!isDeadlineReached)
            {
                continue;
            }

            // 2. Local deadline reached -> Call PayMongo Expire API and transition to Expired
            _logger.LogInformation(
                "[PAYMONGO-WORKER-DEADLINE] Local deadline reached for attempt {AttemptId} (cs_id={CsId}). Calling Expire API.",
                attempt.Id,
                attempt.ProviderPaymentId ?? "none");

            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
            {
                _ = await gateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken).ConfigureAwait(false);
            }

            if (attempt.MarkExpired(nowUtc).IsSuccess)
            {
                await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                transitionedCount++;

                await NotifyFrontendAsync(notifier, attempt.Id, attempt.ProviderPaymentId, nameof(PaymentAttemptStatus.Expired), nowUtc, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return transitionedCount;
    }

    private async Task NotifyFrontendAsync(
        IPaymentStatusNotifier? notifier,
        string attemptId,
        string? providerPaymentId,
        string status,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (notifier is null)
        {
            _logger.LogWarning("[PAYMONGO-SIGNALR-SKIP] IPaymentStatusNotifier is not registered in DI!");
            return;
        }

        try
        {
            await notifier.NotifyPaymentStatusChangedAsync(attemptId, status, updatedAtUtc, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(providerPaymentId))
            {
                await notifier.NotifyPaymentStatusChangedAsync(providerPaymentId, status, updatedAtUtc, cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "[PAYMONGO-SIGNALR-SENT] Broadcast status='{Status}' to groups payment:{AttemptId} and payment:{CsId}",
                status,
                attemptId,
                providerPaymentId ?? "none");
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PAYMONGO-SIGNALR-ERR] Failed broadcasting SignalR for attempt {AttemptId}.", attemptId);
        }
#pragma warning restore CA1031
    }
}

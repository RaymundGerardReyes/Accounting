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

/// <summary>
/// Background worker that reconciles and expires overdue non-terminal payment attempts.
/// Enforces the rule: ALWAYS verify PayMongo's authoritative session status before expiring.
/// </summary>
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
        _logger.LogInformation("PaymentExpirationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueBatchOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Catch all for background worker resilience
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentExpirationWorker encountered an unhandled error.");
            }
#pragma warning restore CA1031

            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("PaymentExpirationWorker stopped.");
    }

    /// <summary>
    /// Executes a single bounded reconciliation + expiration pass.
    /// Can be called directly by deterministic integration tests.
    /// </summary>
    public async Task<int> ProcessOverdueBatchOnceAsync(CancellationToken cancellationToken = default)
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

        int processedCount = 0;

        foreach (PaymentAttempt attempt in nonTerminal)
        {
            if (processedCount >= BatchSize)
            {
                break;
            }

            // Skip attempts that have no deadline or have not yet reached their deadline
            if (attempt.ExpiresAt is null || attempt.ExpiresAt > nowUtc)
            {
                continue;
            }

            processedCount++;

            await ReconcileAndExpireSingleAttemptAsync(
                attempt,
                attemptRepository,
                orderRepository,
                outboxPublisher,
                gateway,
                notifier,
                nowUtc,
                cancellationToken).ConfigureAwait(false);
        }

        return processedCount;
    }

    private async Task ReconcileAndExpireSingleAttemptAsync(
        PaymentAttempt attempt,
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IOrderPaidOutboxPublisher outboxPublisher,
        IPaymentGateway gateway,
        IPaymentStatusNotifier? notifier,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        // 1. Reconcile against PayMongo's authoritative Checkout Session status first
        if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
        {
            var statusResult = await gateway
                .GetCheckoutSessionStatusAsync(attempt.ProviderPaymentId, cancellationToken)
                .ConfigureAwait(false);

            if (statusResult.IsSuccess)
            {
                string remoteStatus = statusResult.Value.Status.Trim().ToLowerInvariant();

                // Case A: Already paid on PayMongo (e.g., missed or delayed webhook) -> Preserve Paid status!
                if (remoteStatus is "paid" or "succeeded")
                {
                    DateTimeOffset paidAt = statusResult.Value.PaidAtUtc ?? nowUtc;
                    string methodUsed = statusResult.Value.PaymentMethodUsed ?? attempt.PaymentMethod;

                    var markPaidRes = attempt.MarkSucceeded(paidAt, methodUsed);
                    if (markPaidRes.IsSuccess)
                    {
                        await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

                        Order? order = await orderRepository.GetByIdAsync(attempt.OrderId, cancellationToken).ConfigureAwait(false);
                        if (order is not null && !string.Equals(order.Status, "paid", StringComparison.OrdinalIgnoreCase))
                        {
                            order.MarkPaid(paidAt);
                            await orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                            await outboxPublisher.PublishOrderPaidAsync(
                                OrderPaidIntegrationEvent.Create(
                                    orderId: order.Id,
                                    orderNumber: order.OrderNumber,
                                    paymentAttemptId: attempt.Id,
                                    amountMinor: attempt.Amount.AmountMinor,
                                    currency: attempt.Amount.CurrencyCode,
                                    paymentMethod: attempt.PaymentMethod,
                                    customerName: "Valued Customer",
                                    customerEmail: string.Empty,
                                    paidAtUtc: paidAt),
                                cancellationToken).ConfigureAwait(false);
                        }

                        await SafeNotifyAsync(notifier, attempt.Id, nameof(PaymentAttemptStatus.Succeeded), paidAt, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return;
                }

                // Case B: Payment is currently in-flight/processing -> Defer expiration!
                if (remoteStatus is "processing" or "awaiting_next_action")
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation(
                            "Deferring expiration for attempt {AttemptId} because remote status is '{RemoteStatus}'.",
                            attempt.Id,
                            remoteStatus);
                    }

                    return;
                }
            }

            // 2. Session is eligible for expiration -> Call PayMongo Expire API if not already expired remotely
            if (!statusResult.IsSuccess ||
                !string.Equals(statusResult.Value.Status, "expired", StringComparison.OrdinalIgnoreCase))
            {
                var expireResult = await gateway
                    .ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken)
                    .ConfigureAwait(false);

                if (expireResult.IsFailure)
                {
                    // Do NOT blindly mark expired if PayMongo rejected the expiration request
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning(
                            "PayMongo rejected expiration for session {ProviderPaymentId} (Attempt {AttemptId}): {Error}. Skipping local expiration.",
                            attempt.ProviderPaymentId,
                            attempt.Id,
                            expireResult.Error.Message);
                    }

                    return;
                }
            }
        }

        // 3. Transition domain aggregate to Expired and persist
        var transitionResult = attempt.MarkExpired(nowUtc);
        if (transitionResult.IsFailure)
        {
            return;
        }

        await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        // 4. Broadcast real-time status change via SignalR
        await SafeNotifyAsync(notifier, attempt.Id, nameof(PaymentAttemptStatus.Expired), nowUtc, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SafeNotifyAsync(
        IPaymentStatusNotifier? notifier,
        string attemptId,
        string status,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (notifier is null)
        {
            return;
        }

        try
        {
            await notifier.NotifyPaymentStatusChangedAsync(attemptId, status, updatedAtUtc, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(ex, "SignalR notification failed for attempt {AttemptId}.", attemptId);
            }
        }
#pragma warning restore CA1031
    }
}

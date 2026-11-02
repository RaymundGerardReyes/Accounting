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
/// Background worker that continuously reconciles non-terminal payment attempts against PayMongo
/// and enforces server-side expiration deadlines every 15 seconds.
/// Stops the Next.js countdown immediately when a session is Paid, Failed, or Expired.
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
                await ProcessOverdueAttemptsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031
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

        int transitionedCount = 0;
        int inspected = 0;

        foreach (PaymentAttempt attempt in nonTerminal)
        {
            if (inspected >= BatchSize)
            {
                break;
            }

            inspected++;

            bool isDeadlineReached = attempt.ExpiresAt.HasValue && attempt.ExpiresAt.Value <= nowUtc;

            // 1. Always query PayMongo's live status for non-terminal attempts with a ProviderPaymentId
            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
            {
                var statusResult = await gateway
                    .GetCheckoutSessionStatusAsync(attempt.ProviderPaymentId, cancellationToken)
                    .ConfigureAwait(false);

                if (statusResult.IsSuccess)
                {
                    string remoteStatus = statusResult.Value.Status.Trim().ToLowerInvariant();

                    // Case A: Remote payment SUCCEEDED / PAID
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
                                    customerName: "Valued Customer",
                                    customerEmail: string.Empty,
                                    paidAtUtc: paidAt);

                                await outboxPublisher.PublishOrderPaidAsync(
                                    paidEvent,
                                    cancellationToken).ConfigureAwait(false);
                            }

                            transitionedCount++;
                            await NotifyFrontendAsync(notifier, attempt.Id, nameof(PaymentAttemptStatus.Succeeded), paidAt, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        continue;
                    }

                    // Case B: Remote payment FAILED on PayMongo (even while countdown is still at 25:58!)
                    if (remoteStatus is "failed")
                    {
                        string failCode = statusResult.Value.FailureCode ?? "payment_failed";
                        string failMsg = statusResult.Value.FailureMessage ?? "Payment failed at gateway.";

                        // Also expire the Checkout Session on PayMongo so the failed session is closed
                        _ = await gateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken).ConfigureAwait(false);

                        if (attempt.MarkFailed(failCode, failMsg, nowUtc).IsSuccess)
                        {
                            await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                            transitionedCount++;

                            await NotifyFrontendAsync(notifier, attempt.Id, nameof(PaymentAttemptStatus.Failed), nowUtc, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        continue;
                    }

                    // Case C: Remote session EXPIRED on PayMongo before local timer reached zero
                    if (remoteStatus is "expired" or "cancelled")
                    {
                        if (attempt.MarkExpired(nowUtc).IsSuccess)
                        {
                            await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                            transitionedCount++;

                            await NotifyFrontendAsync(notifier, attempt.Id, nameof(PaymentAttemptStatus.Expired), nowUtc, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        continue;
                    }

                    // Case D: Payment is actively processing at the deadline -> Defer expiration
                    if (isDeadlineReached && remoteStatus is "processing")
                    {
                        continue;
                    }
                }
            }

            // 2. If local deadline has not been reached yet and remote is still "active", keep counting down
            if (!isDeadlineReached)
            {
                continue;
            }

            // 3. Local deadline reached -> Explicitly expire the Checkout Session on PayMongo
            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
            {
                var expireResult = await gateway
                    .ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken)
                    .ConfigureAwait(false);

                if (expireResult.IsFailure && _logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(
                        "Could not expire PayMongo session {ProviderPaymentId} for attempt {AttemptId}: {Error}",
                        attempt.ProviderPaymentId,
                        attempt.Id,
                        expireResult.Error.Message);
                }
            }

            if (attempt.MarkExpired(nowUtc).IsSuccess)
            {
                await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);
                transitionedCount++;

                await NotifyFrontendAsync(notifier, attempt.Id, nameof(PaymentAttemptStatus.Expired), nowUtc, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return transitionedCount;
    }

    private async Task NotifyFrontendAsync(
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

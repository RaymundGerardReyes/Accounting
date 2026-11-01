using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Infrastructure.Workers;

/// <summary>
/// Background worker that polls for overdue pending payment attempts every
/// 15 seconds.  For each attempt whose <c>ExpiresAt</c> has passed:
/// <list type="bullet">
///   <item>Calls <see cref="IPaymentGateway.ExpireCheckoutSessionAsync"/> to
///         revoke the hosted payment URL on PayMongo's servers.</item>
///   <item>Transitions the local aggregate to <c>Expired</c>.</item>
///   <item>Notifies connected clients via <see cref="IPaymentStatusNotifier"/>
///         so the countdown stops immediately without a page reload.</item>
/// </list>
/// Processes at most 50 attempts per cycle to stay bounded.
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
#pragma warning disable CA1031 // catch all for worker resilience
            catch (Exception ex)
            {
                _logger.LogError(ex, "PaymentExpirationWorker encountered an unhandled error.");
            }
#pragma warning restore CA1031

            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("PaymentExpirationWorker stopped.");
    }

    private async Task ProcessOverdueAttemptsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        var attemptRepository = sp.GetRequiredService<IPaymentAttemptRepository>();
        var gateway = sp.GetRequiredService<IPaymentGateway>();
        var clock = sp.GetRequiredService<IClock>();
        var notifier = sp.GetService<IPaymentStatusNotifier>();

        DateTimeOffset nowUtc = clock.UtcNow;

        IReadOnlyList<PaymentAttempt> nonTerminal =
            await attemptRepository.ListNonTerminalAsync(cancellationToken).ConfigureAwait(false);

        int processed = 0;

        foreach (PaymentAttempt attempt in nonTerminal)
        {
            if (processed >= BatchSize)
            {
                break;
            }

            // Only expire attempts that have an expiry time set and have passed it
            if (attempt.ExpiresAt is null || attempt.ExpiresAt > nowUtc)
            {
                continue;
            }

            processed++;

            // Attempt to expire on PayMongo's side first (idempotent — 400 means already settled)
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

            // Transition domain aggregate
            var transitionResult = attempt.MarkExpired(nowUtc);
            if (transitionResult.IsFailure)
            {
                // Already in a terminal state — skip silently
                continue;
            }

            await attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Expired overdue payment attempt {AttemptId} (provider: {ProviderPaymentId}).",
                    attempt.Id,
                    attempt.ProviderPaymentId);
            }

            // Notify connected clients via SignalR
            if (notifier is not null)
            {
                try
                {
                    await notifier.NotifyPaymentStatusChangedAsync(
                        attempt.Id,
                        nameof(PaymentAttemptStatus.Expired),
                        nowUtc,
                        cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // notification failure must not block expiration
                catch (Exception ex)
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning(ex, "SignalR notification failed for attempt {AttemptId}.", attempt.Id);
                    }
                }
#pragma warning restore CA1031
            }
        }

        if (processed > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("PaymentExpirationWorker expired {Count} overdue attempt(s).", processed);
        }
    }
}


using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.Ports;

/// <summary>
/// Persistence port for querying and updating PaymentAttempt entities.
/// </summary>
public interface IPaymentAttemptRepository
{
    Task<PaymentAttempt?> GetByIdAsync(string attemptId, CancellationToken cancellationToken = default);
    Task<PaymentAttempt?> GetByProviderPaymentIdAsync(string providerPaymentId, CancellationToken cancellationToken = default);
    Task<PaymentAttempt?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentAttempt>> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<PaymentAttempt?> GetLatestByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentAttempt>> ListNonTerminalAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PaymentAttempt attempt, CancellationToken cancellationToken = default);
}


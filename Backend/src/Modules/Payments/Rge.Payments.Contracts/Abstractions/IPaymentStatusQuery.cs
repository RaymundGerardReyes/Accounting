namespace Rge.Payments.Contracts.Abstractions;

public sealed record PaymentAttemptLookupDto(
    string AttemptId,
    string OrderId,
    string Provider,
    string PaymentMethod,
    string? ProviderPaymentId,
    long AmountMinor,
    string Currency,
    string Status,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? PaidAtUtc,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Cross-module contract for querying PaymentAttempt records.
/// </summary>
public interface IPaymentStatusQuery
{
    Task<PaymentAttemptLookupDto?> GetAttemptByIdAsync(string attemptId, CancellationToken cancellationToken = default);
    Task<PaymentAttemptLookupDto?> GetAttemptByProviderPaymentIdAsync(string providerPaymentId, CancellationToken cancellationToken = default);
}


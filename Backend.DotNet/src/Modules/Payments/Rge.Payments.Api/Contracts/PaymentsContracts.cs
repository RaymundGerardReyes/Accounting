namespace Rge.Payments.Api.Contracts;

public sealed record CreateOrderRequest(
    string Title,
    string Category,
    string ReferenceCode,
    long? AmountMinor = null,
    int Quantity = 1,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record CreatePaymentAttemptRequest(
    string? PaymentMethod = "hosted_checkout",
    string? IdempotencyKey = null,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null,
    string? SuccessUrl = null,
    string? CancelUrl = null);

public sealed record ExpirePaymentRequest(
    string? Reason = null);

public sealed record FailPaymentRequest(
    string? FailureCode = null,
    string? Code = null,
    string? FailureMessage = null,
    string? Message = null);

public sealed record SimulatePaymentAttemptRequest(
    string? EventType = "checkout_session.payment.paid",
    string? FailureCode = null,
    string? FailureMessage = null);

public sealed record SimulateWebhookRequest(
    string PaymentAttemptId,
    string? EventType = "checkout_session.payment.paid",
    string? FailureCode = null,
    string? FailureMessage = null);

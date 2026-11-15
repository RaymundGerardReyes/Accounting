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

public sealed record UpdateOrderRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("title")] string? Title = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("description")] string? Description = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("amount")] long? Amount = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("amount_minor")] long? AmountMinor = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("amount_centavos")] long? AmountCentavos = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("customer_name")] string? CustomerName = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("customer_email")] string? CustomerEmail = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("currency")] string? Currency = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("metadata")] System.Text.Json.JsonElement? Metadata = null);

public sealed record CancelOrderRequest(
    string? Reason = null);


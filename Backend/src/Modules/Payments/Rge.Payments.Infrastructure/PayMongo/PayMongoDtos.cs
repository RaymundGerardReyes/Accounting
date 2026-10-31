using System.Text.Json.Serialization;

namespace Rge.Payments.Infrastructure.PayMongo;

// =============================================================================
// 1. Outbound Request DTOs: POST https://api.paymongo.com/v1/checkout_sessions
// =============================================================================

public sealed record PayMongoCreateCheckoutRequest(
    [property: JsonPropertyName("data")] PayMongoCreateCheckoutData Data);

public sealed record PayMongoCreateCheckoutData(
    [property: JsonPropertyName("attributes")] PayMongoCreateCheckoutAttributes Attributes);

public sealed record PayMongoCreateCheckoutAttributes(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("reference_number")] string ReferenceNumber,
    [property: JsonPropertyName("send_email_receipt")] bool SendEmailReceipt,
    [property: JsonPropertyName("show_description")] bool ShowDescription,
    [property: JsonPropertyName("show_line_items")] bool ShowLineItems,
    [property: JsonPropertyName("success_url")] string SuccessUrl,
    [property: JsonPropertyName("cancel_url")] string CancelUrl,
    [property: JsonPropertyName("payment_method_types")] IReadOnlyList<string> PaymentMethodTypes,
    [property: JsonPropertyName("line_items")] IReadOnlyList<PayMongoLineItem> LineItems,
    [property: JsonPropertyName("billing")] PayMongoBillingInfo? Billing = null,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record PayMongoLineItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("amount")] long AmountMinor,
    [property: JsonPropertyName("currency")] string Currency = "PHP",
    [property: JsonPropertyName("description")] string? Description = null);

public sealed record PayMongoBillingInfo(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone);

// =============================================================================
// 2. Inbound Response DTOs: Checkout Session Resource (cs_...)
// =============================================================================

public sealed record PayMongoCheckoutSessionEnvelope(
    [property: JsonPropertyName("data")] PayMongoCheckoutSessionData? Data);

public sealed record PayMongoCheckoutSessionData(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("attributes")] PayMongoCheckoutSessionAttributes Attributes);

public sealed record PayMongoCheckoutSessionAttributes(
    [property: JsonPropertyName("checkout_url")] string? CheckoutUrl,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("reference_number")] string? ReferenceNumber,
    [property: JsonPropertyName("payment_method_used")] string? PaymentMethodUsed,
    [property: JsonPropertyName("paid_at")] long? PaidAtUnix,
    [property: JsonPropertyName("expired_at")] long? ExpiredAtUnix,
    [property: JsonPropertyName("payments")] IReadOnlyList<PayMongoNestedPayment>? Payments);

public sealed record PayMongoNestedPayment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("attributes")] PayMongoNestedPaymentAttributes? Attributes);

public sealed record PayMongoNestedPaymentAttributes(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("failed_code")] string? FailedCode,
    [property: JsonPropertyName("failed_message")] string? FailedMessage,
    [property: JsonPropertyName("paid_at")] long? PaidAtUnix);

// =============================================================================
// 3. Inbound Webhook Event Envelope (evt_...)
// =============================================================================

public sealed record PayMongoWebhookEnvelope(
    [property: JsonPropertyName("data")] PayMongoWebhookEventData? Data);

public sealed record PayMongoWebhookEventData(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("attributes")] PayMongoWebhookEventAttributes? Attributes);

public sealed record PayMongoWebhookEventAttributes(
    [property: JsonPropertyName("type")] string EventType,
    [property: JsonPropertyName("livemode")] bool LiveMode,
    [property: JsonPropertyName("created_at")] long CreatedAtUnix,
    [property: JsonPropertyName("data")] PayMongoCheckoutSessionData? ResourceData);


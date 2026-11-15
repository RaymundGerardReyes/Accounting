using System.Text.Json.Serialization;

namespace Rge.Integrations.Contracts;

public sealed record SaveCredentialsRequest(
    string? PaymongoSecretKey = null,
    string? PaymongoWebhookSecret = null,
    string? SmtpHost = null,
    int? SmtpPort = null,
    string? SmtpUser = null,
    string? SmtpAppPassword = null,
    string? GoogleFormsSecret = null);

public sealed record SaveCredentialsResponse(
    bool Success,
    string Message,
    IReadOnlyList<string> UpdatedKeys);

public sealed record EmailTestRequest(
    string? Host = null,
    int? Port = null,
    string? User = null,
    string? Password = null,
    bool SendTestEmail = false,
    string? Recipient = null);

public sealed record SmtpProbeResult(
    string OperationalState,
    long LatencyMs,
    string Host,
    string? Message = null);

public sealed record SmtpExecutionResult(
    bool Success,
    long LatencyMs,
    string Message,
    string? ErrorDetail = null,
    string? RecipientEmail = null,
    string? SenderCopyEmail = null);

public sealed record SmtpRuntimeSettings(
    string Host,
    int Port,
    string Username,
    string Password,
    string FromEmail,
    string SenderName,
    bool UseTls,
    string DefaultReceiptEmail);

public sealed record EmailDispatchResult(
    bool Dispatched,
    string Recipient,
    string MessageId);

public sealed record EmailTestResponse(
    bool Success,
    SmtpProbeResult Probe,
    EmailDispatchResult? EmailDispatch = null);

public sealed record PayMongoTestRequest(
    string? SecretKey = null,
    string? WebhookSecret = null);

public sealed record PayMongoProbeResult(
    string OperationalState,
    long LatencyMs,
    string Mode,
    string? Message = null);

public sealed record WebhookCheckResult(
    bool SignatureVerified,
    string SimulatedHeader,
    string Message);

public sealed record PayMongoTestResponse(
    bool Success,
    PayMongoProbeResult Probe,
    WebhookCheckResult? WebhookCheck = null);

public sealed record IntegrationStatusResponse(
    bool Success,
    Dictionary<string, object> Integrations);

public sealed record GoogleFormsStatusResponse(
    [property: JsonPropertyName("enabled")] bool Enabled = true,
    [property: JsonPropertyName("total_submissions")] int TotalSubmissions = 0,
    [property: JsonPropertyName("synced_count")] int SyncedCount = 0,
    [property: JsonPropertyName("last_synced_at")] DateTimeOffset? LastSyncedAt = null,
    [property: JsonPropertyName("recent_submissions")] IReadOnlyList<GoogleFormSubmissionDto>? RecentSubmissions = null,
    [property: JsonPropertyName("secret_configured")] bool SecretConfigured = false,
    [property: JsonPropertyName("masked_secret")] string? MaskedSecret = null,
    [property: JsonPropertyName("status")] string Status = "active",
    [property: JsonPropertyName("ingested_orders_count")] int IngestedOrdersCount = 0,
    [property: JsonPropertyName("paid_orders_count")] int PaidOrdersCount = 0,
    [property: JsonPropertyName("pending_orders_count")] int PendingOrdersCount = 0,
    [property: JsonPropertyName("webhook_endpoint")] string WebhookEndpoint = "/api/v1/integrations/google-forms/submit",
    [property: JsonPropertyName("auth_header_name")] string AuthHeaderName = "X-Google-Forms-Secret",
    [property: JsonPropertyName("heartbeat_interval")] string HeartbeatInterval = "Active (4s interval)",
    [property: JsonPropertyName("script_template_path")] string ScriptTemplatePath = "scripts/google_apps_script/Code.gs");

public sealed record GoogleFormSubmissionDto(
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("order_number")] string OrderNumber,
    [property: JsonPropertyName("response_id")] string ResponseId,
    [property: JsonPropertyName("customer_name")] string CustomerName,
    [property: JsonPropertyName("customer_email")] string CustomerEmail,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("amount_centavos")] long AmountCentavos,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record GoogleFormStatusResponse(
    bool Enabled,
    string WebhookEndpoint,
    string AuthHeaderName,
    int IngestedOrdersCount,
    string HeartbeatInterval,
    string ScriptTemplatePath,
    bool SecretConfigured = false,
    string? MaskedSecret = null);

public sealed record GoogleFormSubmissionRequest(
    string ResponseId,
    int? SheetRowIndex,
    int? SheetRow,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string? ItemReference,
    string? ReferenceCode,
    string? Title,
    string? Category,
    int? Quantity,
    long? AmountMinor,
    string? PaymentMethod,
    string? Notes,
    Dictionary<string, object>? RawAnswers,
    string? StudentId,
    string? InstructorCredit,
    string? BookTitle,
    string? Secret,
    string? SecretKey);


public sealed record GoogleFormSubmissionResponse(
    string Status,
    string Message,
    string OrderId,
    string OrderNumber,
    string OrderTitle,
    long AmountMinor,
    string AmountPhp,
    string Currency,
    string CheckoutUrl,
    string PaymentAttemptId,
    string? ProviderPaymentId,
    string CustomerEmail,
    string CustomerName,
    bool IsDuplicate);


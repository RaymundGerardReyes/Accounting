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
    string Status,
    int IngestedOrdersCount,
    int PaidOrdersCount,
    int PendingOrdersCount,
    bool SecretConfigured);

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


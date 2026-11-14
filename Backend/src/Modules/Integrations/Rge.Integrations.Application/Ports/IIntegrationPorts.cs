// File: Backend/src/Modules/Integrations/Rge.Integrations.Application/Ports/IIntegrationPorts.cs
using Rge.Integrations.Contracts;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Application.Ports;

public interface ISecretStore
{
    Task<SaveCredentialsResponse> SaveCredentialsAsync(SaveCredentialsRequest request, CancellationToken cancellationToken = default);
    string? GetSecret(string key);
    bool HasGoogleFormsSecret();
}

public interface ISmtpProbe
{
    Task<EmailTestResponse> TestSmtpAsync(EmailTestRequest request, CancellationToken cancellationToken = default);
    Task<SmtpExecutionResult> VerifyHandshakeAsync(CancellationToken ct = default);
    Task<SmtpExecutionResult> SendVerificationEmailAsync(string? recipientEmail, CancellationToken ct = default);
    SmtpRuntimeSettings ResolveSettings();
}

public interface IPayMongoProbe
{
    Task<PayMongoTestResponse> TestPayMongoAsync(PayMongoTestRequest request, CancellationToken cancellationToken = default);
}

public interface IIntegrationDiagnosticProbe
{
    Task<IntegrationStatusResponse> ProbeAllAsync(CancellationToken cancellationToken = default);
}

public interface IGoogleFormsSubmissionStore
{
    Task<GoogleFormsStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task SaveSubmissionAsync(GoogleFormSubmission submission, CancellationToken cancellationToken = default);
    Task<GoogleFormSubmission?> GetByResponseIdAsync(string responseId, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

public interface IGoogleSheetsClient
{
    Task AppendRowAsync(IReadOnlyList<object?> row, CancellationToken cancellationToken = default);
}

public sealed record GoogleFormOrderCreationRequest(
    string ResponseId,
    int? SheetRowIndex,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string ItemReference,
    string Category,
    int Quantity,
    long? AmountMinor,
    string PaymentMethod,
    string? Title = null,
    string? Notes = null,
    string? StudentId = null,
    string? InstructorCredit = null,
    string? BookTitle = null,
    DateTimeOffset? SubmittedAt = null,
    bool SendInitialEmail = true);

public sealed record GoogleFormOrderCreationResult(
    string OrderId,
    string OrderNumber,
    string OrderTitle,
    long AmountMinor,
    string Currency,
    string CheckoutUrl,
    string PaymentAttemptId,
    string? ProviderPaymentId);

public sealed record GoogleFormOrderBridgeRequest(
    string ResponseId,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    string ProductCode,
    string ProductTitle,
    int Quantity,
    long UnitPriceMinor,
    long TotalAmountMinor,
    string Currency,
    string Status,
    DateTimeOffset SubmittedAt,
    bool SendInitialEmail,
    IReadOnlyDictionary<string, string>? AdditionalMetadata = null);

public sealed record GoogleFormOrderBridgeResult(
    string OrderId,
    string OrderNumber,
    string Status,
    bool CreatedNew,
    bool EmailDispatched,
    string? EmailStatusMessage);

public interface IGoogleFormOrderBridge
{
    Task<GoogleFormOrderCreationResult> CreateOrderAndInitiateCheckoutAsync(
        GoogleFormOrderCreationRequest request,
        CancellationToken cancellationToken = default);

    Task<GoogleFormOrderCreationResult?> FindExistingByResponseIdAsync(
        string responseId,
        CancellationToken cancellationToken = default);

    Task<(int Total, int Paid, int Pending)> GetGoogleFormOrderStatsAsync(
        CancellationToken cancellationToken = default);

    Task<GoogleFormOrderBridgeResult> BridgeSubmissionToOrderAsync(
        GoogleFormOrderBridgeRequest request,
        CancellationToken cancellationToken = default);
}

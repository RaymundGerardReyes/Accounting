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


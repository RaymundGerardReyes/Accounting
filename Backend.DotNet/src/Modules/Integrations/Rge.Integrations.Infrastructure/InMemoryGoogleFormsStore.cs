using System.Collections.Concurrent;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Infrastructure;

public sealed class InMemoryGoogleFormsStore : IGoogleFormsSubmissionStore
{
    private readonly ConcurrentDictionary<string, GoogleFormSubmission> _byResponseId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISecretStore _secretStore;

    public InMemoryGoogleFormsStore(ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        _secretStore = secretStore;
    }

    public Task<GoogleFormsStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        int total = _byResponseId.Count;
        return Task.FromResult(new GoogleFormsStatusResponse(
            Status: "active",
            IngestedOrdersCount: total,
            PaidOrdersCount: 0,
            PendingOrdersCount: total,
            SecretConfigured: _secretStore.HasGoogleFormsSecret()));
    }

    public Task SaveSubmissionAsync(GoogleFormSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        _byResponseId[submission.ResponseId] = submission;
        return Task.CompletedTask;
    }

    public Task<GoogleFormSubmission?> GetByResponseIdAsync(string responseId, CancellationToken cancellationToken = default)
    {
        _byResponseId.TryGetValue(responseId, out GoogleFormSubmission? found);
        return Task.FromResult(found);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_byResponseId.Count);
}


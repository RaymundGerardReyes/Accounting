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
        int paid = _byResponseId.Values.Count(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase));
        int pending = total - paid;
        var lastSynced = _byResponseId.Values.Count > 0 ? _byResponseId.Values.Max(x => x.SubmittedAtUtc) : (DateTimeOffset?)null;
        return Task.FromResult(new GoogleFormsStatusResponse(
            Status: "active",
            TotalSubmissions: total,
            SyncedCount: total,
            LastSyncedAt: lastSynced,
            IngestedOrdersCount: total,
            PaidOrdersCount: paid,
            PendingOrdersCount: pending,
            SecretConfigured: _secretStore.HasGoogleFormsSecret()));
    }

    public Task<GoogleFormsStatusSnapshot> GetStatusSnapshotAsync(int maxRecentLimit = 200, CancellationToken cancellationToken = default)
    {
        var list = _byResponseId.Values.OrderByDescending(x => x.SubmittedAtUtc).ToList();
        int total = list.Count;
        int paid = list.Count(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase));
        int cancelled = list.Count(x => string.Equals(x.Status, "cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "expired", StringComparison.OrdinalIgnoreCase));
        int pending = Math.Max(0, total - paid - cancelled);
        var lastSynced = list.Count > 0 ? list[0].SubmittedAtUtc : (DateTimeOffset?)null;
        var recentDtos = list.Take(Math.Clamp(maxRecentLimit, 1, 500)).Select(x => new GoogleFormSubmissionDto(
            OrderId: x.OrderId ?? $"ord_{x.ResponseId}",
            OrderNumber: x.OrderNumber ?? $"ORD-{x.SubmittedAtUtc:yyyyMM}-{x.ResponseId[..Math.Min(4, x.ResponseId.Length)]}",
            ResponseId: x.ResponseId,
            CustomerName: x.CustomerName,
            CustomerEmail: x.CustomerEmail,
            Description: x.ItemReference,
            AmountCentavos: x.AmountCentavos,
            Status: x.Status,
            CreatedAt: x.SubmittedAtUtc)).ToList();

        return Task.FromResult(new GoogleFormsStatusSnapshot(
            TotalSubmissions: total,
            SyncedCount: total,
            IngestedOrdersCount: total,
            PaidOrdersCount: paid,
            PendingOrdersCount: pending,
            CancelledOrdersCount: cancelled,
            LastSyncedAt: lastSynced,
            RecentSubmissions: recentDtos));
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

    public Task UpsertAsync(GoogleFormSubmission submission, CancellationToken cancellationToken = default) =>
        SaveSubmissionAsync(submission, cancellationToken);

    public Task<IReadOnlyList<GoogleFormSubmission>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GoogleFormSubmission>>(_byResponseId.Values.ToList());

    public Task<IReadOnlyList<GoogleFormSubmission>> GetRecentSubmissionsAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GoogleFormSubmission>>(
            _byResponseId.Values.OrderByDescending(x => x.SubmittedAtUtc).Take(limit <= 0 ? 50 : limit).ToList());
}


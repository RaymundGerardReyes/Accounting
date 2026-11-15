using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Audit.Application.Ports;

namespace Rge.Audit.Application.QueryAuditEvents;

public sealed record QueryAuditEventsQuery(
    string? EntityType = null,
    string? EntityId = null,
    int Limit = 100,
    string? ActorId = null,
    IReadOnlySet<string>? ActorRoles = null) : IQuery<AuditEventsResponse>;

public sealed record AuditEventsResponse(
    ChainIntegrityDto ChainIntegrity,
    IReadOnlyList<AuditEventItemDto> Events);

public sealed class QueryAuditEventsHandler : IQueryHandler<QueryAuditEventsQuery, AuditEventsResponse>
{
    private sealed record ChainCacheSnapshot(DateTimeOffset TimestampUtc, string Fingerprint, ChainIntegrityDto Integrity);
    private static volatile ChainCacheSnapshot? _chainCacheSnapshot;

    private static readonly HashSet<string> PrivilegedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "supervisor", "finance_reviewer", "finance_controller", "auditor", "bank_approver", "system_admin"
    };

    private readonly IAuditReader _auditReader;
    private readonly IChainVerifier _chainVerifier;

    public QueryAuditEventsHandler(IAuditReader auditReader, IChainVerifier chainVerifier)
    {
        ArgumentNullException.ThrowIfNull(auditReader);
        ArgumentNullException.ThrowIfNull(chainVerifier);

        _auditReader = auditReader;
        _chainVerifier = chainVerifier;
    }

    public async Task<Result<AuditEventsResponse>> HandleAsync(
        QueryAuditEventsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        bool isPrivileged = query.ActorRoles != null && query.ActorRoles.Any(r => PrivilegedRoles.Contains(r));
        string? restrictedActorId = isPrivileged ? null : query.ActorId;

        int effectiveLimit = Math.Clamp(query.Limit, 1, 500);

        var events = await _auditReader.QueryEventsAsync(query.EntityType, query.EntityId, effectiveLimit, restrictedActorId, cancellationToken).ConfigureAwait(false);

        ChainIntegrityDto chain;
        string fingerprint = ComputeEventsFingerprint(events);
        var snapshot = _chainCacheSnapshot;
        if (snapshot != null &&
            DateTimeOffset.UtcNow - snapshot.TimestampUtc < TimeSpan.FromSeconds(5) &&
            string.Equals(snapshot.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            chain = snapshot.Integrity;
        }
        else
        {
            chain = await _chainVerifier.VerifyChainAsync(cancellationToken).ConfigureAwait(false);
            _chainCacheSnapshot = new ChainCacheSnapshot(DateTimeOffset.UtcNow, fingerprint, chain);
        }

        var response = new AuditEventsResponse(chain, events);
        return Result.Success(response);
    }

    private static string ComputeEventsFingerprint(IReadOnlyList<AuditEventItemDto> events)
    {
        if (events.Count == 0)
        {
            return "0:empty";
        }

        var first = events[0];
        var last = events[^1];
        return $"{events.Count}:{first.SequenceNo}:{first.RecordHash}:{last.SequenceNo}:{last.RecordHash}";
    }
}


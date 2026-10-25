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
        var chain = await _chainVerifier.VerifyChainAsync(cancellationToken).ConfigureAwait(false);

        var response = new AuditEventsResponse(chain, events);
        return Result.Success(response);
    }
}


namespace Rge.Audit.Application.Ports;

public sealed record ChainIntegrityDto(
    bool Valid,
    long EventCount,
    string HeadHash,
    long? BrokenAtSequence = null,
    string? Reason = null);

public sealed record AuditEventItemDto(
    string Id,
    long SequenceNo,
    string ActorId,
    string ActorRole,
    string Action,
    string EntityType,
    string EntityId,
    string? PreviousState,
    string? NewState,
    string? Reason,
    string? CorrelationId,
    string? EvidenceRef,
    DateTimeOffset OccurredAt,
    string PrevHash,
    string RecordHash);

public interface IChainVerifier
{
    Task<ChainIntegrityDto> VerifyChainAsync(CancellationToken cancellationToken = default);
}

public interface IAuditReader
{
    Task<IReadOnlyList<AuditEventItemDto>> QueryEventsAsync(
        string? entityType,
        string? entityId,
        int limit,
        string? restrictedActorId,
        CancellationToken cancellationToken = default);
}


namespace Rge.Audit.Contracts;

/// <summary>
/// Data payload for appending a new immutable financial audit log event.
/// </summary>
public sealed record AuditTrailEntry(
    string ActorId,
    string ActorRole,
    string Action,
    string EntityType,
    string EntityId,
    string? PreviousState = null,
    string? NewState = null,
    string? Reason = null,
    string? CorrelationId = null,
    string? EvidenceRef = null);

/// <summary>
/// Result returned after successfully appending an audit record into the cryptographic hash chain.
/// </summary>
public sealed record AuditRecordResult(
    string Id,
    long SequenceNo,
    string PrevHash,
    string RecordHash,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Authoritative cross-module contract for recording audit events into the append-only hash chain.
/// </summary>
public interface IAuditTrail
{
    Task<AuditRecordResult> RecordAsync(AuditTrailEntry entry, CancellationToken cancellationToken = default);
}


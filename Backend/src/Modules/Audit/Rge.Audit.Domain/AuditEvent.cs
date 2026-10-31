using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Audit.Domain;

/// <summary>
/// Immutable aggregate root representing a cryptographically linked financial audit record.
/// Each record seals a SHA-256 digest of its payload and previous hash, forming an append-only hash chain.
/// </summary>
public sealed class AuditEvent : AggregateRoot<string>
{
    public long SequenceNo { get; }
    public string ActorId { get; }
    public string ActorRole { get; }
    public string Action { get; }
    public string EntityType { get; }
    public string EntityId { get; }
    public string? PreviousState { get; }
    public string? NewState { get; }
    public string? Reason { get; }
    public string? CorrelationId { get; }
    public string? EvidenceRef { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public AuditHash PrevHash { get; }
    public AuditHash RecordHash { get; }

    public AuditEvent(
        string id,
        long sequenceNo,
        string actorId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? previousState,
        string? newState,
        string? reason,
        string? correlationId,
        string? evidenceRef,
        DateTimeOffset occurredAtUtc,
        AuditHash prevHash,
        AuditHash recordHash)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequenceNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        SequenceNo = sequenceNo;
        ActorId = actorId;
        ActorRole = actorRole;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        PreviousState = previousState;
        NewState = newState;
        Reason = reason;
        CorrelationId = correlationId;
        EvidenceRef = evidenceRef;
        OccurredAtUtc = occurredAtUtc;
        PrevHash = prevHash;
        RecordHash = recordHash;
    }

    public static AuditEvent Create(
        long sequenceNo,
        string actorId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? previousState,
        string? newState,
        string? reason,
        string? correlationId,
        string? evidenceRef,
        DateTimeOffset occurredAtUtc,
        AuditHash prevHash,
        string? id = null)
    {
        string eventId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
        AuditHash recordHash = ComputeRecordHash(
            sequenceNo,
            actorId,
            actorRole,
            action,
            entityType,
            entityId,
            previousState,
            newState,
            reason,
            correlationId,
            evidenceRef,
            occurredAtUtc,
            prevHash);

        return new AuditEvent(
            eventId,
            sequenceNo,
            actorId,
            actorRole,
            action,
            entityType,
            entityId,
            previousState,
            newState,
            reason,
            correlationId,
            evidenceRef,
            occurredAtUtc,
            prevHash,
            recordHash);
    }

    public static AuditHash ComputeRecordHash(
        long sequenceNo,
        string actorId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? previousState,
        string? newState,
        string? reason,
        string? correlationId,
        string? evidenceRef,
        DateTimeOffset occurredAtUtc,
        AuditHash prevHash)
    {
        string occurredIso = occurredAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
        return ComputeRecordHash(
            sequenceNo,
            actorId,
            actorRole,
            action,
            entityType,
            entityId,
            previousState,
            newState,
            reason,
            correlationId,
            evidenceRef,
            occurredIso,
            prevHash);
    }

    public static AuditHash ComputeRecordHash(
        long sequenceNo,
        string actorId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? previousState,
        string? newState,
        string? reason,
        string? correlationId,
        string? evidenceRef,
        string occurredIso,
        AuditHash prevHash)
    {
        string payload = string.Join('|',
            sequenceNo.ToString(CultureInfo.InvariantCulture),
            actorId ?? string.Empty,
            actorRole ?? string.Empty,
            action ?? string.Empty,
            entityType ?? string.Empty,
            entityId ?? string.Empty,
            previousState ?? string.Empty,
            newState ?? string.Empty,
            reason ?? string.Empty,
            correlationId ?? string.Empty,
            evidenceRef ?? string.Empty,
            occurredIso ?? string.Empty,
            prevHash.Value);

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        string hashHex = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new AuditHash(hashHex);
    }

    public static string BuildCanonicalChainPayload(
        long sequenceNo,
        string actorId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? previousState,
        string? newState,
        string? reason,
        string? correlationId,
        string? evidenceRef,
        DateTimeOffset occurredAtUtc,
        string prevHash)
    {
        string occurredIso = occurredAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
        return string.Join('|',
            sequenceNo.ToString(CultureInfo.InvariantCulture),
            actorId ?? string.Empty,
            actorRole ?? string.Empty,
            action ?? string.Empty,
            entityType ?? string.Empty,
            entityId ?? string.Empty,
            previousState ?? string.Empty,
            newState ?? string.Empty,
            reason ?? string.Empty,
            correlationId ?? string.Empty,
            evidenceRef ?? string.Empty,
            occurredIso,
            prevHash ?? string.Empty);
    }
}


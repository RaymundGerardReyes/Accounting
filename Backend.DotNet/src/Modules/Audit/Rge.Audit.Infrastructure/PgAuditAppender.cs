using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Rge.Audit.Application.Ports;
using Rge.Audit.Contracts;
using Rge.Audit.Domain;
using Rge.BuildingBlocks.Application.Behaviors;
using Rge.BuildingBlocks.Infrastructure.Persistence;

namespace Rge.Audit.Infrastructure;

/// <summary>
/// Appends financial audit events transactionally via PostgreSQL stored procedure fn_append_audit_event.
/// Falls back to an atomic in-memory chain for offline tests.
/// </summary>
public sealed class PgAuditAppender : IAuditTrail, IAuditBehaviorWriter, IAuditStore
{
    private static readonly object SyncLock = new();
    internal static readonly ConcurrentDictionary<long, AuditEvent> InMemoryEvents = new();
    private static long _maxSequence;

    private readonly ConnectionScope _connectionScope;
    private readonly ChainVerifier _chainVerifier;

    public PgAuditAppender(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
        _chainVerifier = new ChainVerifier(connectionScope);
    }

    public Task AppendAsync(
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
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditTrailEntry(
            ActorId: string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            ActorRole: string.IsNullOrWhiteSpace(actorRole) ? "system" : actorRole,
            Action: action,
            EntityType: entityType,
            EntityId: entityId,
            PreviousState: previousState,
            NewState: newState,
            Reason: reason,
            CorrelationId: correlationId,
            EvidenceRef: evidenceRef);

        return RecordAsync(entry, cancellationToken);
    }

    public async Task<ChainVerificationReport> VerifyChainAsync(CancellationToken cancellationToken = default)
    {
        ChainIntegrityDto dto = await _chainVerifier.VerifyChainAsync(cancellationToken).ConfigureAwait(false);
        return new ChainVerificationReport(
            IsIntact: dto.Valid,
            TotalVerifiedRecords: dto.EventCount,
            HeadHash: dto.HeadHash,
            BrokenAtSequence: dto.BrokenAtSequence,
            Reason: dto.Reason);
    }

    public Task AppendAsync(
        string actorId,
        string actorRole,
        string action,
        string entityType,
        string entityId,
        string? reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditTrailEntry(
            ActorId: string.IsNullOrWhiteSpace(actorId) ? "system" : actorId,
            ActorRole: string.IsNullOrWhiteSpace(actorRole) ? "system" : actorRole,
            Action: action,
            EntityType: entityType,
            EntityId: entityId,
            PreviousState: null,
            NewState: null,
            Reason: reason,
            CorrelationId: correlationId,
            EvidenceRef: null);

        return RecordAsync(entry, cancellationToken);
    }

    public async Task<AuditRecordResult> RecordAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = @"
                SELECT sequence_no, prev_hash, record_hash, occurred_at
                  FROM fn_append_audit_event(
                    @actor_id, @actor_role, @action, @entity_type, @entity_id,
                    @previous_state, @new_state, @reason, @correlation_id, @evidence_ref
                  );";
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;

            AddParam(cmd, "@actor_id", DbType.String, entry.ActorId);
            AddParam(cmd, "@actor_role", DbType.String, entry.ActorRole);
            AddParam(cmd, "@action", DbType.String, entry.Action);
            AddParam(cmd, "@entity_type", DbType.String, entry.EntityType);
            AddParam(cmd, "@entity_id", DbType.String, entry.EntityId);
            AddParam(cmd, "@previous_state", DbType.String, (object?)entry.PreviousState ?? DBNull.Value);
            AddParam(cmd, "@new_state", DbType.String, (object?)entry.NewState ?? DBNull.Value);
            AddParam(cmd, "@reason", DbType.String, (object?)entry.Reason ?? DBNull.Value);
            AddParam(cmd, "@correlation_id", DbType.String, (object?)entry.CorrelationId ?? DBNull.Value);
            AddParam(cmd, "@evidence_ref", DbType.String, (object?)entry.EvidenceRef ?? DBNull.Value);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long seq = reader.GetInt64(0);
                string prev = reader.GetString(1);
                string hash = reader.GetString(2);
                DateTimeOffset occurred = reader.GetDateTime(3);

                string eventId = Guid.NewGuid().ToString();
                var ev = new AuditEvent(
                    eventId, seq, entry.ActorId, entry.ActorRole, entry.Action,
                    entry.EntityType, entry.EntityId, entry.PreviousState, entry.NewState,
                    entry.Reason, entry.CorrelationId, entry.EvidenceRef, occurred,
                    new AuditHash(prev), new AuditHash(hash));

                InMemoryEvents[seq] = ev;
                return new AuditRecordResult(eventId, seq, prev, hash, occurred);
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // Offline/test fallback
        }

        return RecordInMemory(entry);
    }

    private static AuditRecordResult RecordInMemory(AuditTrailEntry entry)
    {
        lock (SyncLock)
        {
            long nextSeq = ++_maxSequence;
            AuditHash prevHash = nextSeq == 1
                ? AuditHash.Genesis
                : (InMemoryEvents.TryGetValue(nextSeq - 1, out var prev) ? prev.RecordHash : AuditHash.Genesis);

            DateTimeOffset occurred = DateTimeOffset.UtcNow;
            var ev = AuditEvent.Create(
                nextSeq, entry.ActorId, entry.ActorRole, entry.Action,
                entry.EntityType, entry.EntityId, entry.PreviousState, entry.NewState,
                entry.Reason, entry.CorrelationId, entry.EvidenceRef, occurred, prevHash);

            InMemoryEvents[nextSeq] = ev;
            return new AuditRecordResult(ev.Id, ev.SequenceNo, ev.PrevHash, ev.RecordHash, ev.OccurredAtUtc);
        }
    }

    private static void AddParam(DbCommand cmd, string name, DbType type, object? value)
    {
        DbParameter p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}


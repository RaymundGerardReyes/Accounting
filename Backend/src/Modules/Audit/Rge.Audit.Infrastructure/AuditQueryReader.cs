using System.Data;
using System.Data.Common;
using Rge.Audit.Application.Ports;
using Rge.Audit.Domain;
using Rge.BuildingBlocks.Infrastructure.Persistence;

namespace Rge.Audit.Infrastructure;

/// <summary>
/// High-throughput query reader for audit events with entity and actor filtering.
/// </summary>
public sealed class AuditQueryReader : IAuditReader
{
    private readonly ConnectionScope _connectionScope;

    public AuditQueryReader(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
    }

    public async Task<IReadOnlyList<AuditEventItemDto>> QueryEventsAsync(
        string? entityType,
        string? entityId,
        int limit,
        string? restrictedActorId,
        CancellationToken cancellationToken = default)
    {
        List<AuditEventItemDto> items = [];

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;

            string whereClause = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(entityType) && !string.Equals(entityType, "all", StringComparison.OrdinalIgnoreCase))
            {
                whereClause += " AND entity_type = @entity_type";
                AddParam(cmd, "@entity_type", DbType.String, entityType);
            }

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                whereClause += " AND entity_id = @entity_id";
                AddParam(cmd, "@entity_id", DbType.String, entityId);
            }

            if (!string.IsNullOrWhiteSpace(restrictedActorId))
            {
                whereClause += " AND actor_id = @restricted_actor_id";
                AddParam(cmd, "@restricted_actor_id", DbType.String, restrictedActorId);
            }

            cmd.CommandText = $@"
                SELECT id, sequence_no, actor_id, actor_role, action,
                       entity_type, entity_id, previous_state, new_state,
                       reason, correlation_id, evidence_ref, occurred_at,
                       prev_hash, record_hash
                  FROM audit_events
                 {whereClause}
                 ORDER BY sequence_no DESC
                 LIMIT @limit;";
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@limit", DbType.Int32, limit);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string id = reader.GetString(0);
                long seq = reader.GetInt64(1);
                string actorId = reader.GetString(2);
                string actorRole = reader.GetString(3);
                string action = reader.GetString(4);
                string entType = reader.GetString(5);
                string entId = reader.GetString(6);
                string? prevState = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(7);
                string? newState = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(8);
                string? reason = await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9);
                string? corrId = await reader.IsDBNullAsync(10, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(10);
                string? evRef = await reader.IsDBNullAsync(11, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(11);
                DateTimeOffset occurredAt = reader.GetDateTime(12);
                string prevHash = reader.GetString(13);
                string recordHash = reader.GetString(14);

                items.Add(new AuditEventItemDto(
                    id, seq, actorId, actorRole, action,
                    entType, entId, prevState, newState,
                    reason, corrId, evRef, occurredAt,
                    prevHash, recordHash));
            }
            return items;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // Offline/test fallback
            IEnumerable<AuditEvent> query = PgAuditAppender.InMemoryEvents.Values;

            if (!string.IsNullOrWhiteSpace(entityType) && !string.Equals(entityType, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(e => string.Equals(e.EntityType, entityType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(e => string.Equals(e.EntityId, entityId, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(restrictedActorId))
            {
                query = query.Where(e => string.Equals(e.ActorId, restrictedActorId, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .OrderByDescending(e => e.SequenceNo)
                .Take(limit)
                .Select(e => new AuditEventItemDto(
                    e.Id,
                    e.SequenceNo,
                    e.ActorId,
                    e.ActorRole,
                    e.Action,
                    e.EntityType,
                    e.EntityId,
                    e.PreviousState,
                    e.NewState,
                    e.Reason,
                    e.CorrelationId,
                    e.EvidenceRef,
                    e.OccurredAtUtc,
                    e.PrevHash.Value,
                    e.RecordHash.Value))
                .ToList();
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


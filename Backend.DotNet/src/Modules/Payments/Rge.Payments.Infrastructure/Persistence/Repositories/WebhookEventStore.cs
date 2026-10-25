using System.Collections.Concurrent;
using System.Data.Common;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Webhooks;

namespace Rge.Payments.Infrastructure.Persistence.Repositories;

public sealed class WebhookEventStore : IWebhookEventStore
{
    private const string InsertSql = """
        INSERT INTO webhook_events (
            id, provider, provider_event_id, event_type, payload_json,
            processing_status, received_at, processed_at, error_message
        ) VALUES (
            @id, @provider, @provider_event_id, @event_type, @payload_json::jsonb,
            @processing_status, @received_at, @processed_at, @error_message
        )
        ON CONFLICT (provider_event_id) DO UPDATE SET
            processing_status = EXCLUDED.processing_status,
            processed_at = EXCLUDED.processed_at,
            error_message = EXCLUDED.error_message;
        """;

    private const string ExistsSql = """
        SELECT 1
        FROM webhook_events
        WHERE provider_event_id = @provider_event_id
        LIMIT 1;
        """;

    private const string ListRecentSql = """
        SELECT id, provider, provider_event_id, event_type, payload_json,
               processing_status, received_at, processed_at, error_message
        FROM webhook_events
        ORDER BY received_at DESC
        LIMIT @limit;
        """;

    private static readonly ConcurrentDictionary<string, WebhookEvent> MemoryByEventId = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConnectionScope _connectionScope;

    public WebhookEventStore(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
    }

    public async Task<bool> ExistsAsync(string providerEventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            return false;
        }

        if (MemoryByEventId.ContainsKey(providerEventId.Trim()))
        {
            return true;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = ExistsSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@provider_event_id", providerEventId.Trim());

            object? scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return scalar is not null;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            return MemoryByEventId.ContainsKey(providerEventId.Trim());
        }
    }

    public async Task<IReadOnlyList<WebhookEvent>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        int effectiveLimit = Math.Clamp(limit, 1, 100);

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = ListRecentSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@limit", effectiveLimit);

            List<WebhookEvent> list = [];
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var evt = new WebhookEvent(
                    id: reader.GetString(0),
                    provider: reader.GetString(1),
                    providerEventId: reader.GetString(2),
                    eventType: reader.GetString(3),
                    payloadJson: reader.GetString(4),
                    processingStatus: reader.GetString(5),
                    receivedAt: await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken).ConfigureAwait(false),
                    processedAt: await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false),
                    errorMessage: await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(8));

                MemoryByEventId[evt.ProviderEventId] = evt;
                list.Add(evt);
            }

            return list;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            return MemoryByEventId.Values
                .OrderByDescending(e => e.ReceivedAt)
                .Take(effectiveLimit)
                .ToList();
        }
    }

    public async Task SaveAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);

        MemoryByEventId[webhookEvent.ProviderEventId] = webhookEvent;

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = InsertSql;
            cmd.CommandTimeout = SqlTimeouts.StandardCommandSeconds;

            AddParam(cmd, "@id", webhookEvent.Id);
            AddParam(cmd, "@provider", webhookEvent.Provider);
            AddParam(cmd, "@provider_event_id", webhookEvent.ProviderEventId);
            AddParam(cmd, "@event_type", webhookEvent.EventType);
            AddParam(cmd, "@payload_json", webhookEvent.PayloadJson);
            AddParam(cmd, "@processing_status", webhookEvent.ProcessingStatus);
            AddParam(cmd, "@received_at", webhookEvent.ReceivedAt);
            AddParam(cmd, "@processed_at", webhookEvent.ProcessedAt);
            AddParam(cmd, "@error_message", webhookEvent.ErrorMessage);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }
}

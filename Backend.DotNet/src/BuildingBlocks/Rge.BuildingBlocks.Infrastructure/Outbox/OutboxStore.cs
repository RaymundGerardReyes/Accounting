using System.Data.Common;

using System.Text.Json;

using Rge.BuildingBlocks.Application.Messaging;

using Rge.BuildingBlocks.Infrastructure.Persistence;



namespace Rge.BuildingBlocks.Infrastructure.Outbox;



/// <summary>

/// High-throughput ADO.NET outbox persistence store using FOR UPDATE SKIP LOCKED

/// to prevent worker contention and guarantee at-least-once delivery.

/// </summary>

public sealed class OutboxStore : IOutboxStore, IOutboxWriter

{

    private const string EnqueueSql = """

        INSERT INTO fulfillment_outbox (

            id, order_id, payment_attempt_id, event_type,

            payload_json, correlation_id, status, created_at

        )

        VALUES (

            @id, @order_id, @payment_attempt_id, @event_type,

            @payload_json, @correlation_id, @status, @created_at

        );

        """;



    private const string ClaimBatchSql = """

        UPDATE fulfillment_outbox

        SET status = 'processing',

            locked_by = @worker_id,

            lease_expires_at = @lease_expires_at

        WHERE id IN (

            SELECT id

            FROM fulfillment_outbox

            WHERE status = 'pending'

               OR (status = 'processing' AND lease_expires_at < @now_utc)

            ORDER BY created_at ASC

            LIMIT @batch_size

            FOR UPDATE SKIP LOCKED

        )

        RETURNING id, order_id, payment_attempt_id, event_type,

                  payload_json, correlation_id, status, created_at,

                  processed_at, locked_by, lease_expires_at;

        """;



    private const string CompleteSql = """

        UPDATE fulfillment_outbox

        SET status = 'completed',

            processed_at = @processed_at,

            locked_by = NULL,

            lease_expires_at = NULL

        WHERE id = @id;

        """;



    private const string FailSql = """

        UPDATE fulfillment_outbox

        SET status = 'failed',

            processed_at = @failed_at,

            error_message = @error_message,

            locked_by = NULL,

            lease_expires_at = NULL

        WHERE id = @id;

        """;



    private readonly ConnectionScope _connectionScope;



    public OutboxStore(ConnectionScope connectionScope)

    {

        ArgumentNullException.ThrowIfNull(connectionScope);

        _connectionScope = connectionScope;

    }



    public async Task EnqueueAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)

        where TEvent : IIntegrationEvent

    {

        ArgumentNullException.ThrowIfNull(integrationEvent);



        string payloadJson = JsonSerializer.Serialize(integrationEvent);

        var message = new OutboxMessage(

            Id: integrationEvent.EventId.ToString("D"),

            OrderId: null,

            PaymentAttemptId: null,

            EventType: integrationEvent.EventType,

            PayloadJson: payloadJson,

            CorrelationId: integrationEvent.CorrelationId,

            Status: "pending",

            CreatedAtUtc: integrationEvent.OccurredAtUtc);



        await EnqueueRawAsync(message, cancellationToken).ConfigureAwait(false);

    }



    public async Task EnqueueRawAsync(OutboxMessage message, CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(message);



        DbConnection connection = await _connectionScope

            .GetOpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand cmd = connection.CreateCommand();

        cmd.Transaction = _connectionScope.CurrentTransaction;

        cmd.CommandText = EnqueueSql;

        cmd.CommandTimeout = SqlTimeouts.WebhookHotWriteSeconds;



        AddParam(cmd, "@id", message.Id);

        AddParam(cmd, "@order_id", message.OrderId);

        AddParam(cmd, "@payment_attempt_id", message.PaymentAttemptId);

        AddParam(cmd, "@event_type", message.EventType);

        AddParam(cmd, "@payload_json", message.PayloadJson);

        AddParam(cmd, "@correlation_id", message.CorrelationId);

        AddParam(cmd, "@status", message.Status);

        AddParam(cmd, "@created_at", message.CreatedAtUtc);



        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

    }



    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(

        string workerId,

        int batchSize,

        TimeSpan leaseDuration,

        DateTimeOffset nowUtc,

        CancellationToken cancellationToken = default)

    {

        DbConnection connection = await _connectionScope

            .GetOpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand cmd = connection.CreateCommand();

        cmd.CommandText = ClaimBatchSql;

        cmd.CommandTimeout = SqlTimeouts.StandardCommandSeconds;



        AddParam(cmd, "@worker_id", workerId);

        AddParam(cmd, "@lease_expires_at", nowUtc.Add(leaseDuration));

        AddParam(cmd, "@now_utc", nowUtc);

        AddParam(cmd, "@batch_size", batchSize);



        List<OutboxMessage> claimed = [];

        await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))

        {

            claimed.Add(new OutboxMessage(

                Id: reader.GetString(0),

                OrderId: await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(1),

                PaymentAttemptId: await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2),

                EventType: reader.GetString(3),

                PayloadJson: reader.GetString(4),

                CorrelationId: reader.GetString(5),

                Status: reader.GetString(6),

                CreatedAtUtc: await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false),

                ProcessedAtUtc: await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(8, cancellationToken).ConfigureAwait(false),

                LockedByWorker: await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9),

                LeaseExpiresAtUtc: await reader.IsDBNullAsync(10, cancellationToken).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(10, cancellationToken).ConfigureAwait(false)));

        }



        return claimed;

    }



    public async Task MarkCompletedAsync(

        string messageId,

        DateTimeOffset processedAtUtc,

        CancellationToken cancellationToken = default)

    {

        DbConnection connection = await _connectionScope

            .GetOpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand cmd = connection.CreateCommand();

        cmd.CommandText = CompleteSql;

        AddParam(cmd, "@id", messageId);

        AddParam(cmd, "@processed_at", processedAtUtc);



        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

    }



    public async Task MarkFailedAsync(

        string messageId,

        string errorReason,

        DateTimeOffset failedAtUtc,

        CancellationToken cancellationToken = default)

    {

        DbConnection connection = await _connectionScope

            .GetOpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand cmd = connection.CreateCommand();

        cmd.CommandText = FailSql;

        AddParam(cmd, "@id", messageId);

        AddParam(cmd, "@failed_at", failedAtUtc);

        AddParam(cmd, "@error_message", errorReason);



        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

    }



    private static void AddParam(DbCommand cmd, string name, object? value)

    {

        DbParameter param = cmd.CreateParameter();

        param.ParameterName = name;

        param.Value = value ?? DBNull.Value;

        cmd.Parameters.Add(param);

    }

}

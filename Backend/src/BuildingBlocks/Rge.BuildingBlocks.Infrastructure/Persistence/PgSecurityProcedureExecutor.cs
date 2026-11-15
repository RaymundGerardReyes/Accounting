using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Infrastructure.Persistence;

public sealed record AtomicWebhookDbOutcome(
    string OutcomeStatus,
    bool DuplicateIgnored,
    string? ResolvedOrderId,
    string? ResolvedAttemptId);

/// <summary>
/// Executes the hardened PostgreSQL SECURITY DEFINER stored procedures for
/// RBAC/SoD verification, single-round-trip PayMongo webhook ingestion, and set-based audit chain verification.
/// Features strongly-typed parameter binding, synchronous column reads, and a short-lived SoD decision cache.
/// </summary>
public sealed class PgSecurityProcedureExecutor
{
    private sealed record SodCacheKey(string ActorId, string? RequestorId, string Workflow, string Stage, string TargetDepartmentId, long AmountMinor);
    private sealed record SodCacheEntry(DateTimeOffset ExpiresAtUtc, Result Result);
    private static readonly ConcurrentDictionary<SodCacheKey, SodCacheEntry> SodCache = new();

    private const string EnforceSodSql = """
        SELECT is_authorized, error_code, error_message
        FROM fn_enforce_sod_and_approval_limit(
            @p_actor_id, @p_requestor_id, @p_workflow,
            @p_stage, @p_target_department_id, @p_amount_minor
        );
        """;

    private const string IngestWebhookAtomicSql = """
        SELECT outcome_status, duplicate_ignored, resolved_order_id, resolved_attempt_id
        FROM fn_ingest_paymongo_webhook_atomic(
            @p_webhook_id, @p_provider, @p_provider_event_id, @p_event_type,
            @p_raw_payload, @p_payment_attempt_id, @p_provider_payment_id,
            @p_actual_method, @p_outbox_id, @p_correlation_id
        );
        """;

    private readonly ConnectionScope _connectionScope;

    public PgSecurityProcedureExecutor(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
    }

    public static void InvalidateSodCache()
    {
        SodCache.Clear();
    }

    public async Task<Result> EnforceSodAndApprovalLimitAsync(
        string actorId,
        string? requestorId,
        string workflow,
        string stage,
        string targetDepartmentId,
        long amountMinor,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = new SodCacheKey(actorId, requestorId, workflow, stage, targetDepartmentId, amountMinor);
        if (SodCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Result;
        }

        DbConnection conn = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand cmd = conn.CreateCommand();
        cmd.Transaction = _connectionScope.CurrentTransaction;
        cmd.CommandText = EnforceSodSql;
        cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;

        AddParam(cmd, "@p_actor_id", DbType.String, actorId);
        AddParam(cmd, "@p_requestor_id", DbType.String, requestorId);
        AddParam(cmd, "@p_workflow", DbType.String, workflow);
        AddParam(cmd, "@p_stage", DbType.String, stage);
        AddParam(cmd, "@p_target_department_id", DbType.String, targetDepartmentId);
        AddParam(cmd, "@p_amount_minor", DbType.Int64, amountMinor);

        await using DbDataReader reader = await cmd
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure(Error.Forbidden("Security.VerificationFailed", "Database authorization check returned no rows."));
        }

        bool isAuthorized = reader.GetBoolean(0);
        if (isAuthorized)
        {
            var success = Result.Success();
            SodCache[cacheKey] = new SodCacheEntry(DateTimeOffset.UtcNow.AddSeconds(3), success);
            return success;
        }

        string code = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)
            ? "Security.Forbidden"
            : reader.GetString(1);
        string message = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
            ? "Access denied by database security policy."
            : reader.GetString(2);

        var failure = code.Contains("SoD", StringComparison.OrdinalIgnoreCase)
            ? Result.Failure(Error.SodViolation(code, message))
            : Result.Failure(Error.Forbidden(code, message));

        return failure;
    }

    public async Task<AtomicWebhookDbOutcome> IngestPayMongoWebhookAtomicAsync(
        string webhookId,
        string provider,
        string providerEventId,
        string eventType,
        string rawPayload,
        string? paymentAttemptId,
        string? providerPaymentId,
        string? actualMethod,
        string outboxId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        DbConnection conn = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand cmd = conn.CreateCommand();
        cmd.Transaction = _connectionScope.CurrentTransaction;
        cmd.CommandText = IngestWebhookAtomicSql;
        cmd.CommandTimeout = SqlTimeouts.WebhookHotWriteSeconds;

        AddParam(cmd, "@p_webhook_id", DbType.String, webhookId);
        AddParam(cmd, "@p_provider", DbType.String, provider);
        AddParam(cmd, "@p_provider_event_id", DbType.String, providerEventId);
        AddParam(cmd, "@p_event_type", DbType.String, eventType);
        AddParam(cmd, "@p_raw_payload", DbType.String, rawPayload);
        AddParam(cmd, "@p_payment_attempt_id", DbType.String, paymentAttemptId);
        AddParam(cmd, "@p_provider_payment_id", DbType.String, providerPaymentId);
        AddParam(cmd, "@p_actual_method", DbType.String, actualMethod);
        AddParam(cmd, "@p_outbox_id", DbType.String, outboxId);
        AddParam(cmd, "@p_correlation_id", DbType.String, correlationId);

        await using DbDataReader reader = await cmd
            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new AtomicWebhookDbOutcome("failed", false, null, null);
        }

        return new AtomicWebhookDbOutcome(
            OutcomeStatus: reader.GetString(0),
            DuplicateIgnored: reader.GetBoolean(1),
            ResolvedOrderId: await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2),
            ResolvedAttemptId: await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3));
    }

    private static void AddParam(DbCommand cmd, string name, DbType dbType, object? value)
    {
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.DbType = dbType;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }
}


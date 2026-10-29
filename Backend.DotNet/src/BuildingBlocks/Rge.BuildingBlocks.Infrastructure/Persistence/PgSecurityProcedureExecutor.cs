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
/// </summary>
public sealed class PgSecurityProcedureExecutor
{
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

    public async Task<Result> EnforceSodAndApprovalLimitAsync(
        string actorId,
        string? requestorId,
        string workflow,
        string stage,
        string targetDepartmentId,
        long amountMinor,
        CancellationToken cancellationToken = default)
    {
        DbConnection conn = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand cmd = conn.CreateCommand();
        cmd.Transaction = _connectionScope.CurrentTransaction;
        cmd.CommandText = EnforceSodSql;
        cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;

        AddParam(cmd, "@p_actor_id", actorId);
        AddParam(cmd, "@p_requestor_id", requestorId);
        AddParam(cmd, "@p_workflow", workflow);
        AddParam(cmd, "@p_stage", stage);
        AddParam(cmd, "@p_target_department_id", targetDepartmentId);
        AddParam(cmd, "@p_amount_minor", amountMinor);

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
            return Result.Success();
        }

        string code = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)
            ? "Security.Forbidden"
            : reader.GetString(1);
        string message = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)
            ? "Access denied by database security policy."
            : reader.GetString(2);

        return code.Contains("SoD", StringComparison.OrdinalIgnoreCase)
            ? Result.Failure(Error.SodViolation(code, message))
            : Result.Failure(Error.Forbidden(code, message));
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

        AddParam(cmd, "@p_webhook_id", webhookId);
        AddParam(cmd, "@p_provider", provider);
        AddParam(cmd, "@p_provider_event_id", providerEventId);
        AddParam(cmd, "@p_event_type", eventType);
        AddParam(cmd, "@p_raw_payload", rawPayload);
        AddParam(cmd, "@p_payment_attempt_id", paymentAttemptId);
        AddParam(cmd, "@p_provider_payment_id", providerPaymentId);
        AddParam(cmd, "@p_actual_method", actualMethod);
        AddParam(cmd, "@p_outbox_id", outboxId);
        AddParam(cmd, "@p_correlation_id", correlationId);

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

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }
}


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Rge.BuildingBlocks.Infrastructure.Observability;

public sealed record HandlerTraceNode(
    string ModuleName,
    string HandlerName,
    string MethodName,
    string RequestType,
    string OperationType,
    string ParametersJson,
    string ResultSummary,
    string Status,
    string? ErrorCode,
    double ElapsedMs,
    DateTimeOffset ExecutedAtUtc);

public sealed record SqlTraceNode(
    string ModuleName,
    string HandlerName,
    string RepositoryName,
    string OperationName,
    string SqlVerb,
    string CommandType,
    string SqlText,
    IReadOnlyList<string> ParameterNames,
    string BoundParametersJson,
    int? RowsReturned,
    int? RowsAffected,
    string Status,
    string? SqlState,
    double ElapsedMs,
    DateTimeOffset ExecutedAtUtc);

public sealed record DbTransactionTraceNode(
    string TransactionId,
    string EventType,
    string IsolationLevel,
    string Outcome,
    string? SqlState,
    double DurationMs,
    DateTimeOffset OccurredAtUtc);

public sealed class RuntimeTransactionEnvelope
{
    public string CorrelationId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string SpanId { get; init; } = string.Empty;
    public string Environment { get; init; } = "Development";
    public string ExecutionOrigin { get; init; } = "HTTP";
    public string HttpMethod { get; set; } = string.Empty;
    public string HttpRouteTemplate { get; set; } = string.Empty;
    public string HttpRawPath { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public int HttpStatusCode { get; set; }
    public string ActorId { get; set; } = "anonymous";
    public string ActorRole { get; set; } = "none";
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public double TotalDurationMs { get; set; }
    public ConcurrentQueue<HandlerTraceNode> Handlers { get; } = new();
    public ConcurrentQueue<SqlTraceNode> SqlQueries { get; } = new();
    public ConcurrentQueue<DbTransactionTraceNode> DatabaseTransactions { get; } = new();
}

public static class RuntimeExecutionStore
{
    private const int MaxRetainedTransactions = 300;
    private static readonly AsyncLocal<RuntimeTransactionEnvelope?> ActiveEnvelope = new();
    private static readonly ConcurrentQueue<RuntimeTransactionEnvelope> RecentTransactions = new();
    private static readonly ConcurrentDictionary<string, RuntimeTransactionEnvelope> ByCorrelationId = new(StringComparer.OrdinalIgnoreCase);

    public static RuntimeTransactionEnvelope? Current => RuntimeLogEnvironmentGate.IsDevelopmentFullBlast ? ActiveEnvelope.Value : null;

    public static RuntimeTransactionEnvelope? BeginTransactionEnvelope(
        string correlationId,
        string traceId,
        string spanId,
        string origin,
        string httpMethod,
        string rawPath,
        string? queryString)
    {
        // HARD BLOCK IN PRODUCTION
        if (RuntimeLogEnvironmentGate.IsProductionLocked)
        {
            ActiveEnvelope.Value = null;
            return null;
        }

        var envelope = new RuntimeTransactionEnvelope
        {
            CorrelationId = correlationId,
            TraceId = traceId,
            SpanId = spanId,
            Environment = RuntimeLogEnvironmentGate.EnvironmentName,
            ExecutionOrigin = origin,
            HttpMethod = httpMethod,
            HttpRawPath = rawPath,
            QueryString = queryString
        };

        ActiveEnvelope.Value = envelope;
        ByCorrelationId[correlationId] = envelope;
        RecentTransactions.Enqueue(envelope);

        while (RecentTransactions.Count > MaxRetainedTransactions && RecentTransactions.TryDequeue(out var oldest))
        {
            ByCorrelationId.TryRemove(oldest.CorrelationId, out _);
        }

        return envelope;
    }

    public static void RecordHandler(HandlerTraceNode node)
    {
        if (RuntimeLogEnvironmentGate.IsProductionLocked)
        {
            return;
        }

        ActiveEnvelope.Value?.Handlers.Enqueue(node);
    }

    public static void RecordSql(SqlTraceNode node)
    {
        if (RuntimeLogEnvironmentGate.IsProductionLocked)
        {
            return;
        }

        ActiveEnvelope.Value?.SqlQueries.Enqueue(node);
    }

    public static void RecordDbTransaction(DbTransactionTraceNode node)
    {
        if (RuntimeLogEnvironmentGate.IsProductionLocked)
        {
            return;
        }

        ActiveEnvelope.Value?.DatabaseTransactions.Enqueue(node);
    }

    public static void CompleteAndEmitFullBlastTree(
        ILogger logger,
        string routeTemplate,
        string actorId,
        string actorRole,
        int statusCode,
        double totalDurationMs)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (RuntimeLogEnvironmentGate.IsProductionLocked)
        {
            return;
        }

        var env = ActiveEnvelope.Value;
        if (env is null)
        {
            return;
        }

        env.HttpRouteTemplate = routeTemplate;
        env.ActorId = actorId;
        env.ActorRole = actorRole;
        env.HttpStatusCode = statusCode;
        env.TotalDurationMs = Math.Round(totalDurationMs, 2);
        env.CompletedAtUtc = DateTimeOffset.UtcNow;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"[DEV FULL-BLAST TRACE] CorrelationId={env.CorrelationId} TraceId={env.TraceId}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  HTTP     : {env.HttpMethod} {env.HttpRawPath}{env.QueryString} (Route={env.HttpRouteTemplate}) -> {env.HttpStatusCode} in {env.TotalDurationMs:0.00} ms"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  ACTOR    : ActorId={env.ActorId} | Roles=[{env.ActorRole}]"));

        var modules = env.Handlers.Select(h => h.ModuleName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  MODULES  : [{(modules.Length > 0 ? string.Join(", ", modules) : "None")}]"));

        foreach (var h in env.Handlers)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  ├─ [HANDLER] Module={h.ModuleName} | {h.HandlerName}.{h.MethodName}({h.RequestType}) [{h.OperationType}]"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  │  ├─ Params : {h.ParametersJson}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  │  └─ Result : Status={h.Status} {(h.ErrorCode is not null ? $"ErrorCode={h.ErrorCode} " : "")}Output={h.ResultSummary} ({h.ElapsedMs:0.00} ms)"));
        }

        foreach (var sql in env.SqlQueries)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  ├─ [SQL] Repo={sql.RepositoryName}.{sql.OperationName} | Verb={sql.SqlVerb} | Type={sql.CommandType} ({sql.ElapsedMs:0.00} ms)"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  │  ├─ BoundParams : Names=[{string.Join(", ", sql.ParameterNames)}] Values={sql.BoundParametersJson}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  │  ├─ Rows        : Returned={sql.RowsReturned?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | Affected={sql.RowsAffected?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | Status={sql.Status}{(sql.SqlState is not null ? $" (SqlState={sql.SqlState})" : "")}"));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  │  └─ Query       : {sql.SqlText}"));
        }

        foreach (var tx in env.DatabaseTransactions)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  └─ [DB-TX] TxId={tx.TransactionId} | Event={tx.EventType} | Isolation={tx.IsolationLevel} | Outcome={tx.Outcome} ({tx.DurationMs:0.00} ms)"));
        }

        sb.AppendLine("================================================================================");
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{FullBlastTraceTree}", sb.ToString());
        }

        ActiveEnvelope.Value = null;
    }

    public static IReadOnlyList<RuntimeTransactionEnvelope> QueryRecent(
        string? module = null,
        string? handler = null,
        string? correlationId = null,
        int limit = 50)
    {
        if (RuntimeLogEnvironmentGate.IsProductionLocked)
        {
            return Array.Empty<RuntimeTransactionEnvelope>();
        }

        var query = RecentTransactions.Reverse();

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(x => x.CorrelationId.Contains(correlationId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(module))
        {
            query = query.Where(x => x.Handlers.Any(h => h.ModuleName.Equals(module.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(handler))
        {
            query = query.Where(x => x.Handlers.Any(h => h.HandlerName.Contains(handler.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        return query.Take(Math.Clamp(limit, 1, MaxRetainedTransactions)).ToArray();
    }

    public static RuntimeTransactionEnvelope? GetByCorrelationId(string correlationId)
    {
        if (RuntimeLogEnvironmentGate.IsProductionLocked || string.IsNullOrWhiteSpace(correlationId))
        {
            return null;
        }

        return ByCorrelationId.TryGetValue(correlationId.Trim(), out var env) ? env : null;
    }

    public static void Clear()
    {
        while (RecentTransactions.TryDequeue(out _)) { }
        ByCorrelationId.Clear();
    }
}

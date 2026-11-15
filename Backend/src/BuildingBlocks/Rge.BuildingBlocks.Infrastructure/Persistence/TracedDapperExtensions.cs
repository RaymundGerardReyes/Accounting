using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rge.BuildingBlocks.Infrastructure.Observability;

namespace Rge.BuildingBlocks.Infrastructure.Persistence;

public static class TracedDapperExtensions
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static async Task<IReadOnlyList<T>> QueryTracedAsync<T>(
        this IDbConnection connection,
        ILogger logger,
        string repositoryName,
        CommandDefinition command,
        [CallerMemberName] string operationName = "")
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(repositoryName);

        using var activity = StartSqlActivity(repositoryName, operationName, command);
        var sw = Stopwatch.StartNew();

        try
        {
            var rows = (await connection.QueryAsync<T>(command).ConfigureAwait(false)).AsList();
            sw.Stop();

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: rows.Count, rowsAffected: null,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: true, sqlState: null, exceptionType: null);

            return rows;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var sqlState = (ex as PostgresException)?.SqlState;

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: null, rowsAffected: null,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: false, sqlState: sqlState, exceptionType: ex.GetType().Name);

            throw;
        }
    }

    public static async Task<T?> QueryFirstOrDefaultTracedAsync<T>(
        this IDbConnection connection,
        ILogger logger,
        string repositoryName,
        CommandDefinition command,
        [CallerMemberName] string operationName = "")
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(repositoryName);

        using var activity = StartSqlActivity(repositoryName, operationName, command);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await connection.QueryFirstOrDefaultAsync<T>(command).ConfigureAwait(false);
            sw.Stop();

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: result is null ? 0 : 1, rowsAffected: null,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: true, sqlState: null, exceptionType: null);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var sqlState = (ex as PostgresException)?.SqlState;

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: null, rowsAffected: null,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: false, sqlState: sqlState, exceptionType: ex.GetType().Name);

            throw;
        }
    }

    public static async Task<int> ExecuteTracedAsync(
        this IDbConnection connection,
        ILogger logger,
        string repositoryName,
        CommandDefinition command,
        [CallerMemberName] string operationName = "")
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(repositoryName);

        using var activity = StartSqlActivity(repositoryName, operationName, command);
        var sw = Stopwatch.StartNew();

        try
        {
            var rowsAffected = await connection.ExecuteAsync(command).ConfigureAwait(false);
            sw.Stop();

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: null, rowsAffected: rowsAffected,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: true, sqlState: null, exceptionType: null);

            return rowsAffected;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var sqlState = (ex as PostgresException)?.SqlState;

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: null, rowsAffected: null,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: false, sqlState: sqlState, exceptionType: ex.GetType().Name);

            throw;
        }
    }

    public static async Task<T?> ExecuteScalarTracedAsync<T>(
        this IDbConnection connection,
        ILogger logger,
        string repositoryName,
        CommandDefinition command,
        [CallerMemberName] string operationName = "")
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(repositoryName);

        using var activity = StartSqlActivity(repositoryName, operationName, command);
        var sw = Stopwatch.StartNew();

        try
        {
            var scalar = await connection.ExecuteScalarAsync<T>(command).ConfigureAwait(false);
            sw.Stop();

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: scalar is null ? 0 : 1, rowsAffected: null,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: true, sqlState: null, exceptionType: null);

            return scalar;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var sqlState = (ex as PostgresException)?.SqlState;

            LogSqlOutcome(
                logger, activity, repositoryName, operationName, command,
                rowsReturned: null, rowsAffected: null,
                elapsedMs: sw.Elapsed.TotalMilliseconds,
                succeeded: false, sqlState: sqlState, exceptionType: ex.GetType().Name);

            throw;
        }
    }

    private static Activity? StartSqlActivity(
        string repositoryName,
        string operationName,
        CommandDefinition command)
    {
        var activity = ActivitySources.Database.StartActivity(
            $"db.{repositoryName}.{operationName}",
            ActivityKind.Client);

        activity?.SetTag("db.system", "postgresql");
        activity?.SetTag("rge.repository", repositoryName);
        activity?.SetTag("rge.sql_operation", operationName);
        activity?.SetTag("db.command_type", (command.CommandType ?? CommandType.Text).ToString());

        return activity;
    }

    private static void LogSqlOutcome(
        ILogger logger,
        Activity? activity,
        string repositoryName,
        string operationName,
        CommandDefinition command,
        int? rowsReturned,
        int? rowsAffected,
        double elapsedMs,
        bool succeeded,
        string? sqlState,
        string? exceptionType)
    {
        var ctx = ExecutionContextAccessor.Instance.Current;
        var paramMeta = ParameterSanitizer.ExtractSqlParameters(command.Parameters);
        var compactSql = FormatSqlSummary(command.CommandText);
        var sqlVerb = ExtractSqlVerb(compactSql);

        activity?.SetTag("db.operation", sqlVerb);
        if (rowsReturned.HasValue) activity?.SetTag("db.rows_returned", rowsReturned.Value);
        if (rowsAffected.HasValue) activity?.SetTag("db.rows_affected", rowsAffected.Value);

        var status = succeeded ? "SUCCESS" : "FAILURE";

        // Record into in-memory full-blast store when in Development
        RuntimeExecutionStore.RecordSql(new SqlTraceNode(
            ModuleName: ctx.ModuleName ?? "Unknown",
            HandlerName: ctx.HandlerName ?? "Unknown",
            RepositoryName: repositoryName,
            OperationName: operationName,
            SqlVerb: sqlVerb,
            CommandType: (command.CommandType ?? CommandType.Text).ToString(),
            SqlText: compactSql,
            ParameterNames: paramMeta.ParameterNames,
            BoundParametersJson: paramMeta.SanitizedValuesJson,
            RowsReturned: rowsReturned,
            RowsAffected: rowsAffected,
            Status: status,
            SqlState: sqlState,
            ElapsedMs: Math.Round(elapsedMs, 2),
            ExecutedAtUtc: DateTimeOffset.UtcNow));

        if (succeeded)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "[RUNTIME-SQL] CorrelationId={CorrelationId} TraceId={TraceId} SpanId={SpanId} " +
                    "Module={Module} Handler={Handler} Repository={Repository} Operation={Operation} " +
                    "SqlVerb={SqlVerb} CommandType={CommandType} ParameterNames=[{ParameterNames}] " +
                    "ApprovedParams={ApprovedParams} RowsReturned={RowsReturned} RowsAffected={RowsAffected} " +
                    "ElapsedMs={ElapsedMs:0.00} Status=SUCCESS Sql=\"{Sql}\"",
                    ctx.CorrelationId,
                    ctx.TraceId,
                    activity?.SpanId.ToString() ?? ctx.SpanId,
                    ctx.ModuleName ?? "Unknown",
                    ctx.HandlerName ?? "Unknown",
                    repositoryName,
                    operationName,
                    sqlVerb,
                    command.CommandType ?? CommandType.Text,
                    string.Join(",", paramMeta.ParameterNames),
                    paramMeta.SanitizedValuesJson,
                    rowsReturned?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
                    rowsAffected?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
                    elapsedMs,
                    compactSql);
            }
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, sqlState ?? exceptionType);
            activity?.SetTag("db.sql_state", sqlState ?? "UNKNOWN");

            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(
                    "[RUNTIME-SQL] CorrelationId={CorrelationId} TraceId={TraceId} SpanId={SpanId} " +
                    "Module={Module} Handler={Handler} Repository={Repository} Operation={Operation} " +
                    "SqlVerb={SqlVerb} CommandType={CommandType} ParameterNames=[{ParameterNames}] " +
                    "ApprovedParams={ApprovedParams} SqlState={SqlState} ExceptionType={ExceptionType} " +
                    "ElapsedMs={ElapsedMs:0.00} Status=FAILURE Sql=\"{Sql}\"",
                    ctx.CorrelationId,
                    ctx.TraceId,
                    activity?.SpanId.ToString() ?? ctx.SpanId,
                    ctx.ModuleName ?? "Unknown",
                    ctx.HandlerName ?? "Unknown",
                    repositoryName,
                    operationName,
                    sqlVerb,
                    command.CommandType ?? CommandType.Text,
                    string.Join(",", paramMeta.ParameterNames),
                    paramMeta.SanitizedValuesJson,
                    sqlState ?? "NONE",
                    exceptionType ?? "UnknownException",
                    elapsedMs,
                    compactSql);
            }
        }
    }

    private static string FormatSqlSummary(string rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
        {
            return string.Empty;
        }

        var compact = WhitespaceRegex.Replace(rawSql.Trim(), " ");
        return compact.Length <= 320 ? compact : compact[..320] + " ...";
    }

    private static string ExtractSqlVerb(string compactSql)
    {
        if (string.IsNullOrWhiteSpace(compactSql))
        {
            return "UNKNOWN";
        }

        var firstSpace = compactSql.IndexOf(' ', StringComparison.Ordinal);
        return (firstSpace > 0 ? compactSql[..firstSpace] : compactSql).ToUpperInvariant();
    }
}

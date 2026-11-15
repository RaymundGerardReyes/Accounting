using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Application.Behaviors;

public static class LoggingBehavior
{
    public static async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        ICurrentActor currentActor,
        RequestHandlerDelegate<TResponse> next,
        Action<string, string, double, bool, string?>? onCompletedLog = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentActor);
        ArgumentNullException.ThrowIfNull(next);

        string requestName = typeof(TRequest).Name;
        long startTimestamp = Stopwatch.GetTimestamp();

        Activity? activity = Activity.Current;
        activity?.SetTag("rge.request.name", requestName);
        activity?.SetTag("rge.actor.id", currentActor.ActorId);
        activity?.SetTag("rge.correlation.id", currentActor.CorrelationId);

        Result<TResponse> result = await next(cancellationToken).ConfigureAwait(false);

        double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        activity?.SetTag("rge.request.duration_ms", elapsedMs);
        activity?.SetTag("rge.request.succeeded", result.IsSuccess);

        onCompletedLog?.Invoke(
            requestName,
            currentActor.CorrelationId,
            elapsedMs,
            result.IsSuccess,
            result.IsFailure ? result.Error.Code : null);

        return result;
    }
}

public sealed class LoggingBehavior<TRequest, TResponse> where TRequest : notnull
{
    private static readonly ActivitySource ApplicationActivitySource = new("Rge.Accounting.Application", "1.0.0");
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentActor _actor;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        ICurrentActor actor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
    }

    public async Task<Result<TResponse>> HandleAsync(
        TRequest request,
        Type handlerType,
        Func<CancellationToken, Task<Result<TResponse>>> next,
        string sanitizedParamsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(next);

        var moduleName = ExtractModuleName(handlerType);
        var handlerName = handlerType.Name;
        var requestName = typeof(TRequest).Name;
        var operationType = ResolveOperationType(requestName);
        var actorId = _actor.IsAuthenticated && !string.IsNullOrWhiteSpace(_actor.ActorId) ? _actor.ActorId : "anonymous";
        var actorRole = _actor.Roles is { Count: > 0 } ? string.Join(",", _actor.Roles) : "none";

        using var activity = ApplicationActivitySource.StartActivity(
            $"{moduleName}.{handlerName}.HandleAsync",
            ActivityKind.Internal);

        var correlationId = activity?.GetTagItem("correlation.id")?.ToString() ?? Activity.Current?.RootId ?? Guid.NewGuid().ToString("N");
        activity?.SetTag("rge.module", moduleName);
        activity?.SetTag("rge.handler", handlerName);
        activity?.SetTag("rge.operation", requestName);
        activity?.SetTag("rge.operation_type", operationType);
        activity?.SetTag("rge.actor_id", actorId);
        activity?.SetTag("rge.actor_role", actorRole);

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = activity?.TraceId.ToString() ?? "no-trace",
            ["SpanId"] = activity?.SpanId.ToString() ?? "no-span",
            ["Module"] = moduleName,
            ["Handler"] = handlerName,
            ["Operation"] = requestName,
            ["OperationType"] = operationType,
            ["ActorId"] = actorId,
            ["ActorRole"] = actorRole
        });

        var sw = Stopwatch.StartNew();

        try
        {
            var result = await next(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            var elapsedMs = sw.Elapsed.TotalMilliseconds;

            if (result.IsSuccess)
            {
                var resultSummary = SummarizeValue(result.Value);
                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("rge.status", "SUCCESS");
                activity?.SetTag("rge.result_summary", resultSummary);

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "[RUNTIME-HANDLER] CorrelationId={CorrelationId} TraceId={TraceId} SpanId={SpanId} " +
                        "Module={Module} Handler={Handler} Method=HandleAsync Request={Operation} Type={OperationType} " +
                        "ActorId={ActorId} ActorRole={ActorRole} Parameters={Parameters} " +
                        "Status=SUCCESS Result={ResultSummary} ElapsedMs={ElapsedMs:0.00}",
                        correlationId,
                        activity?.TraceId.ToString() ?? "no-trace",
                        activity?.SpanId.ToString() ?? "no-span",
                        moduleName,
                        handlerName,
                        requestName,
                        operationType,
                        actorId,
                        actorRole,
                        sanitizedParamsJson,
                        resultSummary,
                        elapsedMs);
                }
            }
            else
            {
                var errorCode = result.Error.Code;
                activity?.SetStatus(ActivityStatusCode.Error, errorCode);
                activity?.SetTag("rge.status", "FAILURE");
                activity?.SetTag("rge.error_code", errorCode);

                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(
                        "[RUNTIME-HANDLER] CorrelationId={CorrelationId} TraceId={TraceId} SpanId={SpanId} " +
                        "Module={Module} Handler={Handler} Method=HandleAsync Request={Operation} Type={OperationType} " +
                        "ActorId={ActorId} ActorRole={ActorRole} Parameters={Parameters} " +
                        "Status=FAILURE ErrorCode={ErrorCode} ElapsedMs={ElapsedMs:0.00}",
                        correlationId,
                        activity?.TraceId.ToString() ?? "no-trace",
                        activity?.SpanId.ToString() ?? "no-span",
                        moduleName,
                        handlerName,
                        requestName,
                        operationType,
                        actorId,
                        actorRole,
                        sanitizedParamsJson,
                        errorCode,
                        elapsedMs);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            activity?.SetTag("rge.status", "EXCEPTION");
            activity?.SetTag("exception.type", ex.GetType().FullName);

            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex,
                    "[RUNTIME-HANDLER] CorrelationId={CorrelationId} TraceId={TraceId} SpanId={SpanId} " +
                    "Module={Module} Handler={Handler} Method=HandleAsync Request={Operation} Type={OperationType} " +
                    "ActorId={ActorId} ActorRole={ActorRole} Parameters={Parameters} " +
                    "Status=EXCEPTION ExceptionType={ExceptionType} ElapsedMs={ElapsedMs:0.00}",
                    correlationId,
                    activity?.TraceId.ToString() ?? "no-trace",
                    activity?.SpanId.ToString() ?? "no-span",
                    moduleName,
                    handlerName,
                    requestName,
                    operationType,
                    actorId,
                    actorRole,
                    sanitizedParamsJson,
                    ex.GetType().Name,
                    elapsedMs);
            }

            throw;
        }
    }

    private static string ExtractModuleName(Type handlerType)
    {
        var ns = handlerType.Namespace ?? string.Empty;
        var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0].Equals("Rge", StringComparison.OrdinalIgnoreCase) ? parts[1] : "Host";
    }

    private static string ResolveOperationType(string requestTypeName)
    {
        if (requestTypeName.EndsWith("Command", StringComparison.OrdinalIgnoreCase)) return "Command";
        if (requestTypeName.EndsWith("Query", StringComparison.OrdinalIgnoreCase)) return "Query";
        return "Operation";
    }

    private static string SummarizeValue(TResponse? response)
    {
        if (response is null) return "null";
        if (response is ICollection collection) return $"Count={collection.Count}";
        if (response is IEnumerable enumerable and not string)
        {
            var count = 0;
            foreach (var _ in enumerable) count++;
            return $"Count={count}";
        }
        return response.GetType().Name;
    }
}

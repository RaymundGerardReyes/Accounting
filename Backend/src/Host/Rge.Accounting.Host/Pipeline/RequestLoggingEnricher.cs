using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Observability;

namespace Rge.Accounting.Host.Pipeline;

public sealed class RequestLoggingEnricher
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingEnricher> _logger;

    public RequestLoggingEnricher(RequestDelegate next, ILogger<RequestLoggingEnricher> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context, ICurrentActor actor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actor);

        var correlationId = context.Items["CorrelationId"]?.ToString()
            ?? context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? context.TraceIdentifier
            ?? Guid.NewGuid().ToString("N");

        var activity = Activity.Current;
        activity?.SetTag("correlation.id", correlationId);

        var traceId = activity?.TraceId.ToString() ?? context.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        var spanId = activity?.SpanId.ToString() ?? "root";
        var method = context.Request.Method;
        var rawPath = context.Request.Path.Value ?? "/";

        var queryString = RuntimeLogEnvironmentGate.IsDevelopmentFullBlast ? context.Request.QueryString.Value : null;

        RuntimeExecutionStore.BeginTransactionEnvelope(
            correlationId: correlationId,
            traceId: traceId,
            spanId: spanId,
            origin: "HTTP",
            httpMethod: method,
            rawPath: rawPath,
            queryString: queryString);

        using var execScope = ExecutionContextAccessor.Instance.BeginScope(prev => prev with
        {
            CorrelationId = correlationId,
            TraceId = traceId,
            SpanId = spanId,
            ExecutionOrigin = "HTTP",
            HttpMethod = method,
            HttpRoute = rawPath
        });

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = traceId,
            ["SpanId"] = spanId
        });

        var sw = Stopwatch.StartNew();

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();

            // Resolve low-cardinality route template after endpoint routing has executed
            var endpoint = context.GetEndpoint() as RouteEndpoint;
            var routeTemplate = endpoint?.RoutePattern.RawText ?? rawPath;
            var actorId = actor.IsAuthenticated && !string.IsNullOrWhiteSpace(actor.ActorId) ? actor.ActorId : "anonymous";
            var actorRole = actor.Roles is { Count: > 0 } ? string.Join(",", actor.Roles) : "none";

            activity?.SetTag("http.route", routeTemplate);
            activity?.SetTag("rge.actor_id", actorId);
            activity?.SetTag("rge.actor_role", actorRole);

            if (RuntimeLogEnvironmentGate.IsDevelopmentFullBlast)
            {
                RuntimeExecutionStore.CompleteAndEmitFullBlastTree(
                    _logger,
                    routeTemplate: routeTemplate,
                    actorId: actorId,
                    actorRole: actorRole,
                    statusCode: context.Response.StatusCode,
                    totalDurationMs: sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                // Production Locked: emit only compact single-line HTTP summary without raw query strings or payloads
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "[HTTP-EXECUTION] CorrelationId={CorrelationId} TraceId={TraceId} Method={Method} " +
                        "Route={RouteTemplate} ActorId={ActorId} StatusCode={StatusCode} DurationMs={DurationMs:0.00}",
                        correlationId,
                        traceId,
                        method,
                        routeTemplate,
                        actorId,
                        context.Response.StatusCode,
                        sw.Elapsed.TotalMilliseconds);
                }
            }
        }
    }
}

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Api.Http;

namespace Rge.Accounting.Host.Pipeline;

/// <summary>
/// Propagates a unified Correlation ID across HTTP headers, OpenTelemetry Activity spans,
/// structured SIEM logs, and PostgreSQL audit_events rows.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = ResolveOrCreateCorrelationId(context);

        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ProblemDetailsFactory.CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static string ResolveOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(ProblemDetailsFactory.CorrelationIdHeader, out var incoming) &&
            !string.IsNullOrWhiteSpace(incoming))
        {
            string candidate = incoming.ToString().Trim();
            // Accept only safe alphanumeric / hyphen correlation IDs up to 64 chars
            if (candidate.Length <= 64 && candidate.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            {
                return candidate;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }
}


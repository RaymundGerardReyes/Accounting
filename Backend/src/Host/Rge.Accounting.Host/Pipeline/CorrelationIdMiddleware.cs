using System.Diagnostics;

using Rge.BuildingBlocks.Api.Http;

using Rge.BuildingBlocks.Infrastructure.Observability;



namespace Rge.Accounting.Host.Pipeline;



/// <summary>

/// Propagates a unified Correlation ID across HTTP headers, OpenTelemetry Activity spans,

/// structured SIEM logs, and PostgreSQL audit_events rows with minimal allocation.

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



        context.Response.OnStarting(static state =>

        {

            var httpContext = (HttpContext)state;

            httpContext.Response.Headers[ProblemDetailsFactory.CorrelationIdHeader] = httpContext.TraceIdentifier;

            return Task.CompletedTask;

        }, context);



        using (_logger.BeginScope(LogEnrichers.CreateRequestScope(correlationId, context.User.Identity?.Name)))

        {

            await _next(context).ConfigureAwait(false);

        }

    }



    private static string ResolveOrCreateCorrelationId(HttpContext context)

    {

        if (context.Request.Headers.TryGetValue(ProblemDetailsFactory.CorrelationIdHeader, out var incoming) &&

            !string.IsNullOrWhiteSpace(incoming))

        {

            ReadOnlySpan<char> candidate = incoming.ToString().AsSpan().Trim();

            if (IsSafeCorrelationId(candidate))

            {

                return candidate.ToString();

            }

        }



        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

    }



    private static bool IsSafeCorrelationId(ReadOnlySpan<char> candidate)

    {

        if (candidate.IsEmpty || candidate.Length > 64)

        {

            return false;

        }



        for (int i = 0; i < candidate.Length; i++)

        {

            char c = candidate[i];

            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')

            {

                return false;

            }

        }



        return true;

    }

}

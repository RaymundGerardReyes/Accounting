using System.Diagnostics;



namespace Rge.Accounting.Host.Pipeline;



/// <summary>

/// Measures end-to-end HTTP pipeline latency and logs structured request completion events.

/// </summary>

public sealed class RequestLoggingEnricher

{

    private readonly RequestDelegate _next;

    private readonly ILogger<RequestLoggingEnricher> _logger;



    public RequestLoggingEnricher(RequestDelegate next, ILogger<RequestLoggingEnricher> logger)

    {

        _next = next;

        _logger = logger;

    }



    public async Task InvokeAsync(HttpContext context)

    {

        ArgumentNullException.ThrowIfNull(context);



        long startTimestamp = Stopwatch.GetTimestamp();

        try

        {

            await _next(context).ConfigureAwait(false);

        }

        finally

        {

            if (_logger.IsEnabled(LogLevel.Information))

            {

                double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

                _logger.LogInformation(

                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:F2} ms [CorrelationId={CorrelationId}]",

                    context.Request.Method,

                    context.Request.Path,

                    context.Response.StatusCode,

                    elapsedMs,

                    context.TraceIdentifier);

            }

        }

    }

}

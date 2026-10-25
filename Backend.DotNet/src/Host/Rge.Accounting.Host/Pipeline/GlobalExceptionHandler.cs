using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Accounting.Host.Pipeline;

/// <summary>
/// Centralized exception handler that logs structured SIEM events while returning
/// zero-leak RFC 9457 ProblemDetails responses containing only the correlation_id.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        _logger.LogError(
            exception,
            "Unhandled exception occurred for {Method} {Path} [CorrelationId={CorrelationId}]",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        var genericError = new Error(
            "Server.InternalError",
            "An unexpected error occurred while processing the request. Reference the correlation_id for support.",
            ErrorType.Failure);

        var problem = ProblemDetailsFactory.Create(httpContext, genericError);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}


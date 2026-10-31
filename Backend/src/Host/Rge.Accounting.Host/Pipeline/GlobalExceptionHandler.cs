using Microsoft.AspNetCore.Diagnostics;

using Rge.BuildingBlocks.Api.Http;

using Rge.BuildingBlocks.Api.Serialization;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.Accounting.Host.Pipeline;



/// <summary>

/// Centralized exception handler that logs structured SIEM events while returning

/// zero-leak RFC 9457 ProblemDetails responses using source-generated JSON serialization.

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

        httpContext.Response.ContentType = "application/problem+json";



        await httpContext.Response

            .WriteAsJsonAsync(problem, AppJsonContext.Default.ProblemDetails, contentType: "application/problem+json", cancellationToken: cancellationToken)

            .ConfigureAwait(false);



        return true;

    }

}

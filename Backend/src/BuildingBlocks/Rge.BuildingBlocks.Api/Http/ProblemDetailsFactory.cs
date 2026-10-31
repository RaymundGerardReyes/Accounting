using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Mvc;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Api.Http;



/// <summary>

/// Builds RFC 9457 ProblemDetails responses containing the request's correlation_id

/// and never leaking internal stack traces.

/// </summary>

public static class ProblemDetailsFactory

{

    public const string CorrelationIdHeader = "X-Correlation-Id";



    public static ProblemDetails Create(HttpContext httpContext, Error error)

    {

        ArgumentNullException.ThrowIfNull(httpContext);

        ArgumentNullException.ThrowIfNull(error);



        int statusCode = MapStatusCode(error.Type);

        string correlationId = httpContext.TraceIdentifier;

        if (httpContext.Request.Headers.TryGetValue(CorrelationIdHeader, out var headerVal) &&

            !string.IsNullOrWhiteSpace(headerVal))

        {

            correlationId = headerVal.ToString();

        }

        var problem = new ProblemDetails

        {

            Status = statusCode,

            Title = MapTitle(error.Type),

            Type = $"https://rge-accounting.local/errors/{error.Code}",

            Detail = error.Message,

            Instance = httpContext.Request.Path

        };



        problem.Extensions["error_code"] = error.Code;

        problem.Extensions["correlation_id"] = correlationId;



        return problem;

    }



    public static int MapStatusCode(ErrorType errorType) =>

        errorType switch

        {

            ErrorType.Validation => StatusCodes.Status400BadRequest,

            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,

            ErrorType.Forbidden => StatusCodes.Status403Forbidden,

            ErrorType.SodViolation => StatusCodes.Status403Forbidden,

            ErrorType.NotFound => StatusCodes.Status404NotFound,

            ErrorType.Conflict => StatusCodes.Status409Conflict,

            ErrorType.PolicyViolation => StatusCodes.Status422UnprocessableEntity,

            _ => StatusCodes.Status500InternalServerError

        };



    private static string MapTitle(ErrorType errorType) =>

        errorType switch

        {

            ErrorType.Validation => "Bad Request",

            ErrorType.Unauthorized => "Unauthorized",

            ErrorType.Forbidden => "Forbidden",

            ErrorType.SodViolation => "Separation of Duties Violation",

            ErrorType.NotFound => "Resource Not Found",

            ErrorType.Conflict => "State Conflict",

            ErrorType.PolicyViolation => "Financial Policy Violation",

            _ => "Internal Server Error"

        };

}

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

        string baseUri = ResolveBaseUri(httpContext);

        var problem = new ProblemDetails

        {

            Status = statusCode,

            Title = MapTitle(error.Type),

            Type = $"{baseUri}/errors/{error.Code}",

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

    private static string ResolveBaseUri(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string scheme = httpContext.Request.Scheme;
        if (httpContext.Request.Headers.TryGetValue("X-Forwarded-Proto", out var fwdProto) &&
            !string.IsNullOrWhiteSpace(fwdProto))
        {
            string firstProto = fwdProto.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(firstProto))
            {
                scheme = firstProto;
            }
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            scheme = "https";
        }

        string? host = null;
        if (httpContext.Request.Headers.TryGetValue("X-Forwarded-Host", out var fwdHost) &&
            !string.IsNullOrWhiteSpace(fwdHost))
        {
            string firstHost = fwdHost.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(firstHost))
            {
                host = firstHost;
            }
        }

        if (string.IsNullOrWhiteSpace(host) && httpContext.Request.Host.HasValue)
        {
            host = httpContext.Request.Host.Value;
        }

        bool isLocalhost = false;
        if (!string.IsNullOrWhiteSpace(host))
        {
            string hostOnly = host.Split(':')[0].Trim();
            isLocalhost = string.Equals(hostOnly, "localhost", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(hostOnly, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(hostOnly, "::1", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(hostOnly, "0.0.0.0", StringComparison.OrdinalIgnoreCase);
        }

        if (isLocalhost)
        {
            return $"{scheme}://{host}";
        }

        string? configuredDomain = GetConfiguredProductionDomain(httpContext);
        if (!string.IsNullOrWhiteSpace(configuredDomain))
        {
            return configuredDomain.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(host))
        {
            return $"{scheme}://{host}";
        }

        return "https://rge-accounting.local";
    }

    private static string? GetConfiguredProductionDomain(HttpContext httpContext)
    {
        string[] envKeys = ["NEXT_PUBLIC_APP_DOMAIN", "APP_DOMAIN", "NEXT_PUBLIC_APP_URL", "FRONTEND_ORIGIN"];

        var configuration = httpContext.RequestServices?.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration)) as Microsoft.Extensions.Configuration.IConfiguration;
        if (configuration is not null)
        {
            foreach (string key in envKeys)
            {
                string? val = configuration[key];
                if (!string.IsNullOrWhiteSpace(val) && !IsLocalhostUri(val))
                {
                    return val;
                }
            }
        }

        foreach (string key in envKeys)
        {
            string? val = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(val) && !IsLocalhostUri(val))
            {
                return val;
            }
        }

        return null;
    }

    private static bool IsLocalhostUri(string uriString)
    {
        if (Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        }

        return uriString.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || uriString.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }
}

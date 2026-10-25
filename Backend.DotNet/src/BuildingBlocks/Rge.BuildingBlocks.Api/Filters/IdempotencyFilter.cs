using Microsoft.AspNetCore.Http;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.BuildingBlocks.Api.Filters;

/// <summary>
/// Minimal API endpoint filter validating the Idempotency-Key header on state-changing HTTP requests.
/// Enforces INV-PAY-002 constraints: non-empty, safe characters, maximum 128 chars length.
/// </summary>
public sealed class IdempotencyFilter : IEndpointFilter
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string AltIdempotencyHeader = "X-Idempotency-Key";
    public const string ContextItemKey = "IdempotencyKey";
    public const int MaxKeyLength = 128;

    private readonly bool _required;

    public IdempotencyFilter(bool required = false)
    {
        _required = required;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        string? key = null;

        if (httpContext.Request.Headers.TryGetValue(IdempotencyHeader, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            key = val.ToString().Trim();
        }
        else if (httpContext.Request.Headers.TryGetValue(AltIdempotencyHeader, out var altVal) && !string.IsNullOrWhiteSpace(altVal))
        {
            key = altVal.ToString().Trim();
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            if (_required)
            {
                var missingError = Error.Validation(
                    "Idempotency.KeyRequired",
                    $"The '{IdempotencyHeader}' header is mandatory for this operation.");
                return Results.Problem(ProblemDetailsFactory.Create(httpContext, missingError));
            }

            return await next(context);
        }

        if (key.Length > MaxKeyLength)
        {
            var lenError = Error.Validation(
                "Idempotency.KeyTooLong",
                $"Idempotency key exceeds maximum allowed length of {MaxKeyLength} characters.");
            return Results.Problem(ProblemDetailsFactory.Create(httpContext, lenError));
        }

        // Validate safe characters: printable ASCII, no control characters or CRLF
        if (key.Any(c => char.IsControl(c) || c > 127))
        {
            var invalidCharError = Error.Validation(
                "Idempotency.InvalidCharacters",
                "Idempotency key contains invalid control or non-ASCII characters.");
            return Results.Problem(ProblemDetailsFactory.Create(httpContext, invalidCharError));
        }

        httpContext.Items[ContextItemKey] = key;
        return await next(context);
    }
}


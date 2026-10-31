using System.Collections.Concurrent;

using Rge.BuildingBlocks.Api.Filters;

using Rge.BuildingBlocks.Api.Http;

using Rge.BuildingBlocks.Api.Serialization;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.Accounting.Host.Pipeline;



/// <summary>

/// Guards concurrent state-mutating requests carrying the same Idempotency-Key

/// from executing simultaneously (in-flight lock guard).

/// </summary>

public sealed class IdempotencyKeyMiddleware

{

    private readonly ConcurrentDictionary<string, DateTimeOffset> _inFlightKeys = new(StringComparer.Ordinal);

    private readonly RequestDelegate _next;



    public IdempotencyKeyMiddleware(RequestDelegate next)

    {

        _next = next;

    }



    public async Task InvokeAsync(HttpContext context)

    {

        ArgumentNullException.ThrowIfNull(context);



        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPatch(context.Request.Method))

        {

            await _next(context).ConfigureAwait(false);

            return;

        }



        string? rawKey = context.Request.Headers[IdempotencyFilter.IdempotencyHeaderName].FirstOrDefault()

                      ?? context.Request.Headers[IdempotencyFilter.AlternateHeaderName].FirstOrDefault();



        if (string.IsNullOrWhiteSpace(rawKey))

        {

            await _next(context).ConfigureAwait(false);

            return;

        }



        string scopedKey = $"{context.Request.Path}:{rawKey.Trim()}";

        if (!_inFlightKeys.TryAdd(scopedKey, DateTimeOffset.UtcNow))

        {

            var conflict = Error.Conflict(

                "Idempotency.RequestInFlight",

                "A request with the same Idempotency-Key is currently being processed.");

            var problem = ProblemDetailsFactory.Create(context, conflict);

            context.Response.StatusCode = StatusCodes.Status409Conflict;

            await context.Response

                .WriteAsJsonAsync(problem, AppJsonContext.Default.ProblemDetails, contentType: "application/problem+json", cancellationToken: context.RequestAborted)

                .ConfigureAwait(false);

            return;

        }



        try

        {

            await _next(context).ConfigureAwait(false);

        }

        finally

        {

            _inFlightKeys.TryRemove(scopedKey, out _);

        }

    }

}

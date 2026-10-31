using Microsoft.AspNetCore.Http;

using Rge.BuildingBlocks.Api.Http;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Api.Filters;



/// <summary>

/// Minimal API endpoint filter validating X-Idempotency-Key or Idempotency-Key headers

/// on state-mutating payment and disbursement endpoints.

/// </summary>

public sealed class IdempotencyFilter : IEndpointFilter

{

    public const string IdempotencyHeaderName = "Idempotency-Key";

    public const string AlternateHeaderName = "X-Idempotency-Key";



    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)

    {

        ArgumentNullException.ThrowIfNull(context);

        ArgumentNullException.ThrowIfNull(next);



        HttpRequest request = context.HttpContext.Request;

        if (HttpMethods.IsPost(request.Method) || HttpMethods.IsPatch(request.Method))

        {

            string? key = request.Headers[IdempotencyHeaderName].FirstOrDefault()

                       ?? request.Headers[AlternateHeaderName].FirstOrDefault();



            if (!string.IsNullOrWhiteSpace(key) && key.Length > 128)

            {

                var error = Error.Validation(

                    "Idempotency.KeyTooLong",

                    "Idempotency-Key cannot exceed 128 characters.");

                return Result.Failure(error).ToHttpResult(context.HttpContext);

            }

        }



        return await next(context).ConfigureAwait(false);

    }

}

using Microsoft.AspNetCore.Http;

using Rge.BuildingBlocks.Api.Http;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Api.Filters;



/// <summary>

/// Minimal API endpoint filter that rejects null request payloads or invalid states early.

/// </summary>

public sealed class ValidationFilter<TRequest> : IEndpointFilter

    where TRequest : class

{

    private readonly Func<TRequest, Result>? _validator;



    public ValidationFilter(Func<TRequest, Result>? validator = null)

    {

        _validator = validator;

    }



    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)

    {

        ArgumentNullException.ThrowIfNull(context);

        ArgumentNullException.ThrowIfNull(next);



        TRequest? request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)

        {

            var error = Error.Validation("Request.BodyRequired", "Request body is required and cannot be null.");

            return Result.Failure(error).ToHttpResult(context.HttpContext);

        }



        if (_validator is not null)

        {

            Result validation = _validator(request);

            if (validation.IsFailure)

            {

                return validation.ToHttpResult(context.HttpContext);

            }

        }



        return await next(context).ConfigureAwait(false);

    }

}

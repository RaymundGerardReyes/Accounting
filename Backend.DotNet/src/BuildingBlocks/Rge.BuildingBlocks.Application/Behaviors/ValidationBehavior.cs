using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Application.Behaviors;



/// <summary>

/// Executes synchronous, zero-reflection input validation before invoking the handler.

/// </summary>

public static class ValidationBehavior

{

    public static async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(

        TRequest request,

        IEnumerable<IValidator<TRequest>> validators,

        RequestHandlerDelegate<TResponse> next,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(validators);

        ArgumentNullException.ThrowIfNull(next);



        foreach (IValidator<TRequest> validator in validators)

        {

            Result validationResult = validator.Validate(request);

            if (validationResult.IsFailure)

            {

                return Result.Failure<TResponse>(validationResult.Error);

            }

        }



        return await next(cancellationToken).ConfigureAwait(false);

    }

}

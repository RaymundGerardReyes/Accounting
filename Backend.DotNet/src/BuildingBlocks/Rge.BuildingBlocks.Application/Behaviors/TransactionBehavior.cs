using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Application.Behaviors;



/// <summary>

/// Wraps state-mutating commands in an atomic database transaction,

/// committing only when Result.IsSuccess is true and rolling back on failure or exception.

/// </summary>

public static class TransactionBehavior

{

    public static async Task<Result<TResponse>> ExecuteAsync<TResponse>(

        IUnitOfWork unitOfWork,

        RequestHandlerDelegate<TResponse> next,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(unitOfWork);

        ArgumentNullException.ThrowIfNull(next);



        if (unitOfWork.HasActiveTransaction)

        {

            return await next(cancellationToken).ConfigureAwait(false);

        }



        await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try

        {

            Result<TResponse> response = await next(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccess)

            {

                await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            }

            else

            {

                await unitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);

            }



            return response;

        }

        catch

        {

            await unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            throw;

        }

    }

}

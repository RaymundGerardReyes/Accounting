using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Application.Behaviors;



/// <summary>

/// Enforces server-side deny-by-default authentication, RBAC role membership,

/// and department scope checks before executing an authorized command or query.

/// </summary>

public static class AuthorizationBehavior

{

    public static async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(

        TRequest request,

        ICurrentActor currentActor,

        RequestHandlerDelegate<TResponse> next,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(currentActor);

        ArgumentNullException.ThrowIfNull(next);



        if (request is IAuthorizedRequest authRequest)

        {

            if (!currentActor.IsAuthenticated)

            {

                return Result.Failure<TResponse>(

                    Error.Unauthorized("Security.Unauthenticated", "Authentication is required to perform this action."));

            }



            if (!string.IsNullOrWhiteSpace(authRequest.RequiredRole) &&

                !currentActor.HasRole(authRequest.RequiredRole))

            {

                return Result.Failure<TResponse>(

                    Error.Forbidden(

                        "Security.InsufficientRole",

                        $"Actor '{currentActor.ActorId}' lacks required role '{authRequest.RequiredRole}'."));

            }



            if (!string.IsNullOrWhiteSpace(authRequest.TargetDepartmentId) &&

                !currentActor.HasScopeForDepartment(authRequest.TargetDepartmentId))

            {

                return Result.Failure<TResponse>(

                    Error.Forbidden(

                        "Security.OutOfDepartmentScope",

                        $"Actor '{currentActor.ActorId}' does not have scope for department '{authRequest.TargetDepartmentId}'."));

            }

        }



        return await next(cancellationToken).ConfigureAwait(false);

    }

}

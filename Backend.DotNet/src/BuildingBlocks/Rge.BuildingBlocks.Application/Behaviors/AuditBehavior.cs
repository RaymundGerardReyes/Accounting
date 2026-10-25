using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;



namespace Rge.BuildingBlocks.Application.Behaviors;



public interface IAuditBehaviorWriter

{

    Task AppendAsync(

        string actorId,

        string actorRole,

        string action,

        string entityType,

        string entityId,

        string? reason,

        string correlationId,

        CancellationToken cancellationToken = default);

}



/// <summary>

/// Automatically records an audit trail entry with the current request's CorrelationId

/// whenever an IAuditableCommand succeeds.

/// </summary>

public static class AuditBehavior

{

    public static async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(

        TRequest request,

        ICurrentActor currentActor,

        IAuditBehaviorWriter auditWriter,

        RequestHandlerDelegate<TResponse> next,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(currentActor);

        ArgumentNullException.ThrowIfNull(auditWriter);

        ArgumentNullException.ThrowIfNull(next);



        Result<TResponse> result = await next(cancellationToken).ConfigureAwait(false);



        if (result.IsSuccess && request is IAuditableCommand auditable)

        {

            string primaryRole = currentActor.Roles.FirstOrDefault() ?? "anonymous";

            await auditWriter.AppendAsync(

                actorId: currentActor.ActorId,

                actorRole: primaryRole,

                action: auditable.AuditAction,

                entityType: auditable.EntityType,

                entityId: auditable.EntityId,

                reason: auditable.Reason,

                correlationId: currentActor.CorrelationId,

                cancellationToken: cancellationToken).ConfigureAwait(false);

        }



        return result;

    }

}

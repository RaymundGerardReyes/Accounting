using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;

using Rge.Identity.Domain;



namespace Rge.Identity.Application.ResolveActor;



public sealed record ResolveActorQuery(string ActorId) : IQuery<ResolvedActorResponse>;



public sealed record ResolvedActorRoleDto(string Role, string DepartmentScope);



public sealed record ResolvedActorResponse(

    string Id,

    string EmployeeCode,

    string FullName,

    string Email,

    string DepartmentId,

    string CostCenterId,

    string EmployeeGroup,

    string Status,

    IReadOnlyList<ResolvedActorRoleDto> RoleAssignments);



public sealed class ResolveActorHandler : IQueryHandler<ResolveActorQuery, ResolvedActorResponse>

{

    private readonly IRoleStore _roleStore;

    private readonly IActorCache _actorCache;



    public ResolveActorHandler(IRoleStore roleStore, IActorCache actorCache)

    {

        _roleStore = roleStore;

        _actorCache = actorCache;

    }



    public async Task<Result<ResolvedActorResponse>> HandleAsync(

        ResolveActorQuery query,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.ActorId))

        {

            return Result.Failure<ResolvedActorResponse>(

                Error.Unauthorized("Identity.ResolveActor.Unauthenticated", "Actor identifier is required."));

        }



        Actor? actor = await _actorCache

            .GetOrLoadActorAsync(query.ActorId, ct => _roleStore.GetActorByIdAsync(query.ActorId, ct), cancellationToken)

            .ConfigureAwait(false);



        if (actor is null)

        {

            return Result.Failure<ResolvedActorResponse>(

                Error.NotFound("Identity.ResolveActor.NotFound", $"Finance user '{query.ActorId}' was not found."));

        }



        List<ResolvedActorRoleDto> roles = actor.RoleAssignments

            .Where(r => r.IsActive)

            .Select(r => new ResolvedActorRoleDto(r.RoleName, r.DepartmentScope))

            .ToList();



        return Result.Success(new ResolvedActorResponse(

            Id: actor.Id,

            EmployeeCode: actor.EmployeeCode,

            FullName: actor.FullName,

            Email: actor.Email,

            DepartmentId: actor.DepartmentId,

            CostCenterId: actor.CostCenterId,

            EmployeeGroup: actor.EmployeeGroup,

            Status: actor.Status,

            RoleAssignments: roles));

    }

}

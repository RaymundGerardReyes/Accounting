using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;

using Rge.Identity.Domain;



namespace Rge.Identity.Application.GetSession;



public sealed record GetSessionQuery(string SessionTokenHash) : IQuery<SessionStatusResponse>;



public sealed record SessionStatusResponse(

    string SessionId,

    string UserId,

    string EmployeeCode,

    string FullName,

    string Email,

    string DepartmentId,

    string CostCenterId,

    IReadOnlySet<string> EffectiveRoles,

    string EffectiveDepartmentScope,

    DateTimeOffset ExpiresAtUtc);



public sealed class GetSessionHandler : IQueryHandler<GetSessionQuery, SessionStatusResponse>

{

    private readonly IAuthenticator _authenticator;

    private readonly IRoleStore _roleStore;

    private readonly IActorCache _actorCache;

    private readonly IClock _clock;



    public GetSessionHandler(

        IAuthenticator authenticator,

        IRoleStore roleStore,

        IActorCache actorCache,

        IClock clock)

    {

        _authenticator = authenticator;

        _roleStore = roleStore;

        _actorCache = actorCache;

        _clock = clock;

    }



    public async Task<Result<SessionStatusResponse>> HandleAsync(

        GetSessionQuery query,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.SessionTokenHash))

        {

            return Result.Failure<SessionStatusResponse>(

                Error.Unauthorized("Identity.Session.Missing", "No active session token provided."));

        }



        Session? session = await _authenticator

            .GetSessionByTokenHashAsync(query.SessionTokenHash, cancellationToken)

            .ConfigureAwait(false);



        DateTimeOffset nowUtc = _clock.UtcNow;

        if (session is null || !session.IsActive(nowUtc))

        {

            return Result.Failure<SessionStatusResponse>(

                Error.Unauthorized("Identity.Session.ExpiredOrInvalid", "Session has expired or has been revoked."));

        }



        Actor? actor = await _actorCache

            .GetOrLoadActorAsync(session.UserId, ct => _roleStore.GetActorByIdAsync(session.UserId, ct), cancellationToken)

            .ConfigureAwait(false);



        if (actor is null || !actor.IsActive)

        {

            return Result.Failure<SessionStatusResponse>(

                Error.Forbidden("Identity.Session.UserInactive", "Associated user account is no longer active."));

        }



        IReadOnlySet<string> effectiveRoles = !string.IsNullOrWhiteSpace(session.ActAsRole)

            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { session.ActAsRole }

            : actor.GetActiveRoleNames();



        string effectiveScope = !string.IsNullOrWhiteSpace(session.ActAsDepartmentScope)

            ? session.ActAsDepartmentScope

            : actor.GetPrimaryDepartmentScope();



        return Result.Success(new SessionStatusResponse(

            SessionId: session.Id,

            UserId: actor.Id,

            EmployeeCode: actor.EmployeeCode,

            FullName: actor.FullName,

            Email: actor.Email,

            DepartmentId: actor.DepartmentId,

            CostCenterId: actor.CostCenterId,

            EffectiveRoles: effectiveRoles,

            EffectiveDepartmentScope: effectiveScope,

            ExpiresAtUtc: session.ExpiresAtUtc));

    }

}

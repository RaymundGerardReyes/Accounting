using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;

using Rge.Identity.Domain;



namespace Rge.Identity.Application.ActAs;



public sealed record ActAsCommand(

    string SessionTokenHash,

    string? RoleName,

    string? DepartmentScope) : ICommand<ActAsResponse>;



public sealed record ActAsResponse(

    string UserId,

    string? ActAsRole,

    string? ActAsDepartmentScope);



public sealed class ActAsHandler : ICommandHandler<ActAsCommand, ActAsResponse>

{

    private readonly IAuthenticator _authenticator;

    private readonly IClock _clock;



    public ActAsHandler(IAuthenticator authenticator, IClock clock)

    {

        _authenticator = authenticator;

        _clock = clock;

    }



    public async Task<Result<ActAsResponse>> HandleAsync(

        ActAsCommand command,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(command);

#if !SANDBOX_TOOLS

        await Task.CompletedTask;

        return Result.Failure<ActAsResponse>(

            Error.Forbidden("Identity.ActAs.DisabledInProduction", "Act-As role override is disabled in production builds."));

#else

        if (!string.IsNullOrWhiteSpace(command.RoleName) && !Role.IsValidRoleName(command.RoleName))

        {

            return Result.Failure<ActAsResponse>(

                Error.Validation("Identity.ActAs.InvalidRole", $"Role '{command.RoleName}' is not a valid corporate role."));

        }



        Session? session = await _authenticator

            .GetSessionByTokenHashAsync(command.SessionTokenHash, cancellationToken)

            .ConfigureAwait(false);



        if (session is null || !session.IsActive(_clock.UtcNow))

        {

            return Result.Failure<ActAsResponse>(

                Error.Unauthorized("Identity.Session.Invalid", "Active session not found."));

        }



        session.ApplyDevActAsOverride(command.RoleName, command.DepartmentScope);

        await _authenticator.UpdateSessionOverrideAsync(session, cancellationToken).ConfigureAwait(false);



        return Result.Success(new ActAsResponse(

            UserId: session.UserId,

            ActAsRole: session.ActAsRole,

            ActAsDepartmentScope: session.ActAsDepartmentScope));

#endif

    }

}

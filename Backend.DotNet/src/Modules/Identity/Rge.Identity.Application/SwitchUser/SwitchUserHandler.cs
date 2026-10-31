using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Identity.Application.Ports;
using Rge.Identity.Domain;

namespace Rge.Identity.Application.SwitchUser;

public sealed record SwitchUserCommand(
    string SessionTokenHash,
    string TargetUserId) : ICommand<SwitchUserResponse>;

public sealed record SwitchUserResponse(
    string ActiveUserId,
    string FullName,
    string DepartmentId,
    IReadOnlySet<string> Roles);

public sealed class SwitchUserHandler : ICommandHandler<SwitchUserCommand, SwitchUserResponse>
{
    private readonly IAuthenticator _authenticator;
    private readonly IRoleStore _roleStore;
    private readonly IActorCache _actorCache;
    private readonly IClock _clock;

    public SwitchUserHandler(
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

    public async Task<Result<SwitchUserResponse>> HandleAsync(
        SwitchUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string? env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        bool isProduction = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);

#if !SANDBOX_TOOLS
        if (isProduction)
        {
            await Task.CompletedTask;
            return Result.Failure<SwitchUserResponse>(
                Error.Forbidden("Identity.SwitchUser.DisabledInProduction", "Persona switching is disabled in production environments."));
        }
#endif

        if (string.IsNullOrWhiteSpace(command.SessionTokenHash) || string.IsNullOrWhiteSpace(command.TargetUserId))
        {
            return Result.Failure<SwitchUserResponse>(
                Error.Validation("Identity.SwitchUser.InvalidParameters", "Session token and TargetUserId are required."));
        }

        Session? session = await _authenticator
            .GetSessionByTokenHashAsync(command.SessionTokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (session is null || !session.IsActive(_clock.UtcNow))
        {
            return Result.Failure<SwitchUserResponse>(
                Error.Unauthorized("Identity.Session.Invalid", "Active session not found."));
        }

        Actor? targetActor = await _actorCache
            .GetOrLoadActorAsync(command.TargetUserId, ct => _roleStore.GetActorByIdAsync(command.TargetUserId, ct), cancellationToken)
            .ConfigureAwait(false);

        if (targetActor is null || !targetActor.IsActive)
        {
            return Result.Failure<SwitchUserResponse>(
                Error.NotFound("Identity.SwitchUser.TargetNotFound", $"Target finance user '{command.TargetUserId}' not found or inactive."));
        }

        session.SwitchDevPersona(targetActor.Id);
        await _authenticator.UpdateSessionOverrideAsync(session, cancellationToken).ConfigureAwait(false);

        return Result.Success(new SwitchUserResponse(
            ActiveUserId: targetActor.Id,
            FullName: targetActor.FullName,
            DepartmentId: targetActor.DepartmentId,
            Roles: targetActor.GetActiveRoleNames()));
    }
}

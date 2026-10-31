using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;



namespace Rge.Identity.Application.Logout;



public sealed record LogoutCommand(string SessionTokenHash) : ICommand;



public sealed class LogoutHandler : ICommandHandler<LogoutCommand>

{

    private readonly IAuthenticator _authenticator;

    private readonly IClock _clock;



    public LogoutHandler(IAuthenticator authenticator, IClock clock)

    {

        _authenticator = authenticator;

        _clock = clock;

    }



    public async Task<Result> HandleAsync(

        LogoutCommand command,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(command);

        if (!string.IsNullOrWhiteSpace(command.SessionTokenHash))

        {

            await _authenticator

                .RevokeSessionAsync(command.SessionTokenHash, _clock.UtcNow, cancellationToken)

                .ConfigureAwait(false);

        }



        return Result.Success();

    }

}

using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;



namespace Rge.Identity.Application.CreateCsrfToken;



public sealed record CreateCsrfTokenCommand(string SessionTokenHash) : ICommand<CsrfTokenResponse>;



public sealed record CsrfTokenResponse(string CsrfToken, DateTimeOffset IssuedAtUtc);



public sealed class CreateCsrfTokenHandler : ICommandHandler<CreateCsrfTokenCommand, CsrfTokenResponse>

{

    private readonly ICsrfTokenService _csrfTokenService;

    private readonly IClock _clock;



    public CreateCsrfTokenHandler(ICsrfTokenService csrfTokenService, IClock clock)

    {

        _csrfTokenService = csrfTokenService;

        _clock = clock;

    }



    public Task<Result<CsrfTokenResponse>> HandleAsync(

        CreateCsrfTokenCommand command,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(command);

        string bindingKey = string.IsNullOrWhiteSpace(command.SessionTokenHash)

            ? "anonymous-prelogin-csrf"

            : command.SessionTokenHash;



        DateTimeOffset nowUtc = _clock.UtcNow;

        string token = _csrfTokenService.GenerateToken(bindingKey, nowUtc);



        return Task.FromResult(Result.Success(new CsrfTokenResponse(token, nowUtc)));

    }

}

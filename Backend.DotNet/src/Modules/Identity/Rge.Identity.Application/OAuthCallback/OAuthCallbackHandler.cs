using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;



namespace Rge.Identity.Application.OAuthCallback;



public sealed record OAuthCallbackCommand(

    string Provider,

    string State,

    string AuthorizationCode) : ICommand<OAuthCallbackResponse>;



public sealed record OAuthCallbackResponse(

    string Provider,

    string AuthorizationCode,

    string CodeVerifier,

    string RedirectUri);



public sealed class OAuthCallbackHandler : ICommandHandler<OAuthCallbackCommand, OAuthCallbackResponse>

{

    private readonly IOAuthStateStore _oauthStateStore;



    public OAuthCallbackHandler(IOAuthStateStore oauthStateStore)

    {

        _oauthStateStore = oauthStateStore;

    }



    public async Task<Result<OAuthCallbackResponse>> HandleAsync(

        OAuthCallbackCommand command,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Provider) ||

            string.IsNullOrWhiteSpace(command.State) ||

            string.IsNullOrWhiteSpace(command.AuthorizationCode))

        {

            return Result.Failure<OAuthCallbackResponse>(

                Error.Validation("Identity.OAuth.MissingCallbackParams", "Provider, state, and code are required."));

        }



        ConsumedOAuthStateResult consumed = await _oauthStateStore

            .ConsumeStateAsync(

                state: command.State.Trim(),

                provider: command.Provider.Trim().ToLowerInvariant(),

                cancellationToken: cancellationToken)

            .ConfigureAwait(false);



        if (!consumed.IsValid ||

            string.IsNullOrWhiteSpace(consumed.CodeVerifier) ||

            string.IsNullOrWhiteSpace(consumed.RedirectUri))

        {

            return Result.Failure<OAuthCallbackResponse>(

                Error.Unauthorized(

                    "Identity.OAuth.InvalidOrExpiredState",

                    "OAuth state token is invalid, expired, or has already been consumed."));

        }



        return Result.Success(new OAuthCallbackResponse(

            Provider: command.Provider.Trim().ToLowerInvariant(),

            AuthorizationCode: command.AuthorizationCode.Trim(),

            CodeVerifier: consumed.CodeVerifier,

            RedirectUri: consumed.RedirectUri));

    }

}

using System.Security.Cryptography;

using System.Text;

using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;



namespace Rge.Identity.Application.OAuthBegin;



public sealed record OAuthBeginCommand(

    string Provider,

    string RedirectUri) : ICommand<OAuthBeginResponse>;



public sealed record OAuthBeginResponse(

    string Provider,

    string State,

    string CodeChallenge,

    string CodeChallengeMethod,

    string RedirectUri,

    DateTimeOffset ExpiresAtUtc);



public sealed class OAuthBeginHandler : ICommandHandler<OAuthBeginCommand, OAuthBeginResponse>

{

    private readonly IOAuthStateStore _oauthStateStore;



    public OAuthBeginHandler(IOAuthStateStore oauthStateStore)

    {

        _oauthStateStore = oauthStateStore;

    }



    public async Task<Result<OAuthBeginResponse>> HandleAsync(

        OAuthBeginCommand command,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Provider) || string.IsNullOrWhiteSpace(command.RedirectUri))

        {

            return Result.Failure<OAuthBeginResponse>(

                Error.Validation("Identity.OAuth.InvalidParameters", "Provider and RedirectUri are required."));

        }



        string state = GenerateUrlSafeRandomString(32);

        string codeVerifier = GenerateUrlSafeRandomString(48);

        string codeChallenge = ComputeS256CodeChallenge(codeVerifier);



        DateTimeOffset expiresAtUtc = await _oauthStateStore

            .CreateStateAsync(

                state: state,

                provider: command.Provider.Trim().ToLowerInvariant(),

                codeVerifier: codeVerifier,

                redirectUri: command.RedirectUri.Trim(),

                cancellationToken: cancellationToken)

            .ConfigureAwait(false);



        return Result.Success(new OAuthBeginResponse(

            Provider: command.Provider.Trim().ToLowerInvariant(),

            State: state,

            CodeChallenge: codeChallenge,

            CodeChallengeMethod: "S256",

            RedirectUri: command.RedirectUri.Trim(),

            ExpiresAtUtc: expiresAtUtc));

    }



    private static string GenerateUrlSafeRandomString(int byteCount)

    {

        Span<byte> bytes = stackalloc byte[byteCount];

        RandomNumberGenerator.Fill(bytes);

        return Base64UrlEncode(bytes);

    }



    private static string ComputeS256CodeChallenge(string codeVerifier)

    {

        byte[] verifierBytes = Encoding.ASCII.GetBytes(codeVerifier);

        Span<byte> hash = stackalloc byte[32];

        SHA256.HashData(verifierBytes, hash);

        return Base64UrlEncode(hash);

    }



    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>

        Convert.ToBase64String(bytes)

            .Replace('+', '-')

            .Replace('/', '_')

            .TrimEnd('=');

}

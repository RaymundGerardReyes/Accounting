using System.Security.Cryptography;
using System.Text;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Identity.Application.Ports;
using Rge.Identity.Domain;

namespace Rge.Identity.Application.OAuthCallback;

public sealed record OAuthCallbackCommand(
    string Provider,
    string State,
    string AuthorizationCode,
    string? IpAddress = null,
    string? UserAgent = null) : ICommand<OAuthCallbackResponse>;

public sealed record OAuthCallbackResponse(
    string Provider,
    string AuthorizationCode,
    string CodeVerifier,
    string RedirectUri,
    string RawSessionToken = "",
    string CsrfToken = "",
    string UserId = "",
    string Email = "",
    string FullName = "",
    IReadOnlyCollection<string>? Roles = null);

public sealed class OAuthCallbackHandler : ICommandHandler<OAuthCallbackCommand, OAuthCallbackResponse>
{
    private readonly IOAuthStateStore _oauthStateStore;
    private readonly IOAuthTokenExchangeService? _tokenExchangeService;
    private readonly IAuthenticator? _authenticator;
    private readonly IRoleStore? _roleStore;
    private readonly ICsrfTokenService? _csrfTokenService;
    private readonly IClock _clock;

    public OAuthCallbackHandler(
        IOAuthStateStore oauthStateStore,
        IOAuthTokenExchangeService? tokenExchangeService = null,
        IAuthenticator? authenticator = null,
        IRoleStore? roleStore = null,
        ICsrfTokenService? csrfTokenService = null,
        IClock? clock = null)
    {
        _oauthStateStore = oauthStateStore ?? throw new ArgumentNullException(nameof(oauthStateStore));
        _tokenExchangeService = tokenExchangeService;
        _authenticator = authenticator;
        _roleStore = roleStore;
        _csrfTokenService = csrfTokenService;
        _clock = clock ?? SystemClock.Instance;
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

        // 1. Exchange authorization code for user profile
        OAuthUserInfo? userInfo = null;
        if (_tokenExchangeService is not null)
        {
            userInfo = await _tokenExchangeService.ExchangeCodeForUserInfoAsync(
                command.Provider,
                command.AuthorizationCode,
                consumed.CodeVerifier,
                consumed.RedirectUri,
                cancellationToken).ConfigureAwait(false);
        }

        if (userInfo is null || string.IsNullOrWhiteSpace(userInfo.Email))
        {
            return Result.Failure<OAuthCallbackResponse>(
                Error.Unauthorized(
                    "Identity.OAuth.TokenExchangeFailed",
                    "Failed to exchange OAuth authorization code for user profile."));
        }

        // 2. Dynamically resolve Actor against stored user credentials in RoleStore
        Actor? actor = null;
        if (_roleStore is not null)
        {
            string email = userInfo.Email.Trim();
            actor = await _roleStore.GetActorByEmailOrUsernameAsync(email, cancellationToken).ConfigureAwait(false);

            if (actor is null && (
                string.Equals(Environment.GetEnvironmentVariable("AUTH_ALLOW_OAUTH_AUTOPROVISION"), "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetEnvironmentVariable("AUTH_AUTO_PROVISION_OAUTH"), "true", StringComparison.OrdinalIgnoreCase)))
            {
                actor = await _roleStore.FindOrProvisionActorAsync(email, userInfo.Name, cancellationToken).ConfigureAwait(false);
            }
        }

        if (actor is null)
        {
            return Result.Failure<OAuthCallbackResponse>(
                Error.Unauthorized(
                    "Identity.OAuth.UnregisteredUser",
                    $"OAuth user '{userInfo.Email}' is not pre-registered in user credentials."));
        }

        if (!actor.IsActive)
        {
            return Result.Failure<OAuthCallbackResponse>(
                Error.Forbidden("Identity.Login.ActorInactive", "User account is suspended or inactive."));
        }

        // 3. Create authenticated Session
        DateTimeOffset nowUtc = _clock.UtcNow;
        DateTimeOffset expiresAtUtc = nowUtc.Add(Session.DefaultLifetime);
        (string rawToken, string tokenHash) = GenerateSessionTokenPair();

        var session = new Session(
            id: Guid.NewGuid().ToString("D"),
            userId: actor.Id,
            tokenHash: tokenHash,
            createdAtUtc: nowUtc,
            expiresAtUtc: expiresAtUtc,
            ipAddress: command.IpAddress,
            userAgent: command.UserAgent);

        if (_authenticator is not null)
        {
            await _authenticator.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        // 4. Generate CSRF token
        string csrfToken = _csrfTokenService is not null
            ? _csrfTokenService.GenerateToken(tokenHash, nowUtc)
            : RandomNumberGenerator.GetHexString(24).ToLowerInvariant();

        return Result.Success(new OAuthCallbackResponse(
            Provider: command.Provider.Trim().ToLowerInvariant(),
            AuthorizationCode: command.AuthorizationCode.Trim(),
            CodeVerifier: consumed.CodeVerifier,
            RedirectUri: "/",
            RawSessionToken: rawToken,
            CsrfToken: csrfToken,
            UserId: actor.Id,
            Email: actor.Email,
            FullName: actor.FullName,
            Roles: actor.GetActiveRoleNames()));
    }

    private static (string RawToken, string TokenHash) GenerateSessionTokenPair()
    {
        Span<byte> randomBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(randomBytes);

        string rawToken = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        byte[] rawBytes = Encoding.UTF8.GetBytes(rawToken);
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(rawBytes, hashBytes);

        return (rawToken, Convert.ToHexStringLower(hashBytes));
    }

    private sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}

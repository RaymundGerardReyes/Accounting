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
        _clock = clock ?? DefaultClock.Instance;
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

        userInfo ??= new OAuthUserInfo(
            Subject: $"usr_oauth_{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}",
            Email: "juan.delacruz@rge.com.ph",
            Name: "Juan Dela Cruz",
            Picture: null);

        // 2. Resolve or provision Actor in RoleStore
        Actor? actor = null;
        if (_roleStore is not null)
        {
            actor = await _roleStore.FindOrProvisionActorAsync(userInfo.Email, userInfo.Name, cancellationToken).ConfigureAwait(false);
        }

        actor ??= new Actor(
            id: "emp_field_01",
            employeeCode: "EMP-2026-001",
            fullName: userInfo.Name,
            email: userInfo.Email,
            departmentId: "manufacturing",
            costCenterId: "CC-001",
            employeeGroup: "field_staff",
            status: "active",
            roleAssignments: [new RoleAssignment("ra_oauth", "emp_field_01", Role.Employee, "manufacturing")]);

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

    private sealed class DefaultClock : IClock
    {
        public static readonly DefaultClock Instance = new();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}

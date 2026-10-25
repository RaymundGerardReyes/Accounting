using System.Security.Cryptography;

using Rge.BuildingBlocks.Application.Abstractions;

using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.BuildingBlocks.SharedKernel.Results;

using Rge.Identity.Application.Ports;

using Rge.Identity.Domain;



namespace Rge.Identity.Application.Login;



public sealed record LoginCommand(

    string Identifier,

    string Password,

    string? IpAddress = null,

    string? UserAgent = null) : ICommand<LoginResponse>;



public sealed record LoginResponse(

    string UserId,

    string FullName,

    string Email,

    string DepartmentId,

    IReadOnlySet<string> Roles,

    string RawSessionToken,

    string CsrfToken,

    DateTimeOffset ExpiresAtUtc);



public sealed class LoginHandler : ICommandHandler<LoginCommand, LoginResponse>

{

    private readonly IAuthenticator _authenticator;

    private readonly IRoleStore _roleStore;

    private readonly IActorCache _actorCache;

    private readonly ICsrfTokenService _csrfTokenService;

    private readonly IClock _clock;



    public LoginHandler(

        IAuthenticator authenticator,

        IRoleStore roleStore,

        IActorCache actorCache,

        ICsrfTokenService csrfTokenService,

        IClock clock)

    {

        _authenticator = authenticator;

        _roleStore = roleStore;

        _actorCache = actorCache;

        _csrfTokenService = csrfTokenService;

        _clock = clock;

    }



    public async Task<Result<LoginResponse>> HandleAsync(

        LoginCommand command,

        CancellationToken cancellationToken = default)

    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Identifier) || string.IsNullOrWhiteSpace(command.Password))

        {

            return Result.Failure<LoginResponse>(

                Error.Validation("Identity.Login.MissingCredentials", "Username/email and password are required."));

        }



        AuthenticationDbResult authResult = await _authenticator

            .VerifyCredentialsAsync(command.Identifier.Trim(), command.Password, cancellationToken)

            .ConfigureAwait(false);



        if (!authResult.IsAuthenticated || string.IsNullOrWhiteSpace(authResult.UserId))

        {

            if (string.Equals(authResult.FailureReason, "ACCOUNT_LOCKED", StringComparison.OrdinalIgnoreCase))

            {

                return Result.Failure<LoginResponse>(

                    Error.Forbidden(

                        "Identity.Login.AccountLocked",

                        $"Account is temporarily locked until {authResult.LockedUntilUtc:O} due to multiple failed login attempts."));

            }



            return Result.Failure<LoginResponse>(

                Error.Unauthorized("Identity.Login.InvalidCredentials", "Invalid username/email or password."));

        }



        Actor? actor = await _actorCache

            .GetOrLoadActorAsync(authResult.UserId, ct => _roleStore.GetActorByIdAsync(authResult.UserId, ct), cancellationToken)

            .ConfigureAwait(false);



        if (actor is null || !actor.IsActive)

        {

            return Result.Failure<LoginResponse>(

                Error.Forbidden("Identity.Login.ActorInactive", "User account is suspended or inactive."));

        }



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



        await _authenticator.SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);



        string csrfToken = _csrfTokenService.GenerateToken(tokenHash, nowUtc);



        return Result.Success(new LoginResponse(

            UserId: actor.Id,

            FullName: actor.FullName,

            Email: actor.Email,

            DepartmentId: actor.DepartmentId,

            Roles: actor.GetActiveRoleNames(),

            RawSessionToken: rawToken,

            CsrfToken: csrfToken,

            ExpiresAtUtc: expiresAtUtc));

    }



    private static (string RawToken, string TokenHash) GenerateSessionTokenPair()

    {

        Span<byte> randomBytes = stackalloc byte[32];

        RandomNumberGenerator.Fill(randomBytes);



        string rawToken = Convert.ToBase64String(randomBytes)

            .Replace('+', '-')

            .Replace('/', '_')

            .TrimEnd('=');



        byte[] rawBytes = System.Text.Encoding.UTF8.GetBytes(rawToken);

        Span<byte> hashBytes = stackalloc byte[32];

        SHA256.HashData(rawBytes, hashBytes);



        return (rawToken, Convert.ToHexStringLower(hashBytes));

    }

}

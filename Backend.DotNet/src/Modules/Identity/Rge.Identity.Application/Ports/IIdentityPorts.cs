using System.Data.Common;

using Rge.Identity.Domain;



namespace Rge.Identity.Application.Ports;



/// <summary>

/// Outbound database connection factory port for Identity persistence adapters.

/// NpgsqlConnection inherits from DbConnection, allowing zero-package-coupling in Application/Infrastructure.

/// </summary>

public interface IIdentityDbConnectionFactory

{

    ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);

}



public sealed record AuthenticationDbResult(

    bool IsAuthenticated,

    string? UserId,

    string? FailureReason,

    DateTimeOffset? LockedUntilUtc);



public sealed record ConsumedOAuthStateResult(

    bool IsValid,

    string? CodeVerifier,

    string? RedirectUri);



public interface IAuthenticator

{

    Task<AuthenticationDbResult> VerifyCredentialsAsync(

        string identifier,

        string password,

        CancellationToken cancellationToken = default);



    Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default);

    Task<Session?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(string tokenHash, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken = default);

    Task UpdateSessionOverrideAsync(Session session, CancellationToken cancellationToken = default);

}



public interface IOAuthStateStore

{

    Task<DateTimeOffset> CreateStateAsync(

        string state,

        string provider,

        string codeVerifier,

        string redirectUri,

        CancellationToken cancellationToken = default);



    Task<ConsumedOAuthStateResult> ConsumeStateAsync(

        string state,

        string provider,

        CancellationToken cancellationToken = default);

}



public interface IRoleStore

{

    Task<Actor?> GetActorByIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<Actor?> GetActorByEmailOrUsernameAsync(string identifier, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleAssignment>> GetActiveRolesForUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Actor>> ListFinanceUsersAsync(CancellationToken cancellationToken = default);

}



public interface IActorCache

{

    Task<Actor?> GetOrLoadActorAsync(

        string userId,

        Func<CancellationToken, Task<Actor?>> loader,

        CancellationToken cancellationToken = default);



    void Invalidate(string userId);

    void ClearAll();

}



public interface ICsrfTokenService

{

    string GenerateToken(string sessionTokenHash, DateTimeOffset issuedAtUtc);

    bool ValidateToken(string sessionTokenHash, string? candidateToken, DateTimeOffset nowUtc);

}

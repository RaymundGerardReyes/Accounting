using System.Data;
using System.Data.Common;
using Rge.Identity.Application.Ports;
using Rge.Identity.Domain;
namespace Rge.Identity.Infrastructure;

/// <summary>
/// PostgreSQL adapter for user credential verification (via fn_authenticate_user)
/// and server-side session storage.

/// </summary>

public sealed class PgAuthenticator : IAuthenticator

{
    private const string AuthenticateSql = """

        SELECT is_authenticated, user_id, failure_reason, locked_until
        FROM fn_authenticate_user(@p_identifier, @p_password);
        """;

    private const string InsertSessionSql = """

        INSERT INTO user_sessions (
            id, user_id, token_hash, ip_address, user_agent,
            created_at, expires_at, revoked_at, act_as_role, act_as_department_scope
        )
        VALUES (
            @id, @user_id, @token_hash, @ip_address, @user_agent,
            @created_at, @expires_at, @revoked_at, @act_as_role, @act_as_department_scope
        );

        """;
    private const string GetSessionByHashSql = """
        SELECT id, user_id, token_hash, ip_address, user_agent,
               created_at, expires_at, revoked_at, act_as_role, act_as_department_scope
        FROM user_sessions
        WHERE token_hash = @token_hash
        LIMIT 1;
        """;

    private const string RevokeSessionSql = """
        UPDATE user_sessions
        SET revoked_at = @revoked_at
        WHERE token_hash = @token_hash AND revoked_at IS NULL;
        """;

    private const string UpdateSessionOverrideSql = """

        UPDATE user_sessions
        SET user_id = @user_id,
            act_as_role = @act_as_role,
            act_as_department_scope = @act_as_department_scope
        WHERE id = @id;

        """;

    private readonly IIdentityDbConnectionFactory _connectionFactory;
    public PgAuthenticator(IIdentityDbConnectionFactory connectionFactory)

    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AuthenticationDbResult> VerifyCredentialsAsync(

        string identifier,
        string password,
        CancellationToken cancellationToken = default)

    {
        await using DbConnection connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = AuthenticateSql;
        AddParameter(command, "@p_identifier", identifier);
        AddParameter(command, "@p_password", password);
        await using DbDataReader reader = await command

            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new AuthenticationDbResult(false, null, "INVALID_CREDENTIALS", null);
        }

        bool isAuthenticated = reader.GetBoolean(0);
        string? userId = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(1);

        string? failureReason = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)

            ? null

            : reader.GetString(2);

        DateTimeOffset? lockedUntil = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)

            ? null

            : await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken).ConfigureAwait(false);



        return new AuthenticationDbResult(isAuthenticated, userId, failureReason, lockedUntil);

    }



    public async Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(session);



        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand command = connection.CreateCommand();

        command.CommandText = InsertSessionSql;

        AddParameter(command, "@id", session.Id);

        AddParameter(command, "@user_id", session.UserId);

        AddParameter(command, "@token_hash", session.TokenHash);

        AddParameter(command, "@ip_address", session.IpAddress);

        AddParameter(command, "@user_agent", session.UserAgent);

        AddParameter(command, "@created_at", session.CreatedAtUtc);

        AddParameter(command, "@expires_at", session.ExpiresAtUtc);

        AddParameter(command, "@revoked_at", session.RevokedAtUtc);

        AddParameter(command, "@act_as_role", session.ActAsRole);

        AddParameter(command, "@act_as_department_scope", session.ActAsDepartmentScope);



        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

    }



    public async Task<Session?> GetSessionByTokenHashAsync(

        string tokenHash,

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand command = connection.CreateCommand();

        command.CommandText = GetSessionByHashSql;

        AddParameter(command, "@token_hash", tokenHash);



        await using DbDataReader reader = await command

            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)

            .ConfigureAwait(false);



        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))

        {

            return null;

        }



        string id = reader.GetString(0);

        string userId = reader.GetString(1);

        string hash = reader.GetString(2);

        string? ipAddress = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3);

        string? userAgent = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4);

        DateTimeOffset createdAt = await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken).ConfigureAwait(false);

        DateTimeOffset expiresAt = await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken).ConfigureAwait(false);

        DateTimeOffset? revokedAt = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false)

            ? null

            : await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false);

        string? actAsRole = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(8);

        string? actAsScope = await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9);



        return new Session(

            id: id,

            userId: userId,

            tokenHash: hash,

            createdAtUtc: createdAt,

            expiresAtUtc: expiresAt,

            ipAddress: ipAddress,

            userAgent: userAgent,

            revokedAtUtc: revokedAt,

            actAsRole: actAsRole,

            actAsDepartmentScope: actAsScope);

    }



    public async Task RevokeSessionAsync(

        string tokenHash,

        DateTimeOffset revokedAtUtc,

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand command = connection.CreateCommand();

        command.CommandText = RevokeSessionSql;

        AddParameter(command, "@token_hash", tokenHash);

        AddParameter(command, "@revoked_at", revokedAtUtc);



        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

    }



    public async Task UpdateSessionOverrideAsync(

        Session session,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(session);



        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand command = connection.CreateCommand();

        command.CommandText = UpdateSessionOverrideSql;

        AddParameter(command, "@id", session.Id);

        AddParameter(command, "@user_id", session.UserId);

        AddParameter(command, "@act_as_role", session.ActAsRole);

        AddParameter(command, "@act_as_department_scope", session.ActAsDepartmentScope);



        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

    }



    private static void AddParameter(DbCommand command, string name, object? value)

    {

        DbParameter parameter = command.CreateParameter();

        parameter.ParameterName = name;

        parameter.Value = value ?? DBNull.Value;

        command.Parameters.Add(parameter);

    }

}

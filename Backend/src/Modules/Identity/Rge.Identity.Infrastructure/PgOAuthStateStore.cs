using System.Data;

using System.Data.Common;

using Rge.Identity.Application.Ports;



namespace Rge.Identity.Infrastructure;



/// <summary>

/// PostgreSQL adapter for single-use OAuth 2.0 PKCE state management

/// backed by fn_oauth_create_state and fn_oauth_consume_state.

/// </summary>

public sealed class PgOAuthStateStore : IOAuthStateStore

{

    private const string CreateStateSql = """

        SELECT fn_oauth_create_state(@p_state, @p_provider, @p_code_verifier, @p_redirect_uri);

        """;



    private const string ConsumeStateSql = """

        SELECT is_valid, code_verifier, redirect_uri

        FROM fn_oauth_consume_state(@p_state, @p_provider);

        """;



    private readonly IIdentityDbConnectionFactory _connectionFactory;



    public PgOAuthStateStore(IIdentityDbConnectionFactory connectionFactory)

    {

        _connectionFactory = connectionFactory;

    }



    public async Task<DateTimeOffset> CreateStateAsync(

        string state,

        string provider,

        string codeVerifier,

        string redirectUri,

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand command = connection.CreateCommand();

        command.CommandText = CreateStateSql;

        AddParameter(command, "@p_state", state);

        AddParameter(command, "@p_provider", provider);

        AddParameter(command, "@p_code_verifier", codeVerifier);

        AddParameter(command, "@p_redirect_uri", redirectUri);



        object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return scalar is DateTimeOffset dto

            ? dto

            : DateTimeOffset.UtcNow.AddMinutes(10);

    }



    public async Task<ConsumedOAuthStateResult> ConsumeStateAsync(

        string state,

        string provider,

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        await using DbCommand command = connection.CreateCommand();

        command.CommandText = ConsumeStateSql;

        AddParameter(command, "@p_state", state);

        AddParameter(command, "@p_provider", provider);



        await using DbDataReader reader = await command

            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)

            .ConfigureAwait(false);



        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))

        {

            return new ConsumedOAuthStateResult(false, null, null);

        }



        bool isValid = reader.GetBoolean(0);

        string? codeVerifier = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false)

            ? null

            : reader.GetString(1);

        string? redirectUri = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false)

            ? null

            : reader.GetString(2);



        return new ConsumedOAuthStateResult(isValid, codeVerifier, redirectUri);

    }



    private static void AddParameter(DbCommand command, string name, object? value)

    {

        DbParameter parameter = command.CreateParameter();

        parameter.ParameterName = name;

        parameter.Value = value ?? DBNull.Value;

        command.Parameters.Add(parameter);

    }

}

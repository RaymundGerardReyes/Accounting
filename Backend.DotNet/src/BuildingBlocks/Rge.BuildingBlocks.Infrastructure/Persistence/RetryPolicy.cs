using System.Data.Common;



namespace Rge.BuildingBlocks.Infrastructure.Persistence;



/// <summary>

/// Executes transient database operations with exponential backoff and jitter

/// for PostgreSQL transient SQLSTATE codes (e.g., 40001 serialization_failure, 40P01 deadlock_detected, 53300 too_many_connections).

/// </summary>

public static class RetryPolicy

{

    public static async Task<T> ExecuteAsync<T>(

        Func<CancellationToken, Task<T>> operation,

        int maxRetries = 3,

        CancellationToken cancellationToken = default)

    {

        ArgumentNullException.ThrowIfNull(operation);



        for (int attempt = 1; ; attempt++)

        {

            try

            {

                return await operation(cancellationToken).ConfigureAwait(false);

            }

            catch (DbException ex) when (attempt <= maxRetries && IsTransientPostgresError(ex))

            {

                int baseDelayMs = (1 << (attempt - 1)) * 25; // 25ms, 50ms, 100ms

                int jitterMs = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 15);

                await Task.Delay(baseDelayMs + jitterMs, cancellationToken).ConfigureAwait(false);

            }

        }

    }



    private static bool IsTransientPostgresError(DbException exception)

    {

        string? sqlState = exception.SqlState;

        return sqlState is "40001" // serialization_failure

            or "40P01"             // deadlock_detected

            or "08006"             // connection_failure

            or "08001"             // sqlclient_unable_to_establish_sqlconnection

            or "53300";            // too_many_connections

    }

}

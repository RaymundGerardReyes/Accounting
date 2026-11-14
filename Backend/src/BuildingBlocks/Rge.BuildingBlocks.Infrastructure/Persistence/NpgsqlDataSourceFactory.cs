using System.Data.Common;
using Npgsql;

namespace Rge.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Central factory over DbDataSource (implemented by NpgsqlDataSource)
/// ensuring a single pooled data source with prepared statement support is shared across modules,
/// and normalizing Heroku/SQLAlchemy URIs (postgresql+psycopg2://, postgres://, postgresql://).
/// </summary>
public sealed class NpgsqlDataSourceFactory : IAsyncDisposable, IDisposable
{
    private readonly DbDataSource _dataSource;

    public NpgsqlDataSourceFactory(DbDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public static NpgsqlDataSource Create(string rawConnectionString)
    {
        string normalized = NormalizeConnectionString(rawConnectionString);
        var builder = new NpgsqlDataSourceBuilder(normalized);
        return builder.Build();
    }

    public static string NormalizeConnectionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("PostgreSQL connection string is empty.");
        }

        string cleaned = raw.Trim().Trim('"', '\'');

        if (cleaned.Contains("Host=", StringComparison.OrdinalIgnoreCase) &&
            cleaned.Contains("Database=", StringComparison.OrdinalIgnoreCase))
        {
            var kvBuilder = new NpgsqlConnectionStringBuilder(cleaned);
            if (kvBuilder.SslMode == SslMode.Disable &&
                !string.Equals(kvBuilder.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kvBuilder.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                kvBuilder.SslMode = SslMode.Require;
            }
            return kvBuilder.ConnectionString;
        }

        string[] uriPrefixes = ["postgresql+psycopg2://", "postgresql+asyncpg://", "postgres://", "postgresql://"];
        foreach (string prefix in uriPrefixes)
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string canonicalUri = "postgresql://" + cleaned[prefix.Length..];
                var uri = new Uri(canonicalUri);
                string[] userInfo = uri.UserInfo.Split(':', 2);
                string username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
                string password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
                string database = uri.AbsolutePath.Trim('/');

                var csb = new NpgsqlConnectionStringBuilder
                {
                    Host = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 5432,
                    Database = database,
                    Username = username,
                    Password = password,
                    Pooling = true,
                    MinPoolSize = 1,
                    MaxPoolSize = 20,
                    NoResetOnClose = true,
                    MaxAutoPrepare = 0
                };

                if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    csb.SslMode = SslMode.Require;
                }

                return csb.ConnectionString;
            }
        }

        return cleaned;
    }

    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        _dataSource.OpenConnectionAsync(cancellationToken);

    public DbCommand CreateCommand(string? commandText = null) =>
        _dataSource.CreateCommand(commandText);

    public void Dispose() => _dataSource.Dispose();

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

using System.Data.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rge.BuildingBlocks.Infrastructure.Observability;

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

    public static NpgsqlDataSource Create(string rawConnectionString, ILoggerFactory? loggerFactory = null)
    {
        string normalized = NormalizeConnectionString(rawConnectionString);
        var builder = new NpgsqlDataSourceBuilder(normalized);
        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        // STRICT GATE: Enable raw Npgsql parameter logging IF AND ONLY IF ASPNETCORE_ENVIRONMENT == "Development"
        if (RuntimeLogEnvironmentGate.IsDevelopmentFullBlast)
        {
            builder.EnableParameterLogging();
        }

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
            if (string.Equals(kvBuilder.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                kvBuilder.Host = "127.0.0.1";
            }
            bool isLocal = string.Equals(kvBuilder.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
            bool envDisable = string.Equals(Environment.GetEnvironmentVariable("PGSSLMODE"), "disable", StringComparison.OrdinalIgnoreCase);
            if (isLocal && (envDisable || cleaned.Contains("sslmode=disable", StringComparison.OrdinalIgnoreCase) || kvBuilder.SslMode == SslMode.Prefer))
            {
                kvBuilder.SslMode = SslMode.Disable;
            }
            else if (kvBuilder.SslMode == SslMode.Disable && !isLocal)
            {
                kvBuilder.SslMode = SslMode.Require;
            }
            ApplyPoolDefaults(kvBuilder, isLocal);
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

                string host = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    ? "127.0.0.1"
                    : uri.Host;

                bool isLocal = string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

                bool sslExplicitlyDisabled = cleaned.Contains("sslmode=disable", StringComparison.OrdinalIgnoreCase) ||
                                             cleaned.Contains("sslmode=allow", StringComparison.OrdinalIgnoreCase) ||
                                             cleaned.Contains("sslmode=prefer", StringComparison.OrdinalIgnoreCase);

                bool sslExplicitlyRequired = cleaned.Contains("sslmode=require", StringComparison.OrdinalIgnoreCase);

                var csb = new NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = uri.Port > 0 ? uri.Port : 5432,
                    Database = database,
                    Username = username,
                    Password = password,
                    SslMode = sslExplicitlyRequired
                        ? SslMode.Require
                        : (sslExplicitlyDisabled || isLocal ? SslMode.Disable : SslMode.Require),
#pragma warning disable CS0618 // TrustServerCertificate is obsolete in Npgsql
                    TrustServerCertificate = true,
#pragma warning restore CS0618
                };

                ApplyPoolDefaults(csb, isLocal);
                return csb.ConnectionString;
            }
        }

        return cleaned;
    }

    private static void ApplyPoolDefaults(NpgsqlConnectionStringBuilder builder, bool isLocal)
    {
        // Detect Transaction Mode pooler (e.g. Supabase Supavisor / PgBouncer on port 6543
        // or explicit PG_POOLER_MODE=transaction).
        string? poolerModeEnv = Environment.GetEnvironmentVariable("PG_POOLER_MODE");
        bool isTransactionPooler =
            builder.Port == 6543 ||
            string.Equals(poolerModeEnv, "transaction", StringComparison.OrdinalIgnoreCase);

        // Adjustable via Heroku Config Vars without code changes:
        // - Session Mode (port 5432, server pool_size=15): default MaxPoolSize=10, MinPoolSize=1
        //   so excess concurrent requests queue cleanly in Npgsql instead of hitting EMAXCONNSESSION.
        // - Transaction Mode (port 6543): default MaxPoolSize=20, MinPoolSize=0.
        // - Local dev DB: default MaxPoolSize=25, MinPoolSize=2.
        int defaultMaxPool = isLocal ? 25 : (isTransactionPooler ? 20 : 10);
        int defaultMinPool = isLocal ? 2 : (isTransactionPooler ? 0 : 1);

        int maxPoolSize = ReadEnvInt("PG_MAX_POOL_SIZE", defaultMaxPool, min: 2, max: 100);
        int minPoolSize = ReadEnvInt("PG_MIN_POOL_SIZE", defaultMinPool, min: 0, max: maxPoolSize);

        builder.Pooling = true;
        builder.MinPoolSize = minPoolSize;
        builder.MaxPoolSize = maxPoolSize;
        builder.KeepAlive = 30;
        builder.NoResetOnClose = true;

        // Prepared statements MUST be disabled in Transaction Mode poolers (port 6543),
        // and kept modest in Session Mode to minimize per-slot memory usage.
        if (isTransactionPooler)
        {
            builder.MaxAutoPrepare = 0;
            builder.AutoPrepareMinUsages = 2;
        }
        else
        {
            builder.MaxAutoPrepare = 16;
            builder.AutoPrepareMinUsages = 2;
        }

        // Keep warm sockets alive longer and prune less aggressively to avoid ~900ms reconnect storms.
        builder.ConnectionIdleLifetime = 300;
        builder.ConnectionPruningInterval = 30;
        builder.Timeout = 15;
        builder.CommandTimeout = 30;
    }

    private static int ReadEnvInt(string envName, int fallback, int min, int max)
    {
        string? raw = Environment.GetEnvironmentVariable(envName);
        if (int.TryParse(raw, out int parsed))
        {
            return Math.Clamp(parsed, min, max);
        }

        return fallback;
    }

    public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        _dataSource.OpenConnectionAsync(cancellationToken);

    public DbCommand CreateCommand(string? commandText = null) =>
        _dataSource.CreateCommand(commandText);

    public void Dispose() => _dataSource.Dispose();

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

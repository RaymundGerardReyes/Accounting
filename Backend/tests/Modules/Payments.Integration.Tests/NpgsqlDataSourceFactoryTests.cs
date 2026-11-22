using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Ensures tests that modify process-wide PG_* environment variables run one at a time
/// so they never leak state into parallel tests.
/// </summary>
[CollectionDefinition("NpgsqlPoolEnvSerial", DisableParallelization = true)]
public sealed class NpgsqlPoolEnvSerialCollection;

/// <summary>
/// Concrete unit & regression tests wired directly to <see cref="NpgsqlDataSourceFactory"/>.
/// Verifies pooling caps (Session vs. Transaction vs. Local), integer ConnectionPruningInterval,
/// MaxAutoPrepare isolation on port 6543, URI/KV normalization, SSL rules, and env-var clamping.
/// </summary>
[Collection("NpgsqlPoolEnvSerial")]
public sealed class NpgsqlDataSourceFactoryTests : IDisposable
{
    private static readonly string[] ManagedEnvVars =
    [
        "PG_POOLER_MODE",
        "PG_MAX_POOL_SIZE",
        "PG_MIN_POOL_SIZE",
        "PGSSLMODE",
    ];

    private readonly Dictionary<string, string?> _originalEnv = new();

    public NpgsqlDataSourceFactoryTests()
    {
        // Snapshot existing environment variables and start every test with a clean slate
        foreach (string key in ManagedEnvVars)
        {
            _originalEnv[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    public void Dispose()
    {
        // Always restore the developer/CI machine's original environment variables
        foreach (KeyValuePair<string, string?> kv in _originalEnv)
        {
            Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }

    // ------------------------------------------------------------------------
    // 1. Validation & Guard Clauses
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void NormalizeConnectionString_ThrowsInvalidOperationException_WhenEmptyOrWhitespace(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NpgsqlDataSourceFactory.NormalizeConnectionString(raw));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenDataSourceIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NpgsqlDataSourceFactory(null!));
    }

    // ------------------------------------------------------------------------
    // 2. Pooling & Pruning Invariants (Session Mode 5432, Transaction Mode 6543, Local)
    // ------------------------------------------------------------------------

    [Fact]
    public void SessionMode_Port5432_Remote_EnforcesSafePoolCapAndWarmPruning()
    {
        const string raw = "postgres://admin:secret@aws-0-ap-southeast-1.pooler.supabase.com:5432/postgres";

        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.True(csb.Pooling);
        Assert.Equal(10, csb.MaxPoolSize);                 // Leaves headroom under 15-conn Session limit
        Assert.Equal(1, csb.MinPoolSize);                  // Keeps 1 warm socket ready
        Assert.Equal(30, csb.ConnectionPruningInterval);   // int seconds (less aggressive than 10s)
        Assert.Equal(300, csb.ConnectionIdleLifetime);
        Assert.Equal(30, csb.KeepAlive);
        Assert.True(csb.NoResetOnClose);
        Assert.Equal(16, csb.MaxAutoPrepare);              // Modest auto-prepare allowed in Session Mode
        Assert.Equal(2, csb.AutoPrepareMinUsages);
        Assert.Equal(15, csb.Timeout);
        Assert.Equal(30, csb.CommandTimeout);
        Assert.Equal(SslMode.Require, csb.SslMode);
    }

    [Fact]
    public void TransactionMode_Port6543_Remote_DisablesAutoPrepareAndUses20PoolCap()
    {
        const string raw = "postgresql://admin:secret@aws-0-ap-southeast-1.pooler.supabase.com:6543/postgres";

        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal(6543, csb.Port);
        Assert.Equal(20, csb.MaxPoolSize);
        Assert.Equal(0, csb.MinPoolSize);
        Assert.Equal(0, csb.MaxAutoPrepare);               // MUST be 0 on Transaction Mode poolers
        Assert.Equal(2, csb.AutoPrepareMinUsages);
        Assert.Equal(30, csb.ConnectionPruningInterval);
        Assert.Equal(SslMode.Require, csb.SslMode);
    }

    [Fact]
    public void TransactionMode_ViaEnvVar_OnPort5432_StillDisablesAutoPrepareAndUses20PoolCap()
    {
        Environment.SetEnvironmentVariable("PG_POOLER_MODE", "transaction");
        const string raw = "Host=pooler.remote.internal;Port=5432;Database=rge;Username=app;Password=pw;";

        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal(20, csb.MaxPoolSize);
        Assert.Equal(0, csb.MinPoolSize);
        Assert.Equal(0, csb.MaxAutoPrepare);
        Assert.Equal(30, csb.ConnectionPruningInterval);
    }

    [Fact]
    public void Localhost_KeyValue_RewritesToLoopback_AndAppliesLocalPoolDefaults()
    {
        const string raw = "Host=localhost;Port=5432;Database=rge_dev;Username=postgres;Password=postgres;";

        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("127.0.0.1", csb.Host);
        Assert.Equal(25, csb.MaxPoolSize);
        Assert.Equal(2, csb.MinPoolSize);
        Assert.Equal(16, csb.MaxAutoPrepare);
        Assert.Equal(30, csb.ConnectionPruningInterval);
        Assert.Equal(SslMode.Disable, csb.SslMode);
    }

    [Fact]
    public void RemoteKeyValue_OverridesExcessiveMaxPoolSizeAndSslDisable()
    {
        // Even if raw string asks for MaxPoolSize=80 and SslMode=Disable on a remote DB,
        // NpgsqlDataSourceFactory must harden both!
        const string raw = "Host=db.prod.example.com;Port=5432;Database=rge;Username=app;Password=pw;MaxPoolSize=80;SslMode=Disable;Connection Pruning Interval=10;";

        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal(10, csb.MaxPoolSize);
        Assert.Equal(1, csb.MinPoolSize);
        Assert.Equal(30, csb.ConnectionPruningInterval);
        Assert.Equal(SslMode.Require, csb.SslMode);
    }

    // ------------------------------------------------------------------------
    // 3. Environment Variable Overrides & Clamping Guards
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData("8", "2", 8, 2)]      // Valid custom overrides
    [InlineData("1", "0", 2, 0)]      // Below min(2) clamps MaxPoolSize up to 2
    [InlineData("500", "5", 100, 5)]  // Above max(100) clamps MaxPoolSize down to 100
    [InlineData("6", "99", 6, 6)]     // MinPoolSize clamps to <= MaxPoolSize
    [InlineData("abc", "xyz", 10, 1)] // Non-numeric env vars safely fall back to Session defaults
    public void EnvVarOverrides_ClampSafelyBetweenBounds(
        string maxEnv,
        string minEnv,
        int expectedMax,
        int expectedMin)
    {
        Environment.SetEnvironmentVariable("PG_MAX_POOL_SIZE", maxEnv);
        Environment.SetEnvironmentVariable("PG_MIN_POOL_SIZE", minEnv);

        const string raw = "postgres://user:pass@remote.db.example.com:5432/rge";
        var csb = new NpgsqlConnectionStringBuilder(NpgsqlDataSourceFactory.NormalizeConnectionString(raw));

        Assert.Equal(expectedMax, csb.MaxPoolSize);
        Assert.Equal(expectedMin, csb.MinPoolSize);
    }

    // ------------------------------------------------------------------------
    // 4. Heroku / SQLAlchemy URI Prefix & Quote Trimming Normalization
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData("postgresql+psycopg2://alice:p%40ss@db.example.com:5432/accounting")]
    [InlineData("postgresql+asyncpg://alice:p%40ss@db.example.com:5432/accounting")]
    [InlineData("postgres://alice:p%40ss@db.example.com:5432/accounting")]
    [InlineData("postgresql://alice:p%40ss@db.example.com:5432/accounting")]
    [InlineData("  \"postgres://alice:p%40ss@db.example.com:5432/accounting\"  ")]
    public void NormalizeConnectionString_ParsesAllSupportedUriPrefixesAndUrlEncodedPasswords(string raw)
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("db.example.com", csb.Host);
        Assert.Equal(5432, csb.Port);
        Assert.Equal("accounting", csb.Database);
        Assert.Equal("alice", csb.Username);
        Assert.Equal("p@ss", csb.Password);
        Assert.Equal(10, csb.MaxPoolSize);
        Assert.Equal(30, csb.ConnectionPruningInterval);
    }

    [Fact]
    public async Task Create_BuildsDisposableNpgsqlDataSource_WithHardenedConnectionString()
    {
        const string raw = "postgres://alice:secret@db.example.com:6543/rge_prod";

        await using NpgsqlDataSource dataSource = NpgsqlDataSourceFactory.Create(raw);
        var csb = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        Assert.Equal(6543, csb.Port);
        Assert.Equal(20, csb.MaxPoolSize);
        Assert.Equal(0, csb.MaxAutoPrepare);
        Assert.Equal(30, csb.ConnectionPruningInterval);
    }
}

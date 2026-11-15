using Dapper;
using Npgsql;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Xunit;

namespace Rge.Finance.Integration.Tests;

public sealed class OfficialGeneralLedgerLifecycleIntegrationTests
{
    [Fact]
    public async Task PostgreSqlOfficialLedgerExposesBalanceFunctionAndImmutabilityTrigger()
    {
        string? connectionString = Environment.GetEnvironmentVariable("RGE_TEST_POSTGRES_CONN");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync();

        string? balanceFunction = await connection.ExecuteScalarAsync<string?>(
            "SELECT to_regprocedure('fn_gl_verify_journal_balance(uuid)')::text;");
        int triggerCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM pg_trigger WHERE tgname = 'trg_gl_journal_batches_immutability';");

        Assert.Equal("fn_gl_verify_journal_balance(uuid)", balanceFunction);
        Assert.Equal(1, triggerCount);
    }
}

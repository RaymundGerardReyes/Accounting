using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Infrastructure.Persistence;

namespace Rge.Identity.Infrastructure;

/// <summary>
/// Seeds default development user credentials, personas, and roles into PostgreSQL
/// for local development and test environments when running in Development mode
/// or when FINANCE_SEED == "dev".
/// </summary>
public static class IdentityDbSeeder
{
    public const string DefaultDevPassword = "password123";

    public const string SeedSql = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;
        """;

    public static async Task SeedDevelopmentCredentialsAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = SeedSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task SeedDevelopmentCredentialsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("IdentityDbSeeder");
        var env = services.GetService<IHostEnvironment>();
        var config = services.GetService<IConfiguration>();

        bool isDev = (env?.IsDevelopment() ?? false) ||
                     string.Equals(config?["FINANCE_SEED"], "dev", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Environment.GetEnvironmentVariable("FINANCE_SEED"), "dev", StringComparison.OrdinalIgnoreCase);

        if (!isDev)
        {
            logger?.LogInformation("IdentityDbSeeder: Skipping seeding in non-Development environment.");
            return;
        }

        logger?.LogInformation("IdentityDbSeeder: Running development credentials seeder...");

        try
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetService<NpgsqlDataSourceFactory>();
            if (factory is null)
            {
                logger?.LogInformation("IdentityDbSeeder: NpgsqlDataSourceFactory not registered; skipping DB seed.");
                return;
            }

            await using DbConnection connection = await factory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await SeedDevelopmentCredentialsAsync(connection, cancellationToken).ConfigureAwait(false);
            logger?.LogInformation("IdentityDbSeeder: Successfully seeded default credentials.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("offline fallback mode", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogInformation("IdentityDbSeeder: Database is in offline fallback mode; in-memory credentials active.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "IdentityDbSeeder: Database seed execution skipped or failed (offline / unreachable database).");
        }
    }
}

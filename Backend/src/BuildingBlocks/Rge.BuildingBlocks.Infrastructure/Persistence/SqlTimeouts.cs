namespace Rge.BuildingBlocks.Infrastructure.Persistence;



/// <summary>

/// Authoritative command timeout budgets in seconds for SQL execution paths.

/// Prevents hung queries from exhausting the connection pool.

/// </summary>

public static class SqlTimeouts

{

    public const int WebhookHotWriteSeconds = 2;

    public const int FastReadSeconds = 3;

    public const int StandardCommandSeconds = 5;

    public const int BatchReconciliationSeconds = 30;

    public const int MigrationSeconds = 120;

}

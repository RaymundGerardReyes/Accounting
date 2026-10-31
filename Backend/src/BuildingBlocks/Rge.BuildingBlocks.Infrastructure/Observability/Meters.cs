using System.Diagnostics.Metrics;



namespace Rge.BuildingBlocks.Infrastructure.Observability;



/// <summary>

/// High-performance System.Diagnostics.Metrics instruments for tracking latency budgets,

/// webhook throughput, outbox depth, and SoD/authz blocks.

/// </summary>

public static class Meters

{

    public const string MeterName = "Rge.Accounting.Metrics";



    private static readonly Meter AppMeter = new(MeterName, "1.0.0");



    public static readonly Histogram<double> WebhookDurationMs = AppMeter.CreateHistogram<double>(

        "rge.webhooks.duration_ms",

        unit: "ms",

        description: "PayMongo webhook processing latency in milliseconds (target p99 < 50ms).");



    public static readonly Counter<long> OrdersCreatedCount = AppMeter.CreateCounter<long>(

        "rge.orders.created_total",

        description: "Total commercial orders and department invoices created.");



    public static readonly Counter<long> SodViolationsCount = AppMeter.CreateCounter<long>(

        "rge.security.sod_violations_total",

        description: "Total Four-Eyes Separation of Duties violations blocked.");

}

using System.Diagnostics;

namespace Rge.BuildingBlocks.Infrastructure.Observability;

public static class ActivitySources
{
    public const string ServiceName = "Rge.Accounting.Host";
    public const string ServiceVersion = "1.0.0";

    public const string ApplicationSourceName = "Rge.Accounting.Application";
    public const string DatabaseSourceName = "Rge.Accounting.Database";
    public const string ExternalSourceName = "Rge.Accounting.External";
    public const string WorkerSourceName = "Rge.Accounting.Workers";

    public static readonly ActivitySource Main = new(ServiceName, ServiceVersion);
    public static readonly ActivitySource Application = new(ApplicationSourceName, ServiceVersion);
    public static readonly ActivitySource Database = new(DatabaseSourceName, ServiceVersion);
    public static readonly ActivitySource External = new(ExternalSourceName, ServiceVersion);
    public static readonly ActivitySource Workers = new(WorkerSourceName, ServiceVersion);
}

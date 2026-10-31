using System.Diagnostics;



namespace Rge.BuildingBlocks.Infrastructure.Observability;



/// <summary>

/// Central OpenTelemetry ActivitySource definitions for distributed tracing across modules.

/// </summary>

public static class ActivitySources

{

    public const string ServiceName = "Rge.Accounting.Platform";

    public const string ServiceVersion = "1.0.0";



    public static readonly ActivitySource Main = new(ServiceName, ServiceVersion);

}

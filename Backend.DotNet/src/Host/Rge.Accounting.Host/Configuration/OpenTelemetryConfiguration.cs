using System.Diagnostics;

using Rge.BuildingBlocks.Infrastructure.Observability;



namespace Rge.Accounting.Host.Configuration;



/// <summary>

/// Registers the platform ActivityListener and Meter configuration so all traces and metrics

/// share the unified Correlation ID.

/// </summary>

public static class OpenTelemetryConfiguration

{

    public static IServiceCollection AddPlatformObservability(this IServiceCollection services)

    {

        ArgumentNullException.ThrowIfNull(services);



        Activity.DefaultIdFormat = ActivityIdFormat.W3C;

        Activity.ForceDefaultIdFormat = true;



        services.AddSingleton(ActivitySources.Main);

        return services;

    }

}

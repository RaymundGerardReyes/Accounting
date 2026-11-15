using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton(ActivitySources.Application);
        services.AddSingleton(ActivitySources.Database);
        services.AddSingleton(ActivitySources.External);
        services.AddSingleton(ActivitySources.Workers);

        return services;
    }
}

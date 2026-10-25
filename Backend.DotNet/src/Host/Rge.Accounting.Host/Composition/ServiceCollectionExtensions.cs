using Rge.Accounting.Host.Configuration;

using Rge.Accounting.Host.Pipeline;

using Rge.BuildingBlocks.Api.Serialization;

using Rge.BuildingBlocks.Infrastructure.Caching;

using Rge.BuildingBlocks.SharedKernel.Primitives;

using Rge.BuildingBlocks.SharedKernel.Time;



namespace Rge.Accounting.Host.Composition;



internal sealed class SystemClock : IClock

{

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

}



/// <summary>

/// Registers cross-cutting BuildingBlocks, JSON source-generation, security pipelines,

/// rate limiters, and module composition services.

/// </summary>

public static class ServiceCollectionExtensions

{

    public static IServiceCollection AddAccountingPlatformHost(

        this IServiceCollection services,

        IConfiguration configuration)

    {

        ArgumentNullException.ThrowIfNull(services);

        ArgumentNullException.ThrowIfNull(configuration);



        services.ConfigureHttpJsonOptions(options =>

        {

            JsonDefaults.ConfigureOptions(options.SerializerOptions);

            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);

        });



        services.AddProblemDetails();

        services.AddExceptionHandler<GlobalExceptionHandler>();



        services.AddSingleton<IClock>(_ => new SystemClock());

        services.AddSingleton<IBusinessDayService>(_ => new PhilippineBusinessCalendar());

        services.AddSingleton<CacheInvalidator>();

        services.AddSingleton<ModuleRegistry>();



        services.AddStrictCors(configuration);

        services.AddEdgeRateLimiting();

        services.AddPlatformObservability();



        return services;

    }

}

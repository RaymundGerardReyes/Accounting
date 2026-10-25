using Rge.Accounting.Host.Configuration;
using Rge.Accounting.Host.Pipeline;
using Rge.Audit.Api;
using Rge.Audit.Infrastructure;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Caching;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Time;
using Rge.Identity.Api;
using Rge.Identity.Api.Authentication;
using Rge.Identity.Infrastructure;
using Rge.Payments.Api;
using Rge.Payments.Infrastructure;
using Rge.Receipts.Api;
using Rge.Receipts.Infrastructure;

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

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, ServerCurrentActor>();

        services.AddSingleton<IClock>(_ => new SystemClock());
        services.AddSingleton<IBusinessDayService>(_ => new PhilippineBusinessCalendar());
        services.AddSingleton<CacheInvalidator>();

        // Register Persistence Connection Scope
        services.AddScoped<ConnectionScope>();

        // Register Module Subsystems
        services.AddIdentityModule();
        services.AddReceiptsModule();
        services.AddPaymentsModule(configuration);
        services.AddAuditModule();

        // Register Minimal API Module Endpoints
        services.AddSingleton(_ =>
        {
            var registry = new ModuleRegistry();
            registry.Register(new AuthEndpoints());
            registry.Register(new FinanceIdentityEndpoints());
            registry.Register(new ReceiptEndpoints());
            registry.Register(new PaymentsEndpoints());
            registry.Register(new AuditEndpoints());
            return registry;
        });

        services.AddStrictCors(configuration);
        services.AddEdgeRateLimiting();
        services.AddPlatformObservability();

        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

public static class KotobeeServiceCollectionExtensions
{
    public static IServiceCollection AddKotobeeIntegration(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configuration is not null)
        {
            services.Configure<KotobeeOptions>(
                configuration.GetSection(KotobeeOptions.SectionName));
        }

        // Typed HttpClient for Kotobee API with a safe timeout
        services.AddHttpClient<IKotobeeApiClient, KotobeeApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Core services & adapters
        services.AddSingleton<IKotobeeFulfillmentQueue, ChannelKotobeeFulfillmentQueue>();
        services.AddSingleton<IKotobeeCatalogMappingResolver, KotobeeCatalogMappingResolver>();
        services.AddScoped<IPaidOrderVerifier, PaymentsContractPaidOrderVerifier>();
        services.AddScoped<IKotobeeEntitlementRepository>(sp =>
            new PgKotobeeEntitlementRepository(
                sp.GetService<NpgsqlDataSource>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<PgKotobeeEntitlementRepository>>()));

        services.AddScoped<FulfillKotobeeOrderService>();
        services.AddScoped<OnOrderPaidEnqueueKotobeeHandler>();
        services.AddScoped<OrderPaidKotobeeOutboxHandler>();

        // Sequential rate-limited background worker (respects Kotobee's 1 req/sec rule)
        services.AddHostedService<KotobeeFulfillmentWorker>();

        return services;
    }
}

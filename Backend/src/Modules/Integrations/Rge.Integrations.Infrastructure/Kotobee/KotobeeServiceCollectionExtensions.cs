using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            services
                .AddOptions<KotobeeOptions>()
                .Bind(configuration.GetSection(KotobeeOptions.SectionName))
                .Validate(opts =>
                {
                    if (!opts.Enabled) return true;

                    var hasAuth = !string.IsNullOrWhiteSpace(opts.AccessKey)
                               || !string.IsNullOrWhiteSpace(opts.Serial);
                    var isLibrary = string.Equals(opts.PublishingMode, "library", StringComparison.OrdinalIgnoreCase);
                    var hasLibId = !isLibrary || !string.IsNullOrWhiteSpace(opts.DefaultLibraryId);

                    return hasAuth && hasLibId && opts.MinRequestIntervalMs >= 1000;
                }, "Kotobee integration is enabled, but AccessKey/Serial, DefaultLibraryId (for library mode), or MinRequestIntervalMs (>= 1000) is invalid.")
                .ValidateOnStart();
        }

        // Typed HttpClient for Kotobee API with a safe timeout
        services.AddHttpClient<IKotobeeApiClient, KotobeeApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Typed HttpClient for Google Apps Script Sheet Status Writer with safe timeout
        services.AddHttpClient<IKotobeeSheetStatusWriter, AppsScriptKotobeeSheetStatusWriter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        // Core services & adapters
        services.AddSingleton<IKotobeeFulfillmentQueue, ChannelKotobeeFulfillmentQueue>();
        services.AddSingleton<IntelligentSheetSchemaAdapter>();
        services.AddSingleton<KotobeePreExecutionIsolationGuard>();
        services.AddScoped<IAdminAlertEmailSender, SmtpAdminAlertEmailSender>();
        services.AddScoped<ITamperAlertSink, DualPostgresAndSmtpTamperAlertSink>();
        services.AddScoped<IKotobeeSheetSnapshotStore, PgKotobeeSheetSnapshotStore>();
        services.AddScoped<IKotobeeCatalogMappingResolver, DynamicSheetKotobeeCatalogProvider>();
        services.AddScoped<IPaidOrderVerifier, PaymentsContractPaidOrderVerifier>();
        services.AddScoped<PgKotobeeEntitlementRepository>(sp =>
            new PgKotobeeEntitlementRepository(
                sp.GetService<NpgsqlDataSource>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<PgKotobeeEntitlementRepository>>()));
        services.AddScoped<IKotobeeEntitlementRepository>(sp => sp.GetRequiredService<PgKotobeeEntitlementRepository>());
        services.AddScoped<IKotobeeSheetWriteBackTracker>(sp => sp.GetRequiredService<PgKotobeeEntitlementRepository>());

        services.AddScoped<KotobeeSheetWriteBackCoordinator>();
        services.AddScoped<FulfillKotobeeOrderService>();
        services.AddScoped<OnOrderPaidEnqueueKotobeeHandler>();
        services.AddScoped<OrderPaidKotobeeOutboxHandler>();

        // Sequential rate-limited background worker (respects Kotobee's 1 req/sec rule)
        services.AddHostedService<KotobeeFulfillmentWorker>();

        return services;
    }
}

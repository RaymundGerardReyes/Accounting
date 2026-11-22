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
                .Bind(configuration.GetSection(KotobeeOptions.SectionName));

            services.PostConfigure<KotobeeOptions>(opts =>
            {
                var section = configuration.GetSection(KotobeeOptions.SectionName);
                var hasFlatKey = !string.IsNullOrWhiteSpace(configuration["KOTOBEE_ACCESS_KEY"]);
                var hasFlatSerial = !string.IsNullOrWhiteSpace(configuration["KOTOBEE_SERIAL"]);
                var hasConfiguredSection = section.Exists() || hasFlatKey || hasFlatSerial;

                // 1. If explicit KOTOBEE_ENABLED env var is set, respect it
                if (bool.TryParse(configuration["KOTOBEE_ENABLED"], out var enabledVal))
                {
                    opts.Enabled = enabledVal;
                }
                // 2. If no section and no Kotobee credentials exist in environment, auto-disable to prevent startup crashes
                else if (!hasConfiguredSection)
                {
                    opts.Enabled = false;
                }

                // 3. Flat env var overrides (for Heroku / Docker / PaaS)
                if (hasFlatKey)
                    opts.AccessKey = configuration["KOTOBEE_ACCESS_KEY"]!.Trim();
                if (hasFlatSerial)
                    opts.Serial = configuration["KOTOBEE_SERIAL"]!.Trim();
                if (!string.IsNullOrWhiteSpace(configuration["KOTOBEE_LIBRARY_ID"]))
                    opts.DefaultLibraryId = configuration["KOTOBEE_LIBRARY_ID"]!.Trim();
                else if (!string.IsNullOrWhiteSpace(configuration["KOTOBEE_DEFAULT_LIBRARY_ID"]))
                    opts.DefaultLibraryId = configuration["KOTOBEE_DEFAULT_LIBRARY_ID"]!.Trim();
                if (!string.IsNullOrWhiteSpace(configuration["KOTOBEE_PUBLISHING_MODE"]))
                    opts.PublishingMode = configuration["KOTOBEE_PUBLISHING_MODE"]!.Trim();
                if (!string.IsNullOrWhiteSpace(configuration["KOTOBEE_SHEET_SECRET"]))
                    opts.SheetWebhookSecret = configuration["KOTOBEE_SHEET_SECRET"]!.Trim();
                else if (!string.IsNullOrWhiteSpace(configuration["KOTOBEE_SHEET_WEBHOOK_SECRET"]))
                    opts.SheetWebhookSecret = configuration["KOTOBEE_SHEET_WEBHOOK_SECRET"]!.Trim();

                if ((!string.IsNullOrWhiteSpace(opts.AccessKey) || !string.IsNullOrWhiteSpace(opts.Serial)) &&
                    configuration["KOTOBEE_ENABLED"]?.Equals("false", StringComparison.OrdinalIgnoreCase) != true &&
                    !string.Equals(configuration["Integrations:Kotobee:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
                {
                    opts.Enabled = true;
                }
            });

            services
                .AddOptions<KotobeeOptions>()
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
        services.AddScoped<IKotobeeOrderEligibilityChecker, OrderKotobeeEligibilityChecker>();
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

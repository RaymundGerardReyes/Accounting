using Microsoft.Extensions.DependencyInjection;
using Rge.Integrations.Application.GetFormStatus;
using Rge.Integrations.Application.IntegrationStatusProbe;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Application.SaveCredentials;
using Rge.Integrations.Application.SheetSyncHandler;
using Rge.Integrations.Application.SubmitGoogleForm;
using Rge.Integrations.Application.TestPayMongo;
using Rge.Integrations.Application.TestSmtp;

namespace Rge.Integrations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var secretStore = new SecretStoreWriter();
        services.AddSingleton<ISecretStore>(secretStore);
        services.AddSingleton<IPayMongoProbe>(secretStore);
        services.AddSingleton<IIntegrationDiagnosticProbe>(secretStore);

        services.AddSingleton<IGoogleSheetsClient, GoogleSheetsClient>();
        services.AddSingleton<ISmtpProbe, SmtpClientFactory>();
        services.AddSingleton<IGoogleFormsSubmissionStore, InMemoryGoogleFormsStore>();

        services.AddScoped<SaveCredentialsHandler>();
        services.AddScoped<TestSmtpHandler>();
        services.AddScoped<TestPayMongoHandler>();
        services.AddScoped<IntegrationStatusProbeHandler>();
        services.AddScoped<GetFormStatusHandler>();
        services.AddScoped<SubmitGoogleFormHandler>();
        services.AddScoped<SheetSyncHandlerHandler>();

        return services;
    }
}


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.Ports;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Payments.Infrastructure.PayMongo;

namespace Rge.Payments.Infrastructure;

/// <summary>
/// Wires PayMongo infrastructure services, typed HttpClient, and handlers into DI.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<PayMongoOptions>(options =>
        {
            configuration.GetSection(PayMongoOptions.SectionName).Bind(options);

            // Overlay secrets from the isolated Secrets configuration section
            string? secretKey = configuration["Secrets:PayMongoSecretKey"];
            if (!string.IsNullOrWhiteSpace(secretKey))
            {
                options.SecretKey = secretKey;
            }

            string? webhookSecret = configuration["Secrets:PayMongoWebhookSecret"];
            if (!string.IsNullOrWhiteSpace(webhookSecret))
            {
                options.WebhookSecret = webhookSecret;
            }
        });

        services.AddSingleton<PayMongoSignatureVerifier>();
        services.AddHttpClient<IPaymentGateway, PayMongoClient>();

        services.AddScoped<InitiateAttemptHandler>();
        services.AddScoped<ReceivePayMongoWebhookHandler>();

        return services;
    }
}


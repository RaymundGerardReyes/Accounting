using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.Orders.GetOrder;
using Rge.Payments.Application.Orders.ListOrders;
using Rge.Payments.Application.PaymentAttempts.ExpireAttempt;
using Rge.Payments.Application.PaymentAttempts.FailAttempt;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.PaymentAttempts.PollAndReconcile;
using Rge.Payments.Application.Ports;
using Rge.Payments.Application.Reconciliation.ReconcileAllNonTerminal;
using Rge.Payments.Application.Reconciliation.ReconcileOne;
using Rge.Payments.Application.Sandbox.CreateAndExpireTest;
using Rge.Payments.Application.Sandbox.SimulateWebhook;
using Rge.Payments.Application.Webhooks.ProcessWebhookEvent;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Payments.Domain.Pricing;
using Rge.Payments.Infrastructure.Catalog;
using Rge.Payments.Infrastructure.Outbox;
using Rge.Payments.Infrastructure.PayMongo;
using Rge.Payments.Infrastructure.Persistence.Repositories;

namespace Rge.Payments.Infrastructure;

/// <summary>
/// Wires PayMongo infrastructure services, repositories, typed HttpClient, and application handlers into DI.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<PayMongoOptions>(options =>
        {
            if (configuration is not null)
            {
                configuration.GetSection(PayMongoOptions.SectionName).Bind(options);

                string? secretKey = configuration["Secrets:PayMongoSecretKey"] ?? configuration["PAYMONGO_SECRET_KEY"];
                if (!string.IsNullOrWhiteSpace(secretKey))
                {
                    options.SecretKey = secretKey;
                }

                string? webhookSecret = configuration["Secrets:PayMongoWebhookSecret"] ?? configuration["PAYMONGO_WEBHOOK_SECRET"];
                if (!string.IsNullOrWhiteSpace(webhookSecret))
                {
                    options.WebhookSecret = webhookSecret;
                }
            }
        });

        services.AddSingleton<PayMongoSignatureVerifier>();
        services.AddSingleton<IWebhookSigningKeyProvider, PayMongoWebhookSigningKeyProvider>();
        services.AddHttpClient<IPaymentGateway, PayMongoClient>();

        // Domain & Infrastructure Repositories
        services.AddSingleton<IPricingPolicy, ServerAuthoritativePricing>();
        services.AddSingleton<IProductCatalog, JsonProductCatalog>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IPaymentAttemptRepository, PaymentAttemptRepository>();
        services.AddScoped<IWebhookEventStore, WebhookEventStore>();
        services.AddScoped<IOrderPaidOutboxPublisher, OrderPaidOutboxPublisher>();

        // Application Command & Query Handlers
        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<ListOrdersHandler>();
        services.AddScoped<InitiateAttemptHandler>();
        services.AddScoped<PollAndReconcileHandler>();
        services.AddScoped<ExpireAttemptHandler>();
        services.AddScoped<FailAttemptHandler>();
        services.AddScoped<ReconcileOneHandler>();
        services.AddScoped<ReconcileAllNonTerminalHandler>();
        services.AddScoped<CreateAndExpireTestHandler>();
        services.AddScoped<SimulateWebhookHandler>();
        services.AddScoped<ProcessWebhookEventHandler>();
        services.AddScoped<ReceivePayMongoWebhookHandler>();

        return services;
    }
}

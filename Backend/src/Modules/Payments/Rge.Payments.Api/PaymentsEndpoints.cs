using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.Payments.Api.Admin;
using Rge.Payments.Api.Attempts;
using Rge.Payments.Api.Hubs;
using Rge.Payments.Api.Orders;
using Rge.Payments.Api.Sandbox;
using Rge.Payments.Api.Webhooks;

namespace Rge.Payments.Api;

/// <summary>
/// Root Payments endpoint module mapping Orders, Attempts, Webhooks, Reconciliation, Sandbox routes, and SignalR Hub.
/// </summary>
public sealed class PaymentsEndpoints : IEndpointModule
{
    private readonly IEndpointModule[] _modules =
    [
        new CreateOrderEndpoint(),
        new ManageOrderEndpoints(),
        new GetOrderEndpoint(),
        new ListOrdersEndpoint(),
        new InitiateAttemptEndpoint(),
        new GetPaymentEndpoint(),
        new ExpireEndpoint(),
        new FailEndpoint(),
        new OutboxEndpoints(),
        new ReconcileEndpoints(),
        new WebhookEventsEndpoint(),
        new SandboxEndpoints(),
        new PayMongoWebhookEndpoint()
    ];

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        foreach (IEndpointModule module in _modules)
        {
            module.MapEndpoints(app);
        }

        // Map SignalR real-time payment hub for checkout countdown and status synchronization
        app.MapHub<PaymentHub>("/hubs/payment");
        app.MapHub<PaymentHub>("/hubs/payments");
        app.MapHub<PaymentHub>("/api/hubs/payments");
    }
}

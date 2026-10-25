using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.Payments.Api.Admin;
using Rge.Payments.Api.Attempts;
using Rge.Payments.Api.Orders;
using Rge.Payments.Api.Sandbox;
using Rge.Payments.Api.Webhooks;

namespace Rge.Payments.Api;

/// <summary>
/// Root Payments endpoint module mapping Orders, Attempts, Webhooks, Reconciliation, and Sandbox routes.
/// </summary>
public sealed class PaymentsEndpoints : IEndpointModule
{
    private readonly IEndpointModule[] _modules =
    [
        new CreateOrderEndpoint(),
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
    }
}

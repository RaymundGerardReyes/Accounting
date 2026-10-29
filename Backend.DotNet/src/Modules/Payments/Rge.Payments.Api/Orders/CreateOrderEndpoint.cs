using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.Orders.CreateOrder;

namespace Rge.Payments.Api.Orders;

public sealed class CreateOrderEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/orders", HandleAsync)
           .WithName("CreateOrder")
           .WithTags("Payments.Orders");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        CreateOrderRequest request,
        CreateOrderHandler handler,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(currentActor);

        var command = new CreateOrderCommand(
            Title: request.Title,
            Category: request.Category,
            ReferenceCode: request.ReferenceCode,
            AmountMinor: request.AmountMinor,
            Quantity: request.Quantity,
            CustomerName: request.CustomerName,
            CustomerEmail: request.CustomerEmail,
            CustomerPhone: request.CustomerPhone,
            Metadata: request.Metadata,
            UserId: string.IsNullOrWhiteSpace(currentActor.ActorId) ? "acme_admin" : currentActor.ActorId);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, order => Results.Created($"/api/v1/orders/{order.Id}", order));
    }
}

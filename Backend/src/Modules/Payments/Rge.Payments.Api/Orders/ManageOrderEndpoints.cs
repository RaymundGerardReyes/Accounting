using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.Orders.ManageOrder;

namespace Rge.Payments.Api.Orders;

public sealed class ManageOrderEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPatch("/api/v1/orders/{order_id}", HandleUpdateAsync)
           .WithName("PatchOrder")
           .WithTags("Payments.Orders");

        app.MapPut("/api/v1/orders/{order_id}", HandleUpdateAsync)
           .WithName("PutOrder")
           .WithTags("Payments.Orders");

        app.MapDelete("/api/v1/orders/{order_id}", HandleDeleteAsync)
           .WithName("DeleteOrder")
           .WithTags("Payments.Orders");

        app.MapPost("/api/v1/orders/{order_id}/cancel", HandleCancelAsync)
           .WithName("CancelOrder")
           .WithTags("Payments.Orders");
    }

    private static async Task<IResult> HandleUpdateAsync(
        HttpContext httpContext,
        string order_id,
        UpdateOrderRequest request,
        UpdateOrderHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        long? amount = request.AmountMinor ?? request.Amount;
        string? title = request.Title ?? request.Description;

        var command = new UpdateOrderCommand(
            OrderId: order_id,
            Title: title,
            AmountMinor: amount,
            Currency: request.Currency,
            Metadata: request.Metadata);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, response => Results.Ok(response));
    }

    private static async Task<IResult> HandleDeleteAsync(
        HttpContext httpContext,
        string order_id,
        string? reason,
        CancelOrderHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var command = new CancelOrderCommand(
            OrderId: order_id,
            Reason: reason ?? "Order deleted via API");

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, response => Results.Ok(response));
    }

    private static async Task<IResult> HandleCancelAsync(
        HttpContext httpContext,
        string order_id,
        CancelOrderRequest? request,
        CancelOrderHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var command = new CancelOrderCommand(
            OrderId: order_id,
            Reason: request?.Reason ?? "Order cancelled via API");

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, response => Results.Ok(response));
    }
}


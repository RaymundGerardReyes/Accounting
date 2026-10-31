using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Application.Orders.ListOrders;

namespace Rge.Payments.Api.Orders;

public sealed class ListOrdersEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/orders", HandleAsync)
           .WithName("ListOrders")
           .WithTags("Payments.Orders");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        string? status,
        int? limit,
        ListOrdersHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var query = new ListOrdersQuery(status, limit ?? 50);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, orders => Results.Ok(orders));
    }
}

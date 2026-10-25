using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Application.Orders.GetOrder;

namespace Rge.Payments.Api.Orders;

public sealed class GetOrderEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/orders/{order_id}", HandleAsync)
           .RequireAuthorization()
           .WithName("GetOrder")
           .WithTags("Payments.Orders");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        string order_id,
        GetOrderHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var query = new GetOrderQuery(order_id);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, order => Results.Ok(order));
    }
}

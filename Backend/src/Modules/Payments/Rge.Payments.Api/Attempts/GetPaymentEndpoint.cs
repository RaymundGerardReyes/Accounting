using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Application.PaymentAttempts.PollAndReconcile;

namespace Rge.Payments.Api.Attempts;

public sealed class GetPaymentEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/payments/{payment_id}", HandleAsync)
           .WithName("GetPaymentStatus")
           .WithTags("Payments.Attempts");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        string payment_id,
        bool? auto_reconcile,
        PollAndReconcileHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var query = new PollAndReconcileQuery(payment_id, auto_reconcile ?? true);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, attempt => Results.Ok(attempt));
    }
}

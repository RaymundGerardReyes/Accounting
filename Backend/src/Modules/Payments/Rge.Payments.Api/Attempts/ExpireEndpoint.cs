using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.PaymentAttempts.ExpireAttempt;

namespace Rge.Payments.Api.Attempts;

public sealed class ExpireEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/payments/{payment_id}/expire", HandleAsync)
           .WithName("ExpirePayment")
           .WithTags("Payments.Attempts");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        string payment_id,
        ExpirePaymentRequest? request,
        ExpireAttemptHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var command = new ExpireAttemptCommand(payment_id, request?.Reason);
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, resp => Results.Ok(resp));
    }
}

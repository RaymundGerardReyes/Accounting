using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.PaymentAttempts.FailAttempt;

namespace Rge.Payments.Api.Attempts;

public sealed class FailEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/payments/{payment_id}/fail", HandleAsync)
           .RequireAuthorization()
           .WithName("FailPayment")
           .WithTags("Payments.Attempts");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        string payment_id,
        FailPaymentRequest? request,
        FailAttemptHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        string? code = request?.FailureCode ?? request?.Code;
        string? message = request?.FailureMessage ?? request?.Message;

        var command = new FailAttemptCommand(payment_id, code, message);
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, resp => Results.Ok(resp));
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;

namespace Rge.Payments.Api.Attempts;

public sealed class InitiateAttemptEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/orders/{order_id}/payment-attempts", HandleAsync)
           .RequireAuthorization()
           .WithName("InitiatePaymentAttempt")
           .WithTags("Payments.Attempts");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        string order_id,
        CreatePaymentAttemptRequest request,
        InitiateAttemptHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        string? headerIdempotency = httpContext.Request.Headers["Idempotency-Key"];
        string? effectiveIdempotency = !string.IsNullOrWhiteSpace(headerIdempotency)
            ? headerIdempotency
            : request.IdempotencyKey;

        var command = new InitiateAttemptCommand(
            OrderId: order_id,
            PaymentMethod: request.PaymentMethod,
            IdempotencyKey: effectiveIdempotency,
            CustomerName: request.CustomerName,
            CustomerEmail: request.CustomerEmail,
            CustomerPhone: request.CustomerPhone,
            SuccessUrl: request.SuccessUrl,
            CancelUrl: request.CancelUrl);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, attempt => Results.Created($"/api/v1/payments/{attempt.Id}", attempt));
    }
}

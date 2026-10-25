using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.Sandbox.CreateAndExpireTest;
using Rge.Payments.Application.Sandbox.SimulateWebhook;

namespace Rge.Payments.Api.Sandbox;

public sealed class SandboxEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/payments/attempts/{attempt_id}/simulate", SimulateAttemptAsync)
           .RequireAuthorization()
           .WithName("SimulatePaymentAttempt")
           .WithTags("Payments.Sandbox");

        app.MapPost("/api/v1/sandbox/create-and-expire-test", CreateAndExpireTestAsync)
           .RequireAuthorization()
           .WithName("CreateAndExpireTest")
           .WithTags("Payments.Sandbox");

        app.MapPost("/api/v1/sandbox/simulate-webhook", SimulateWebhookAsync)
           .RequireAuthorization()
           .WithName("SimulateSignedWebhook")
           .WithTags("Payments.Sandbox");
    }

    private static async Task<IResult> SimulateAttemptAsync(
        HttpContext httpContext,
        string attempt_id,
        SimulatePaymentAttemptRequest? request,
        SimulateWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var command = new SimulateWebhookCommand(
            PaymentAttemptId: attempt_id,
            EventType: request?.EventType ?? "checkout_session.payment.paid",
            FailureCode: request?.FailureCode,
            FailureMessage: request?.FailureMessage);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, resp => Results.Ok(resp));
    }

    private static async Task<IResult> CreateAndExpireTestAsync(
        HttpContext httpContext,
        string? channel,
        CreateAndExpireTestHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(handler);

        var command = new CreateAndExpireTestCommand(channel ?? "qrph");
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, resp => Results.Created("/api/v1/sandbox/create-and-expire-test", resp));
    }

    private static async Task<IResult> SimulateWebhookAsync(
        HttpContext httpContext,
        SimulateWebhookRequest request,
        SimulateWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);

        var command = new SimulateWebhookCommand(
            PaymentAttemptId: request.PaymentAttemptId,
            EventType: request.EventType ?? "checkout_session.payment.paid",
            FailureCode: request.FailureCode,
            FailureMessage: request.FailureMessage);

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ToHttpResult(httpContext, resp => Results.Ok(resp));
    }
}

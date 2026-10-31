using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Payments.Infrastructure.PayMongo;

namespace Rge.Payments.Api.Webhooks;

/// <summary>
/// Minimal API endpoint for POST /api/v1/webhooks/paymongo.
/// Enforces raw-body HMAC-SHA256 verification before passing the payload to ReceivePayMongoWebhookHandler.
/// </summary>
public sealed class PayMongoWebhookEndpoint : IEndpointModule
{
    public const string SignatureHeaderName = "Paymongo-Signature";
    private const int MaxWebhookBodyBytes = 256 * 1024; // 256 KB strict cap on webhook payloads

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/v1/webhooks/paymongo", HandleWebhookAsync)
           .AllowAnonymous()
           .RequireRateLimiting("RateLimit.Webhook")
           .WithName("ReceivePayMongoWebhook")
           .WithTags("Payments.Webhooks");
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext httpContext,
        PayMongoSignatureVerifier signatureVerifier,
        ReceivePayMongoWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.ContentLength > MaxWebhookBodyBytes)
        {
            var sizeError = Error.Validation("Webhooks.PayloadTooLarge", "Webhook payload exceeds 256 KB limit.");
            return Results.Problem(ProblemDetailsFactory.Create(httpContext, sizeError));
        }

        string? signatureHeader = httpContext.Request.Headers[SignatureHeaderName];

        string rawBody;
        using (var reader = new StreamReader(
                   httpContext.Request.Body,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 4096,
                   leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        if (!signatureVerifier.IsValidSignature(signatureHeader, rawBody))
        {
            var sigError = Error.Unauthorized(
                "Webhooks.InvalidSignature",
                "Paymongo-Signature HMAC-SHA256 verification failed or timestamp expired.");
            return Results.Problem(ProblemDetailsFactory.Create(httpContext, sigError));
        }

        var command = new ReceivePayMongoWebhookCommand(signatureHeader, rawBody);
        Result<WebhookAckResponse> result = await handler.HandleAsync(command, cancellationToken);

        return result.ToHttpResult(httpContext, ack => Results.Ok(ack));
    }
}


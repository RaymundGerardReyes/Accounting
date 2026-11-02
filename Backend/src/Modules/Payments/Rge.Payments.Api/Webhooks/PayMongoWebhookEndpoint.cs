#pragma warning disable CA1873
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Payments.Infrastructure.PayMongo;

namespace Rge.Payments.Api.Webhooks;

/// <summary>
/// Minimal API endpoint for PayMongo webhooks.
/// Supports canonical route /api/v1/webhooks/paymongo and aliases /api/payments/webhook, /webhooks/paymongo.
/// Enforces raw-body HMAC-SHA256 verification and returns standard 200–209 JSON acknowledgements.
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

        app.MapPost("/api/payments/webhook", HandleWebhookAsync)
           .AllowAnonymous()
           .RequireRateLimiting("RateLimit.Webhook")
           .WithName("ReceivePayMongoWebhookLegacyAlias")
           .WithTags("Payments.Webhooks");

        app.MapPost("/webhooks/paymongo", HandleWebhookAsync)
           .AllowAnonymous()
           .RequireRateLimiting("RateLimit.Webhook")
           .WithName("ReceivePayMongoWebhookRootAlias")
           .WithTags("Payments.Webhooks");
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext httpContext,
        PayMongoSignatureVerifier signatureVerifier,
        ReceivePayMongoWebhookHandler handler,
        ILogger<PayMongoWebhookEndpoint> logger,
        CancellationToken cancellationToken)
    {
        string path = httpContext.Request.Path.Value ?? "/";
        string? signatureHeader = httpContext.Request.Headers[SignatureHeaderName];
        bool hasSig = !string.IsNullOrWhiteSpace(signatureHeader);

        httpContext.Request.EnableBuffering();

        if (httpContext.Request.ContentLength > MaxWebhookBodyBytes)
        {
            logger.LogWarning("[PAYMONGO-WEBHOOK-REJECTED] Path={Path} | Reason=PayloadTooLarge | Length={Length}",
                path, httpContext.Request.ContentLength);
            return Results.Json(
                data: new
                {
                    received = false,
                    error = "PayloadTooLarge",
                    message = "Webhook payload exceeds 256 KB limit."
                },
                statusCode: StatusCodes.Status400BadRequest);
        }

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

        logger.LogInformation(
            "[PAYMONGO-WEBHOOK-INBOUND] Path={Path} | HasSignature={HasSig} | PayloadBytes={Bytes}",
            path,
            hasSig,
            Encoding.UTF8.GetByteCount(rawBody));

        if (!signatureVerifier.IsValidSignature(signatureHeader, rawBody))
        {
            logger.LogWarning(
                "[PAYMONGO-WEBHOOK-REJECTED] Path={Path} | Reason=InvalidSignature | HasSig={HasSig}",
                path,
                hasSig);
            return Results.Json(
                data: new
                {
                    received = false,
                    error = "InvalidSignature",
                    message = "Paymongo-Signature HMAC-SHA256 verification failed or timestamp expired."
                },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new ReceivePayMongoWebhookCommand(signatureHeader, rawBody);
        Result<WebhookAckResponse> result = await handler.HandleAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "[PAYMONGO-WEBHOOK-REJECTED] Path={Path} | Reason={ErrorCode} | Message={ErrorMessage}",
                path,
                result.Error.Code,
                result.Error.Message);

            return Results.Json(
                data: new
                {
                    received = false,
                    error = result.Error.Code,
                    message = result.Error.Message
                },
                statusCode: StatusCodes.Status400BadRequest);
        }

        logger.LogInformation(
            "[PAYMONGO-WEBHOOK-ACK-200] event_id={ProviderEventId} | type={EventType} | status={ProcessingStatus} | attempt_id={AttemptId}",
            result.Value.ProviderEventId,
            result.Value.EventType,
            result.Value.ProcessingStatus,
            result.Value.PaymentAttemptId ?? "none");

        return Results.Json(
            data: new
            {
                received = true,
                event_id = result.Value.ProviderEventId,
                event_type = result.Value.EventType,
                processing_status = result.Value.ProcessingStatus,
                payment_attempt_id = result.Value.PaymentAttemptId
            },
            statusCode: StatusCodes.Status200OK);
    }
}


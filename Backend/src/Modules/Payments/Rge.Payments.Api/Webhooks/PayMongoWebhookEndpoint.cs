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

        // 1. Receive Webhooks (Canonical & Aliases)
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

        // 2. Auto-Setup Webhooks (Admin Utility)
        app.MapPost("/api/v1/admin/webhooks/paymongo/setup", SetupPayMongoWebhooksAsync)
           .AllowAnonymous()
           .DisableAntiforgery()
           .WithName("SetupPayMongoWebhooks")
           .WithTags("Payments.Admin");
    }

    private static async Task<IResult> SetupPayMongoWebhooksAsync(
        HttpContext httpContext,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IHttpClientFactory clientFactory,
        ILogger<PayMongoWebhookEndpoint> logger,
        CancellationToken cancellationToken)
    {
        string? secretKey = config["Secrets:PayMongoSecretKey"] ?? config["PAYMONGO_SECRET_KEY"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return Results.BadRequest(new { error = "Missing PayMongoSecretKey in configuration." });
        }

        // Automatically determine live Heroku/custom domain from the incoming request headers
        string scheme = httpContext.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? httpContext.Request.Scheme;
        string host = httpContext.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? httpContext.Request.Host.Value ?? "localhost";
        string targetWebhookUrl = $"{scheme}://{host}/api/v1/webhooks/paymongo";

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.paymongo.com/v1/");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:")));
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        var report = new List<string>();

        try
        {
            // Step 1: List all existing webhooks
            using var listResponse = await client.GetAsync("webhooks", cancellationToken).ConfigureAwait(false);
            listResponse.EnsureSuccessStatusCode();
            string listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            using var doc = System.Text.Json.JsonDocument.Parse(listJson);
            bool webhookExists = false;

            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var hook in dataEl.EnumerateArray())
                {
                    string id = hook.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty;
                    if (!hook.TryGetProperty("attributes", out var attrs))
                    {
                        continue;
                    }

                    string url = attrs.TryGetProperty("url", out var urlProp) ? (urlProp.GetString() ?? string.Empty) : string.Empty;
                    string status = attrs.TryGetProperty("status", out var statusProp) ? (statusProp.GetString() ?? string.Empty) : string.Empty;

                    if (!string.IsNullOrWhiteSpace(url) && url.Contains(host, StringComparison.OrdinalIgnoreCase))
                    {
                        webhookExists = true;

                        // Step 2: Re-enable the webhook if it was disabled by PayMongo due to previous retries exhausted
                        if (string.Equals(status, "disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            using var enableResp = await client.PostAsync($"webhooks/{id}/enable", null, cancellationToken).ConfigureAwait(false);
                            if (enableResp.IsSuccessStatusCode)
                            {
                                report.Add($"Successfully re-enabled webhook: {id}");
                            }
                            else
                            {
                                report.Add($"Failed to re-enable webhook: {id}. Please check PayMongo dashboard.");
                            }
                        }
                        else
                        {
                            report.Add($"Webhook {id} is already active and pointing to {url}.");
                        }
                    }
                }
            }

            // Step 3: Create a new webhook if none exists for this domain
            if (!webhookExists)
            {
                var payload = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            events = new[]
                            {
                                "checkout_session.payment.paid",
                                "payment.paid",
                                "payment.failed",
                                "qr_code.expired"
                            },
                            url = targetWebhookUrl
                        }
                    }
                };

                using var content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                using var createResp = await client.PostAsync("webhooks", content, cancellationToken).ConfigureAwait(false);
                string createJson = await createResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (createResp.IsSuccessStatusCode)
                {
                    report.Add($"Created NEW webhook pointing to {targetWebhookUrl}.");
                    using var createDoc = System.Text.Json.JsonDocument.Parse(createJson);
                    string? newSecret = null;
                    if (createDoc.RootElement.TryGetProperty("data", out var newHookData) &&
                        newHookData.TryGetProperty("attributes", out var newHookAttrs) &&
                        newHookAttrs.TryGetProperty("secret_key", out var secProp))
                    {
                        newSecret = secProp.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(newSecret))
                    {
                        report.Add($"CRITICAL: Update your Heroku configuration. Set PAYMONGO_WEBHOOK_SECRET to: {newSecret}");
                    }
                }
                else
                {
                    report.Add($"Failed to create webhook: {createJson}");
                }
            }

            logger.LogInformation("[PAYMONGO-WEBHOOK-SETUP] Completed setup for {TargetUrl} with {ActionsCount} actions",
                targetWebhookUrl, report.Count);

            return Results.Ok(new { success = true, target_url = targetWebhookUrl, actions = report });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PAYMONGO-WEBHOOK-SETUP] Failed to auto-setup PayMongo webhooks.");
            return Results.Problem("An error occurred while contacting PayMongo to setup webhooks.");
        }
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


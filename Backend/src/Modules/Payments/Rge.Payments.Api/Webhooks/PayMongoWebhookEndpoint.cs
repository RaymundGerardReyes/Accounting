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

    public static readonly string[] CanonicalWebhookEvents =
    [
        "checkout_session.payment.paid",
        "payment.paid",
        "payment.failed",
        "source.chargeable",
        "qrph.expired"
    ];

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

        // 2. Auto-Setup & Inspect Webhooks (Admin Utilities)
        app.MapPost("/api/v1/admin/webhooks/paymongo/setup", SetupPayMongoWebhooksAsync)
           .AllowAnonymous()
           .DisableAntiforgery()
           .WithName("SetupPayMongoWebhooks")
           .WithTags("Payments.Admin");

        app.MapGet("/api/v1/admin/webhooks/paymongo/status", InspectPayMongoWebhooksAsync)
           .AllowAnonymous()
           .WithName("InspectPayMongoWebhooks")
           .WithTags("Payments.Admin");
    }

    private static async Task<IResult> InspectPayMongoWebhooksAsync(
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

        string keyMode = secretKey.Trim().StartsWith("sk_live_", StringComparison.OrdinalIgnoreCase) ? "live" : "test";
        bool expectedLivemode = string.Equals(keyMode, "live", StringComparison.OrdinalIgnoreCase);

        string scheme = httpContext.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? httpContext.Request.Scheme;
        string host = httpContext.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? httpContext.Request.Host.Value ?? "localhost";
        string targetWebhookUrl = $"{scheme}://{host}/api/v1/webhooks/paymongo";

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.paymongo.com/v1/");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:")));
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var listResponse = await client.GetAsync("webhooks", cancellationToken).ConfigureAwait(false);
            listResponse.EnsureSuccessStatusCode();
            string listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            using var doc = System.Text.Json.JsonDocument.Parse(listJson);
            var webhooks = new List<object>();

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
                    bool livemode = attrs.TryGetProperty("livemode", out var liveProp) && liveProp.ValueKind == System.Text.Json.JsonValueKind.True;

                    var events = new List<string>();
                    if (attrs.TryGetProperty("events", out var eventsProp) && eventsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var ev in eventsProp.EnumerateArray())
                        {
                            if (ev.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(ev.GetString()))
                            {
                                events.Add(ev.GetString()!);
                            }
                        }
                    }

                    string[] missingCanonical = CanonicalWebhookEvents
                        .Where(ce => !events.Contains(ce, StringComparer.OrdinalIgnoreCase))
                        .ToArray();

                    webhooks.Add(new
                    {
                        id,
                        url,
                        status,
                        livemode,
                        events,
                        matches_host = !string.IsNullOrWhiteSpace(url) && url.Contains(host, StringComparison.OrdinalIgnoreCase),
                        mode_matches_key = livemode == expectedLivemode,
                        missing_canonical_events = missingCanonical
                    });
                }
            }

            return Results.Ok(new
            {
                key_mode = keyMode,
                expected_livemode = expectedLivemode,
                target_url = targetWebhookUrl,
                canonical_events = CanonicalWebhookEvents,
                webhooks
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PAYMONGO-WEBHOOK-INSPECT] Failed to inspect PayMongo webhooks.");
            return Results.Problem("An error occurred while contacting PayMongo to inspect webhooks.");
        }
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

                        var existingEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (attrs.TryGetProperty("events", out var eventsProp) && eventsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var ev in eventsProp.EnumerateArray())
                            {
                                if (ev.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(ev.GetString()))
                                {
                                    existingEvents.Add(ev.GetString()!);
                                }
                            }
                        }

                        string[] missingEvents = CanonicalWebhookEvents
                            .Where(ce => !existingEvents.Contains(ce))
                            .ToArray();

                        if (missingEvents.Length > 0 || !string.Equals(url, targetWebhookUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            string[] mergedEvents = existingEvents
                                .Concat(CanonicalWebhookEvents)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray();

                            var updatePayload = new
                            {
                                data = new
                                {
                                    attributes = new
                                    {
                                        events = mergedEvents,
                                        url = targetWebhookUrl
                                    }
                                }
                            };

                            using var updateContent = new StringContent(
                                System.Text.Json.JsonSerializer.Serialize(updatePayload),
                                Encoding.UTF8,
                                "application/json");

                            using var updateResp = await client.PutAsync($"webhooks/{id}", updateContent, cancellationToken).ConfigureAwait(false);
                            if (updateResp.IsSuccessStatusCode)
                            {
                                report.Add($"Updated webhook {id} events ({string.Join(", ", mergedEvents)}) and URL ({targetWebhookUrl}).");
                            }
                            else
                            {
                                report.Add($"Failed to update webhook {id} events. Please check PayMongo dashboard.");
                            }
                        }

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
                            report.Add($"Webhook {id} is active and pointing to {targetWebhookUrl}.");
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
                            events = CanonicalWebhookEvents,
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

    private static string? ExtractSignatureHeader(IHeaderDictionary headers)
    {
        if (headers.TryGetValue(SignatureHeaderName, out var values) && !string.IsNullOrWhiteSpace(values))
        {
            return values.ToString();
        }

        foreach (var (key, value) in headers)
        {
            if (string.Equals(key, SignatureHeaderName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext httpContext,
        PayMongoSignatureVerifier signatureVerifier,
        ReceivePayMongoWebhookHandler handler,
        ILogger<PayMongoWebhookEndpoint> logger,
        CancellationToken cancellationToken)
    {
        string path = httpContext.Request.Path.Value ?? "/";
        string? signatureHeader = ExtractSignatureHeader(httpContext.Request.Headers);
        bool hasSig = !string.IsNullOrWhiteSpace(signatureHeader);

        httpContext.Request.EnableBuffering();

        if (httpContext.Request.Body.CanSeek)
        {
            httpContext.Request.Body.Position = 0;
        }

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

        if (httpContext.Request.Body.CanSeek)
        {
            httpContext.Request.Body.Position = 0;
        }

        logger.LogInformation(
            "[PAYMONGO-WEBHOOK-INBOUND] Path={Path} | HasSignature={HasSig} | PayloadBytes={Bytes}",
            path,
            hasSig,
            Encoding.UTF8.GetByteCount(rawBody));

        bool isValidSignature;
        try
        {
            isValidSignature = signatureVerifier.IsValidSignature(signatureHeader, rawBody);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[PAYMONGO-WEBHOOK-REJECTED] Path={Path} | Reason=SignatureVerificationException | HasSig={HasSig}",
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

        if (!isValidSignature)
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


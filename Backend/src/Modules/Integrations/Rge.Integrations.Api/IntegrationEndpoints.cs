using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Integrations.Application.IntegrationStatusProbe;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Application.SaveCredentials;
using Rge.Integrations.Application.TestPayMongo;
using Rge.Integrations.Application.TestSmtp;
using Rge.Integrations.Contracts;

namespace Rge.Integrations.Api;

/// <summary>
/// Minimal API endpoints for platform integrations (INT-001, INT-002, INT-005, INT-006).
/// Supports live TCP/STARTTLS handshakes and verification email dispatch to both recipient and sender.
/// </summary>
public sealed class IntegrationEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // INT-001: POST /api/v1/integrations/configure
        app.MapPost("/api/v1/integrations/configure", async (
            SaveCredentialsRequest request,
            HttpContext httpContext,
            SaveCredentialsHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new SaveCredentialsCommand(request);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Integrations");

        // INT-002: POST /api/v1/integrations/email/test and /api/integrations/email/test
        app.MapPost("/api/v1/integrations/email/test", HandleEmailTestAsync)
           .WithTags("Integrations");

        app.MapPost("/api/integrations/email/test", HandleEmailTestAsync)
           .WithTags("Integrations");

        // INT-005: POST /api/v1/integrations/paymongo/test
        app.MapPost("/api/v1/integrations/paymongo/test", async (
            PayMongoTestRequest request,
            HttpContext httpContext,
            TestPayMongoHandler handler,
            CancellationToken ct) =>
        {
            var cmd = new TestPayMongoCommand(request);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Integrations");

        // INT-006: GET /api/v1/integrations/status
        app.MapGet("/api/v1/integrations/status", async (
            HttpContext httpContext,
            IntegrationStatusProbeHandler handler,
            CancellationToken ct) =>
        {
            var query = new GetIntegrationStatusQuery();
            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Integrations");
    }

    private static async Task<IResult> HandleEmailTestAsync(
        HttpContext httpContext,
        ISmtpProbe smtpProbe,
        TestSmtpHandler testHandler,
        CancellationToken ct)
    {
        string? recipientEmail = null;
        string? mode = null;
        bool sendLive = false;
        bool isContractRequest = false;
        EmailTestRequest? contractRequest = null;

        if (httpContext.Request.ContentLength is > 0)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    recipientEmail = TryGetString(root, "recipient_email")
                                  ?? TryGetString(root, "recipientEmail")
                                  ?? TryGetString(root, "email")
                                  ?? TryGetString(root, "to")
                                  ?? TryGetString(root, "recipient")
                                  ?? TryGetString(root, "Recipient");

                    mode = TryGetString(root, "mode") ?? TryGetString(root, "action");

                    if (root.TryGetProperty("send_live", out var sl) && sl.ValueKind == JsonValueKind.True)
                        sendLive = true;
                    if (root.TryGetProperty("sendLive", out var sl2) && sl2.ValueKind == JsonValueKind.True)
                        sendLive = true;
                    if (root.TryGetProperty("dispatch", out var disp) && disp.ValueKind == JsonValueKind.True)
                        sendLive = true;

                    // If request is specifically an EmailTestRequest with Host or SendTestEmail
                    if (root.TryGetProperty("Host", out _) || root.TryGetProperty("host", out _) ||
                        root.TryGetProperty("SendTestEmail", out _) || root.TryGetProperty("sendTestEmail", out _))
                    {
                        string? host = TryGetString(root, "Host") ?? TryGetString(root, "host");
                        int? port = TryGetInt(root, "Port") ?? TryGetInt(root, "port");
                        string? user = TryGetString(root, "User") ?? TryGetString(root, "user");
                        string? pass = TryGetString(root, "Password") ?? TryGetString(root, "password");
                        bool sendTest = false;
                        if (root.TryGetProperty("SendTestEmail", out var ste) && ste.ValueKind == JsonValueKind.True)
                            sendTest = true;
                        else if (root.TryGetProperty("sendTestEmail", out var ste2) && ste2.ValueKind == JsonValueKind.True)
                            sendTest = true;

                        contractRequest = new EmailTestRequest(host, port, user, pass, sendTest, recipientEmail);
                        isContractRequest = true;
                    }
                }
            }
            catch
            {
                // Fall back to query params
            }
        }

        recipientEmail ??= httpContext.Request.Query["recipient_email"].FirstOrDefault()
                        ?? httpContext.Request.Query["email"].FirstOrDefault()
                        ?? httpContext.Request.Query["to"].FirstOrDefault();

        // If this was an explicit contract probe request without live dispatch requested
        if (isContractRequest && contractRequest is not null && !sendLive && string.IsNullOrWhiteSpace(mode))
        {
            var cmd = new TestSmtpCommand(contractRequest);
            var result = await testHandler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }

        bool shouldDispatchEmail = sendLive
            || !string.IsNullOrWhiteSpace(recipientEmail)
            || string.Equals(mode, "dispatch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "send", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase);

        var settings = smtpProbe.ResolveSettings();

        SmtpExecutionResult execResult = shouldDispatchEmail
            ? await smtpProbe.SendVerificationEmailAsync(recipientEmail, ct).ConfigureAwait(false)
            : await smtpProbe.VerifyHandshakeAsync(ct).ConfigureAwait(false);

        if (!execResult.Success)
        {
            return Results.Json(new
            {
                ok = false,
                success = false,
                status = "error",
                mode = shouldDispatchEmail ? "live_dispatch" : "handshake",
                latency_ms = execResult.LatencyMs,
                latencyMs = execResult.LatencyMs,
                host = settings.Host,
                port = settings.Port,
                sender_email = settings.FromEmail,
                recipient_email = execResult.RecipientEmail,
                message = execResult.Message,
                detail = execResult.ErrorDetail ?? execResult.Message
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new
        {
            ok = true,
            success = true,
            status = "verified",
            mode = shouldDispatchEmail ? "live_dispatch" : "handshake",
            latency_ms = execResult.LatencyMs,
            latencyMs = execResult.LatencyMs,
            host = settings.Host,
            port = settings.Port,
            tls = settings.UseTls,
            sender_email = settings.FromEmail,
            recipient_email = execResult.RecipientEmail ?? settings.DefaultReceiptEmail,
            message = execResult.Message
        });
    }

    private static string? TryGetString(JsonElement obj, string propName)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propName, out var el))
        {
            return el.ValueKind == JsonValueKind.String ? el.GetString()?.Trim() : el.ToString();
        }
        return null;
    }

    private static int? TryGetInt(JsonElement obj, string propName)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propName, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v)) return v;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out int s)) return s;
        }
        return null;
    }
}

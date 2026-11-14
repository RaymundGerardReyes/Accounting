using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Integrations.Application.GetFormStatus;
using Rge.Integrations.Application.SubmitGoogleForm;

namespace Rge.Integrations.Api;

/// <summary>
/// Minimal API endpoints for Google Forms Integration (INT-003, INT-004).
/// Supports single form submissions and batch row backfilling for historical records.
/// </summary>
public sealed class GoogleFormsEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // INT-003: GET /api/v1/integrations/google-forms/status and /api/integrations/google-forms/status
        app.MapGet("/api/v1/integrations/google-forms/status", HandleGetStatusAsync)
           .WithTags("GoogleForms");

        app.MapGet("/api/integrations/google-forms/status", HandleGetStatusAsync)
           .WithTags("GoogleForms");

        // INT-004: POST /api/v1/integrations/google-forms/submit and /api/integrations/google-forms/submit
        app.MapPost("/api/v1/integrations/google-forms/submit", HandleSubmitAsync)
           .WithTags("GoogleForms");

        app.MapPost("/api/integrations/google-forms/submit", HandleSubmitAsync)
           .WithTags("GoogleForms");
    }

    private static async Task<IResult> HandleGetStatusAsync(
        HttpContext httpContext,
        GetFormStatusHandler handler,
        CancellationToken ct)
    {
        var query = new GetGoogleFormsStatusQuery();
        var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
        return result.ToHttpResult(httpContext);
    }

    private static async Task<IResult> HandleSubmitAsync(
        HttpContext httpContext,
        SubmitGoogleFormHandler handler,
        CancellationToken ct)
    {
        string? secretHeader = httpContext.Request.Headers["X-Google-Forms-Secret"].FirstOrDefault()
                            ?? httpContext.Request.Headers["x-google-forms-secret"].FirstOrDefault()
                            ?? httpContext.Request.Headers["X-Webhook-Secret"].FirstOrDefault()
                            ?? httpContext.Request.Query["secret"].FirstOrDefault();

        JsonDocument? doc = null;
        if (httpContext.Request.ContentLength is > 0 || httpContext.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                doc = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            }
            catch
            {
                // Ignore parse errors here; handle below
            }
        }

        using (doc)
        {
            var root = doc?.RootElement ?? default;

            if (string.IsNullOrWhiteSpace(secretHeader) && root.ValueKind == JsonValueKind.Object)
            {
                secretHeader = TryGetString(root, "secret")
                            ?? TryGetString(root, "google_forms_secret")
                            ?? TryGetString(root, "secret_key")
                            ?? TryGetString(root, "Secret");
            }

            if (!handler.VerifySecret(secretHeader))
            {
                return Results.Problem(
                    title: "Unauthorized",
                    detail: "Missing or invalid X-Google-Forms-Secret header.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    type: "https://rge-accounting.dev/errors/unauthorized",
                    instance: httpContext.Request.Path);
            }

            // Support batch array of rows (e.g., October + November backfill)
            if (root.ValueKind == JsonValueKind.Object &&
                (root.TryGetProperty("rows", out var rowsEl) || root.TryGetProperty("submissions", out rowsEl)) &&
                rowsEl.ValueKind == JsonValueKind.Array)
            {
                var results = new List<SubmitGoogleFormResponse>();
                foreach (var item in rowsEl.EnumerateArray())
                {
                    var input = ParseSingleSubmission(item, defaultSendEmail: false);
                    results.Add(await handler.HandleAsync(input, ct).ConfigureAwait(false));
                }

                return Results.Ok(new
                {
                    ok = true,
                    synced_count = results.Count,
                    orders = results
                });
            }

            var singleInput = ParseSingleSubmission(root, defaultSendEmail: true);
            var response = await handler.HandleAsync(singleInput, ct).ConfigureAwait(false);

            return Results.Json(new
            {
                ok = response.Ok,
                status = response.Status,
                message = "Google Form order created and checkout session initiated.",
                order_id = response.OrderId,
                order_number = response.OrderNumber,
                orderId = response.OrderId,
                orderNumber = response.OrderNumber,
                response_id = response.ResponseId,
                responseId = response.ResponseId,
                checkout_url = $"/checkout?order_id={Uri.EscapeDataString(response.OrderId)}",
                checkoutUrl = $"/checkout?order_id={Uri.EscapeDataString(response.OrderId)}",
                created_new = response.CreatedNew,
                email_dispatched = response.EmailDispatched,
                email_message = response.EmailMessage,
                created_at = response.CreatedAt
            }, statusCode: response.CreatedNew ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
    }

    private static SubmitGoogleFormInput ParseSingleSubmission(JsonElement el, bool defaultSendEmail)
    {
        string? responseId = TryGetString(el, "response_id")
                          ?? TryGetString(el, "google_form_response_id")
                          ?? TryGetString(el, "responseId")
                          ?? TryGetString(el, "id");

        string? customerName = TryGetString(el, "customer_name")
                            ?? TryGetString(el, "customerName")
                            ?? TryGetString(el, "full_name")
                            ?? TryGetString(el, "name");

        string? customerEmail = TryGetString(el, "customer_email")
                             ?? TryGetString(el, "customerEmail")
                             ?? TryGetString(el, "email");

        string? customerPhone = TryGetString(el, "customer_phone")
                             ?? TryGetString(el, "customerPhone")
                             ?? TryGetString(el, "phone");

        string? productCode = TryGetString(el, "product_code")
                           ?? TryGetString(el, "reference_code")
                           ?? TryGetString(el, "productCode")
                           ?? TryGetString(el, "sku")
                           ?? TryGetString(el, "item_reference");

        string? productTitle = TryGetString(el, "product_title")
                            ?? TryGetString(el, "title")
                            ?? TryGetString(el, "productTitle")
                            ?? TryGetString(el, "item")
                            ?? TryGetString(el, "book_title");

        int quantity = TryGetInt(el, "quantity") ?? 1;
        long? amountMinor = TryGetLong(el, "amount_minor") ?? TryGetLong(el, "amountMinor");
        decimal? amountMajor = TryGetDecimal(el, "amount") ?? TryGetDecimal(el, "total_amount");
        string? currency = TryGetString(el, "currency") ?? "PHP";
        string? status = TryGetString(el, "status") ?? "pending";
        string? timestamp = TryGetString(el, "timestamp")
                         ?? TryGetString(el, "submitted_at")
                         ?? TryGetString(el, "created_at");

        bool sendInitialEmail = defaultSendEmail;
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("send_initial_email", out var sie) &&
                (sie.ValueKind == JsonValueKind.True || sie.ValueKind == JsonValueKind.False))
            {
                sendInitialEmail = sie.GetBoolean();
            }
            else if (el.TryGetProperty("sendInitialEmail", out var sie2) &&
                     (sie2.ValueKind == JsonValueKind.True || sie2.ValueKind == JsonValueKind.False))
            {
                sendInitialEmail = sie2.GetBoolean();
            }
        }

        return new SubmitGoogleFormInput(
            ResponseId: responseId,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            CustomerPhone: customerPhone,
            ProductCode: productCode,
            ProductTitle: productTitle,
            Quantity: quantity,
            AmountMinor: amountMinor,
            AmountMajor: amountMajor,
            Currency: currency,
            Status: status,
            Timestamp: timestamp,
            SendInitialEmail: sendInitialEmail);
    }

    private static string? TryGetString(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var prop))
        {
            return prop.ValueKind == JsonValueKind.String ? prop.GetString()?.Trim() : prop.ToString().Trim('"', ' ');
        }
        return null;
    }

    private static int? TryGetInt(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int v)) return v;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int s)) return s;
        }
        return null;
    }

    private static long? TryGetLong(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out long v)) return v;
            if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out long s)) return s;
        }
        return null;
    }

    private static decimal? TryGetDecimal(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out decimal v)) return v;
            if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out decimal s)) return s;
        }
        return null;
    }
}

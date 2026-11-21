using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

[SuppressMessage("Performance", "CA1873:Avoid unnecessary evaluation")]
public sealed class AppsScriptKotobeeSheetStatusWriter : IKotobeeSheetStatusWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AppsScriptKotobeeSheetStatusWriter> _logger;

    public AppsScriptKotobeeSheetStatusWriter(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<AppsScriptKotobeeSheetStatusWriter>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? NullLogger<AppsScriptKotobeeSheetStatusWriter>.Instance;
    }

    public async Task<KotobeeSheetWriteBackResult> WriteActivationStatusAsync(
        KotobeeSheetStatusWriteBackRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Reuses existing env vars (zero hardcoded URLs or secrets!)
        var webhookUrl = _config["KOTOBEE_SHEET_WRITEBACK_URL"]
                      ?? _config["GOOGLE_SHEETS_WEBHOOK_URL"];
        var secret = _config["KOTOBEE_SHEET_SECRET"]
                  ?? _config["GOOGLE_FORMS_SECRET"];

        if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning(
                "Skipped Google Sheet write-back for Order {OrderId}: webhook URL or secret not configured.",
                request.OrderId);
            return new KotobeeSheetWriteBackResult(false, null, null, "Write-back webhook not configured.");
        }

        // Build a deterministic canonical string and sign it with HMAC-SHA256
        var canonical = $"{request.OrderId}|{request.BuyerEmail}|{request.PaymentStatus}|{request.KotobeeStatus}|{request.UpdatedAtUtc}";
        var signature = ComputeHmacSha256Hex(canonical, secret);

        var bodyPayload = new
        {
            orderId = request.OrderId,
            buyerEmail = request.BuyerEmail,
            productHint = request.ProductHint,
            preferredTabName = request.PreferredTabName,
            paymentStatus = request.PaymentStatus,
            kotobeeStatus = request.KotobeeStatus,
            activatedBookIds = request.ActivatedBookIds,
            updatedAtUtc = request.UpdatedAtUtc,
            notes = request.Notes,
            canonicalSignedText = canonical,
            signature
        };

        var json = JsonSerializer.Serialize(bodyPayload, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            // Note: Google Apps Script Web Apps reply with a 302 redirect on POST; HttpClient follows it automatically.
            using var response = await _httpClient.PostAsync(webhookUrl, content, ct).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Google Sheet write-back for Order {OrderId} returned HTTP {StatusCode}.",
                    request.OrderId, (int)response.StatusCode);
                return new KotobeeSheetWriteBackResult(false, null, null, $"HTTP {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
                  || (root.TryGetProperty("success", out var sProp) && sProp.GetBoolean());

            var tab = root.TryGetProperty("updatedTabName", out var tabProp) ? tabProp.GetString()
                    : root.TryGetProperty("tabName", out var tProp) ? tProp.GetString()
                    : root.TryGetProperty("sheetName", out var snProp) ? snProp.GetString() : null;

            int? row = root.TryGetProperty("updatedRowNumber", out var rowProp) && rowProp.TryGetInt32(out var r1) ? r1
                     : root.TryGetProperty("rowNumber", out var rnProp) && rnProp.TryGetInt32(out var r2) ? r2
                     : root.TryGetProperty("row_updated", out var ruProp) && ruProp.TryGetInt32(out var r3) ? r3 : null;

            var err = root.TryGetProperty("error", out var errProp) ? errProp.GetString()
                    : root.TryGetProperty("message", out var mProp) ? mProp.GetString() : null;

            if (ok)
            {
                _logger.LogInformation(
                    "Updated Google Sheet tab '{Tab}' row {Row} for Order {OrderId} with KotobeeStatus={Status}.",
                    tab, row, request.OrderId, request.KotobeeStatus);
            }
            else
            {
                _logger.LogWarning(
                    "Google Sheet write-back rejected for Order {OrderId}: {Error}",
                    request.OrderId, err);
            }

            return new KotobeeSheetWriteBackResult(ok, tab, row, err);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fault isolation: Sheet write-back error must NEVER undo a successful Kotobee grant!
            _logger.LogError(ex, "Google Sheet write-back failed for Order {OrderId}.", request.OrderId);
            return new KotobeeSheetWriteBackResult(false, null, null, ex.Message);
        }
    }

    public static string ComputeHmacSha256Hex(string message, string secret)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(secret);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
    }
}

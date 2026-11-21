using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Api;

public sealed class KotobeeDynamicSheetEndpoints : IEndpointModule
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // 1. HMAC-verified Google Sheet push webhook
        var pushHandler = async (
            HttpContext context,
            IOptionsSnapshot<KotobeeOptions> optionsSnapshot,
            IntelligentSheetSchemaAdapter adapter,
            IKotobeeSheetSnapshotStore snapshotStore,
            ITamperAlertSink tamperSink,
            ILogger<KotobeeDynamicSheetEndpoints> logger,
            CancellationToken ct) =>
        {
            var opts = optionsSnapshot.Value;
            var secret = opts.SheetWebhookSecret;

            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            context.Request.Body.Position = 0;

            // Verify HMAC-SHA256 signature if secret is configured
            if (!string.IsNullOrWhiteSpace(secret))
            {
                var signatureHeader = context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault()
                                   ?? context.Request.Headers["X-Kotobee-Signature"].FirstOrDefault();

                if (string.IsNullOrWhiteSpace(signatureHeader) || !VerifyHmacSha256(body, signatureHeader, secret))
                {
                    logger.LogWarning("Invalid or missing HMAC signature on Kotobee Sheet push webhook.");
                    await tamperSink.RecordTamperAttemptAsync(new TamperAlertEvent(
                        Operation: "UNAUTHORIZED_SHEET_PUSH",
                        TableName: "kotobee_sheet_catalog_snapshots",
                        RecordId: "N/A",
                        ActorId: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        AuditReason: "Invalid HMAC-SHA256 webhook signature",
                        CorrelationId: context.TraceIdentifier,
                        Details: "Rejected Google Sheet push due to HMAC verification failure."
                    ), ct).ConfigureAwait(false);

                    return Results.Unauthorized();
                }
            }

            AppsScriptWorkbookPayload? workbook;
            try
            {
                workbook = JsonSerializer.Deserialize<AppsScriptWorkbookPayload>(
                    body,
                    JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize AppsScriptWorkbookPayload from request body.");
                return Results.BadRequest(new { error = "Invalid workbook JSON payload format." });
            }

            if (workbook is null || workbook.Sheets.Count == 0)
            {
                return Results.BadRequest(new { error = "Workbook payload is empty or contains no sheets." });
            }

            AdaptedWorkbookResult adaptedResult;
            try
            {
                adaptedResult = adapter.AdaptWorkbook(workbook, opts.PublishingMode, opts.DefaultLibraryId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sheet schema adaptation failed.");
                return Results.UnprocessableEntity(new { error = ex.Message });
            }

            // Anti-mass-wipe guard check
            var previous = await snapshotStore.GetLatestCurrentSnapshotAsync(ct).ConfigureAwait(false);
            try
            {
                DynamicSheetKotobeeCatalogProvider.ValidateMassWipeGuard(
                    adaptedResult.Rules.Count,
                    previous?.ActiveRowCount);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Mass wipe guard triggered during sheet sync.");
                await tamperSink.RecordTamperAttemptAsync(new TamperAlertEvent(
                    Operation: "BLOCKED_MASS_WIPE",
                    TableName: "kotobee_sheet_catalog_snapshots",
                    RecordId: workbook.SpreadsheetId,
                    ActorId: "sheet-webhook",
                    AuditReason: ex.Message,
                    CorrelationId: context.TraceIdentifier,
                    Details: $"Incoming rows: {adaptedResult.Rules.Count}, previous: {previous?.ActiveRowCount}"
                ), ct).ConfigureAwait(false);

                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }

            // Compute payload SHA-256 hash
            var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

            var mappings = DynamicSheetKotobeeCatalogProvider.RulesToProductMappings(
                adaptedResult.Rules, opts.DefaultLibraryId);
            var mappingsJson = JsonSerializer.Serialize(mappings);

            var snapshot = new KotobeeSheetCatalogSnapshot(
                Id: Guid.NewGuid(),
                SpreadsheetId: workbook.SpreadsheetId,
                SheetRange: workbook.Title,
                ContentSha256: contentHash,
                ActiveRowCount: adaptedResult.Rules.Count,
                MappingsJson: mappingsJson,
                SyncedBy: "google-apps-script-webhook",
                SyncedAtUtc: DateTimeOffset.UtcNow,
                IsCurrent: true
            );

            await snapshotStore.SaveSnapshotAsync(snapshot, ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                snapshotId = snapshot.Id,
                activeRowCount = snapshot.ActiveRowCount,
                contentHash = snapshot.ContentSha256,
                detectedSchemaByTab = adaptedResult.DetectedSchemaByTab,
                warnings = adaptedResult.Warnings
            });
        };

        app.MapPost("/api/integrations/kotobee/sheet/push", pushHandler).WithTags("KotobeeSheet");
        app.MapPost("/api/v1/integrations/kotobee/sheet/push", pushHandler).WithTags("KotobeeSheet");

        // 2. Active Sheet Schema and mappings viewer
        var schemaHandler = async (
            IKotobeeSheetSnapshotStore snapshotStore,
            IOptionsSnapshot<KotobeeOptions> optionsSnapshot,
            CancellationToken ct) =>
        {
            var current = await snapshotStore.GetLatestCurrentSnapshotAsync(ct).ConfigureAwait(false);
            if (current is null)
            {
                var staticMappings = optionsSnapshot.Value.ProductMappings;
                return Results.Ok(new
                {
                    source = "appsettings.json (static fallback)",
                    activeRowCount = staticMappings.Values.Sum(l => l.Count),
                    mappings = staticMappings
                });
            }

            var mappings = JsonSerializer.Deserialize<Dictionary<string, List<KotobeeProductBookMapping>>>(
                current.MappingsJson,
                JsonOptions);

            return Results.Ok(new
            {
                source = "PostgreSQL LKG Snapshot",
                snapshotId = current.Id,
                spreadsheetId = current.SpreadsheetId,
                sheetRange = current.SheetRange,
                contentSha256 = current.ContentSha256,
                activeRowCount = current.ActiveRowCount,
                syncedAtUtc = current.SyncedAtUtc,
                mappings
            });
        };

        app.MapGet("/api/integrations/kotobee/sheet/schema", schemaHandler).WithTags("KotobeeSheet");
        app.MapGet("/api/v1/integrations/kotobee/sheet/schema", schemaHandler).WithTags("KotobeeSheet");

        // 3. Historical snapshots list
        var snapshotsHandler = async (
            int? limit,
            IKotobeeSheetSnapshotStore snapshotStore,
            CancellationToken ct) =>
        {
            int safeLimit = Math.Clamp(limit ?? 20, 1, 100);
            var snapshots = await snapshotStore.GetAllSnapshotsAsync(safeLimit, ct).ConfigureAwait(false);
            return Results.Ok(snapshots.Select(s => new
            {
                s.Id,
                s.SpreadsheetId,
                s.SheetRange,
                s.ContentSha256,
                s.ActiveRowCount,
                s.SyncedBy,
                s.SyncedAtUtc,
                s.IsCurrent
            }));
        };

        app.MapGet("/api/integrations/kotobee/sheet/snapshots", snapshotsHandler).WithTags("KotobeeSheet");
        app.MapGet("/api/v1/integrations/kotobee/sheet/snapshots", snapshotsHandler).WithTags("KotobeeSheet");
    }

    private static bool VerifyHmacSha256(string payload, string signatureHeader, string secret)
    {
        var cleanSig = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeader[7..]
            : signatureHeader;

        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(payloadBytes);
        string expectedHex = Convert.ToHexString(hash);

        try
        {
            byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
            byte[] actualBytes = Encoding.UTF8.GetBytes(cleanSig);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch
        {
            return false;
        }
    }
}

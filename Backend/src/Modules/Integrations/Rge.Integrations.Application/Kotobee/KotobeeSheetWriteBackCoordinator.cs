using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Application.Kotobee;

[SuppressMessage("Performance", "CA1873:Avoid unnecessary evaluation")]
public sealed class KotobeeSheetWriteBackCoordinator
{
    private readonly IKotobeeEntitlementRepository _entitlementRepo;
    private readonly IKotobeeSheetStatusWriter _sheetWriter;
    private readonly IKotobeeSheetWriteBackTracker _tracker;
    private readonly ILogger<KotobeeSheetWriteBackCoordinator> _logger;

    public KotobeeSheetWriteBackCoordinator(
        IKotobeeEntitlementRepository entitlementRepo,
        IKotobeeSheetStatusWriter sheetWriter,
        IKotobeeSheetWriteBackTracker tracker,
        ILogger<KotobeeSheetWriteBackCoordinator>? logger = null)
    {
        _entitlementRepo = entitlementRepo ?? throw new ArgumentNullException(nameof(entitlementRepo));
        _sheetWriter = sheetWriter ?? throw new ArgumentNullException(nameof(sheetWriter));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _logger = logger ?? NullLogger<KotobeeSheetWriteBackCoordinator>.Instance;
    }

    /// <summary>
    /// Summarizes all Kotobee activation results for a PAID order and writes them back
    /// to the purchaser's exact row in Google Sheets (matching dynamically across any tab/columns).
    /// </summary>
    public async Task<KotobeeSheetWriteBackResult> SyncPaidFulfillmentOutcomeAsync(
        string orderId,
        string buyerEmail,
        string? productHint,
        AuditExecutionContext auditContext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(buyerEmail);
        ArgumentNullException.ThrowIfNull(auditContext);
        ct.ThrowIfCancellationRequested();

        var records = await _entitlementRepo.GetByOrderIdAsync(orderId, ct).ConfigureAwait(false);
        var activeIds = records
            .Where(r => string.Equals(r.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.KotobeeBookId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var failed = records
            .Where(r => string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        string kotobeeStatus;
        string notes;

        if (activeIds.Count > 0 && failed.Count == 0)
        {
            kotobeeStatus = "ACTIVATED";
            notes = $"Verified full access for {activeIds.Count} ebook(s) in Kotobee.";
        }
        else if (activeIds.Count > 0 && failed.Count > 0)
        {
            kotobeeStatus = "PARTIAL_FAILED";
            notes = $"Activated [{string.Join(", ", activeIds)}]; Failed: " +
                    string.Join("; ", failed.Select(f => $"{f.KotobeeBookId}: {f.LastError}"));
        }
        else if (failed.Count > 0)
        {
            kotobeeStatus = "FAILED";
            notes = string.Join("; ", failed.Select(f => $"{f.KotobeeBookId}: {f.LastError}"));
        }
        else
        {
            kotobeeStatus = "NO_EBOOK_ITEMS";
            notes = "Order confirmed Paid, no Kotobee-mapped ebook items found.";
        }

        var request = new KotobeeSheetStatusWriteBackRequest(
            OrderId: orderId,
            BuyerEmail: buyerEmail,
            ProductHint: productHint ?? records.FirstOrDefault()?.ProductSku,
            PreferredTabName: null, // null = Apps Script dynamically locates the matching tab & row
            PaymentStatus: "PAID",
            KotobeeStatus: kotobeeStatus,
            ActivatedBookIds: string.Join(", ", activeIds),
            UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Notes: notes);

        var result = await _sheetWriter.WriteActivationStatusAsync(request, ct).ConfigureAwait(false);

        if (records.Count > 0)
        {
            await _tracker.RecordSheetWriteBackResultAsync(
                orderId,
                result.Success,
                result.UpdatedTabName,
                result.UpdatedRowNumber,
                result.ErrorMessage,
                auditContext,
                ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Writes FAILED / CANCELED payment status to Google Sheets WITHOUT ever calling Kotobee.
    /// </summary>
    public async Task<KotobeeSheetWriteBackResult> SyncFailedPaymentOutcomeAsync(
        string orderId,
        string buyerEmail,
        string paymentStatus, // e.g., "FAILED" or "CANCELED"
        string failureReason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(buyerEmail);
        ct.ThrowIfCancellationRequested();

        var normalizedStatus = string.IsNullOrWhiteSpace(paymentStatus)
            ? "FAILED"
            : paymentStatus.Trim().ToUpperInvariant();

        var request = new KotobeeSheetStatusWriteBackRequest(
            OrderId: orderId,
            BuyerEmail: buyerEmail,
            ProductHint: null,
            PreferredTabName: null,
            PaymentStatus: normalizedStatus,
            KotobeeStatus: "SKIPPED_UNPAID",
            ActivatedBookIds: string.Empty,
            UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Notes: $"Payment {normalizedStatus}: {failureReason}. Kotobee access not granted.");

        var result = await _sheetWriter.WriteActivationStatusAsync(request, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Sheet write-back for non-paid Order {OrderId} ({PaymentStatus}) finished: Success={Success}.",
            orderId, normalizedStatus, result.Success);

        return result;
    }
}

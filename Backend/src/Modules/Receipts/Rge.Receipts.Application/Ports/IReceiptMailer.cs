using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.Ports;

/// <summary>
/// Dispatches BIR Official Receipts via HTML email with a binary PDF attachment.
/// </summary>
public interface IReceiptMailer
{
    Task<Result> SendReceiptEmailAsync(
        OfficialReceipt receipt,
        string recipientEmail,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default);

    Task SendReceiptAsync(
        OfficialReceipt receipt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return SendReceiptEmailAsync(receipt, receipt.CustomerEmail, [], ct);
    }
}


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
}


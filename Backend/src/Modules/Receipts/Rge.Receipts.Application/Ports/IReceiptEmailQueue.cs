using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.Ports;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "PDF binary payload data transfer object")]
public sealed record ReceiptEmailJob(
    OfficialReceipt Receipt,
    bool ForceResend = false,
    DateTimeOffset EnqueuedAtUtc = default,
    int AttemptCount = 0,
    string? RecipientEmail = null,
    byte[]? PdfBytes = null)
{
    public ReceiptEmailJob(OfficialReceipt receipt, string recipientEmail, byte[] pdfBytes)
        : this(receipt, false, DateTimeOffset.UtcNow, 0, recipientEmail, pdfBytes)
    {
    }
}

public interface IReceiptEmailQueue
{
    ValueTask EnqueueAsync(ReceiptEmailJob job, CancellationToken cancellationToken = default);
    ValueTask EnqueueAsync(OfficialReceipt receipt, bool forceResend = false, CancellationToken ct = default);
    IAsyncEnumerable<ReceiptEmailJob> ReadAllAsync(CancellationToken cancellationToken = default);
}

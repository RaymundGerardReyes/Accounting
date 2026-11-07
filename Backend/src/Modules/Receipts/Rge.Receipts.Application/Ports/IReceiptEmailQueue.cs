using System.Diagnostics.CodeAnalysis;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.Ports;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "PDF binary payload data transfer object")]
public sealed record ReceiptEmailJob(
    OfficialReceipt Receipt,
    string RecipientEmail,
    byte[] PdfBytes);

public interface IReceiptEmailQueue
{
    ValueTask EnqueueAsync(ReceiptEmailJob job, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ReceiptEmailJob> ReadAllAsync(CancellationToken cancellationToken = default);
}

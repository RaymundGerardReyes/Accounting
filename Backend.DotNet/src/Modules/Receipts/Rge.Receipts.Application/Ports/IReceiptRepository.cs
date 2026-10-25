using Rge.Receipts.Domain;

namespace Rge.Receipts.Application.Ports;

/// <summary>
/// Persistence port for saving and retrieving issued BIR Official Receipts.
/// </summary>
public interface IReceiptRepository
{
    Task<long> NextReceiptSequenceAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(OfficialReceipt receipt, CancellationToken cancellationToken = default);
    Task<OfficialReceipt?> GetByPaymentAttemptIdAsync(string paymentAttemptId, CancellationToken cancellationToken = default);
    Task<OfficialReceipt?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
}

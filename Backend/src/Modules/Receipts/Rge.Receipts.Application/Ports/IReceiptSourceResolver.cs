namespace Rge.Receipts.Application.Ports;

public sealed record ReceiptSourceInfo(
    string OrderId,
    string OrderNumber,
    string PaymentAttemptId,
    long AmountMinor,
    string Currency,
    string CustomerName,
    string CustomerEmail,
    string Description,
    string PaymentMethod,
    bool IsPaid);

/// <summary>
/// Cross-module resolver port for looking up payment attempt or order context
/// to dynamically synthesize BIR Official Receipts on demand when paid.
/// </summary>
public interface IReceiptSourceResolver
{
    Task<ReceiptSourceInfo?> ResolveByPaymentAttemptIdAsync(string paymentAttemptId, CancellationToken cancellationToken = default);
    Task<ReceiptSourceInfo?> ResolveByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
}


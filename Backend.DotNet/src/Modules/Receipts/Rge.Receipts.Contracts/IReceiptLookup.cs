using Rge.BuildingBlocks.Application.Messaging;

namespace Rge.Receipts.Contracts;

public sealed record ReceiptSummaryContract(
    string ReceiptId,
    string OrderId,
    string OrderNumber,
    string PaymentAttemptId,
    string OfficialReceiptNumber,
    string TreasuryAcknowledgementNumber,
    long GrossAmountMinor,
    string Currency,
    string AuditDigest,
    DateTimeOffset IssuedAtUtc);

public sealed record ReceiptIssuedIntegrationEvent(
    Guid EventId,
    string OrderId,
    string PaymentAttemptId,
    string OfficialReceiptNumber,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId) : IIntegrationEvent
{
    public string EventType => "receipt.issued";
}

/// <summary>
/// Public cross-module query contract for looking up issued BIR Official Receipts.
/// </summary>
public interface IReceiptLookup
{
    Task<ReceiptSummaryContract?> FindByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<ReceiptSummaryContract?> FindByPaymentAttemptIdAsync(string paymentAttemptId, CancellationToken cancellationToken = default);
}


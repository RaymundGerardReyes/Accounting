using Rge.BuildingBlocks.Application.Messaging;

namespace Rge.Payments.Contracts.Events;

/// <summary>
/// Cross-module integration event published to transactional outbox when a checkout session succeeds.
/// Handled by Receipts (issue OR & acknowledgement), Sheets (sync ledger), and ERP integrations.
/// </summary>
public sealed record OrderPaidIntegrationEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string OrderId,
    string OrderNumber,
    string PaymentAttemptId,
    long AmountMinor,
    string Currency,
    string PaymentMethod,
    string CustomerName,
    string CustomerEmail) : IIntegrationEvent
{
    public const string Type = "order.paid";

    public static OrderPaidIntegrationEvent Create(
        string orderId,
        string orderNumber,
        string paymentAttemptId,
        long amountMinor,
        string currency,
        string paymentMethod,
        string customerName,
        string customerEmail,
        DateTimeOffset paidAtUtc)
    {
        return new OrderPaidIntegrationEvent(
            EventId: Guid.NewGuid(),
            EventType: Type,
            OccurredAtUtc: paidAtUtc,
            CorrelationId: paymentAttemptId,
            OrderId: orderId,
            OrderNumber: orderNumber,
            PaymentAttemptId: paymentAttemptId,
            AmountMinor: amountMinor,
            Currency: currency,
            PaymentMethod: paymentMethod,
            CustomerName: customerName,
            CustomerEmail: customerEmail);
    }
}


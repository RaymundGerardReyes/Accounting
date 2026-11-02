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
        DateTimeOffset paidAtUtc,
        string? metadataJson = null)
    {
        string resolvedName = customerName;
        string resolvedEmail = customerEmail;

        if (!string.IsNullOrWhiteSpace(metadataJson) && (string.IsNullOrWhiteSpace(resolvedName) || string.IsNullOrWhiteSpace(resolvedEmail)))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(metadataJson);
                var root = doc.RootElement;
                if (string.IsNullOrWhiteSpace(resolvedName))
                {
                    if (root.TryGetProperty("customer_name", out var n) ||
                        root.TryGetProperty("customerName", out n) ||
                        root.TryGetProperty("buyer_name", out n) ||
                        root.TryGetProperty("name", out n))
                    {
                        resolvedName = n.GetString() ?? string.Empty;
                    }
                }
                if (string.IsNullOrWhiteSpace(resolvedEmail))
                {
                    if (root.TryGetProperty("customer_email", out var e) ||
                        root.TryGetProperty("customerEmail", out e) ||
                        root.TryGetProperty("buyer_email", out e) ||
                        root.TryGetProperty("email", out e))
                    {
                        resolvedEmail = e.GetString() ?? string.Empty;
                    }
                }
            }
            catch
            {
                // Fallback to provided values
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            resolvedName = "Valued Customer";
        }

        if (string.IsNullOrWhiteSpace(resolvedEmail))
        {
            resolvedEmail = "customer@example.com";
        }

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
            CustomerName: resolvedName,
            CustomerEmail: resolvedEmail);
    }
}


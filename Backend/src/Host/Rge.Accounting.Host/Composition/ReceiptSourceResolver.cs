using System.Text.Json;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Receipts.Application.Ports;

namespace Rge.Accounting.Host.Composition;

/// <summary>
/// Implements cross-module resolution between Payments and Receipts
/// to allow Just-In-Time (JIT) Official Receipt synthesis when queried.
/// </summary>
public sealed class ReceiptSourceResolver : IReceiptSourceResolver
{
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;

    public ReceiptSourceResolver(
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);

        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
    }

    public async Task<ReceiptSourceInfo?> ResolveByPaymentAttemptIdAsync(
        string paymentAttemptId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentAttemptId))
        {
            return null;
        }

        PaymentAttempt? attempt = await _attemptRepository
            .GetByIdAsync(paymentAttemptId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? await _attemptRepository
            .GetByProviderPaymentIdAsync(paymentAttemptId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (attempt is null)
        {
            return null;
        }

        Order? order = await _orderRepository
            .GetByIdAsync(attempt.OrderId, cancellationToken)
            .ConfigureAwait(false);

        return BuildSourceInfo(attempt, order);
    }

    public async Task<ReceiptSourceInfo?> ResolveByOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        Order? order = await _orderRepository
            .GetByIdAsync(orderId.Trim(), cancellationToken)
            .ConfigureAwait(false)
            ?? await _orderRepository
            .GetByOrderNumberAsync(orderId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return null;
        }

        PaymentAttempt? attempt = await _attemptRepository
            .GetLatestByOrderIdAsync(order.Id, cancellationToken)
            .ConfigureAwait(false);

        if (attempt is null)
        {
            return null;
        }

        return BuildSourceInfo(attempt, order);
    }

    private static ReceiptSourceInfo BuildSourceInfo(PaymentAttempt attempt, Order? order)
    {
        string orderId = order?.Id ?? attempt.OrderId;
        string orderNumber = order?.OrderNumber ?? $"ORD-{orderId[..Math.Min(8, orderId.Length)].ToUpperInvariant()}";
        long amountMinor = order?.Amount.AmountMinor ?? attempt.Amount.AmountMinor;
        string currency = order?.Amount.CurrencyCode ?? attempt.Amount.CurrencyCode ?? "PHP";
        string customerName = "Valued Customer";
        string customerEmail = string.Empty;

        if (order is not null && !string.IsNullOrWhiteSpace(order.MetadataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(order.MetadataJson);
                if (doc.RootElement.TryGetProperty("customer_name", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    customerName = n.GetString() ?? customerName;
                }
                if (doc.RootElement.TryGetProperty("customer_email", out var e) && e.ValueKind == JsonValueKind.String)
                {
                    customerEmail = e.GetString() ?? customerEmail;
                }
            }
            catch
            {
                // Fallback to default customer info if json parse fails
            }
        }

        string description = order?.Title ?? $"Payment for {orderNumber}";
        string paymentMethod = attempt.PaymentMethod ?? "paymongo";

        bool isPaid = attempt.Status == PaymentAttemptStatus.Succeeded ||
                      (order is not null && string.Equals(order.Status, OrderStatus.Paid, StringComparison.OrdinalIgnoreCase));

        return new ReceiptSourceInfo(
            OrderId: orderId,
            OrderNumber: orderNumber,
            PaymentAttemptId: attempt.Id,
            AmountMinor: amountMinor,
            Currency: currency,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            Description: description,
            PaymentMethod: paymentMethod,
            IsPaid: isPaid);
    }
}


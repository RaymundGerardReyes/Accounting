using Microsoft.Extensions.Logging;
using Rge.Integrations.Application.Ports;
using Rge.Payments.Contracts.Abstractions;

namespace Rge.Integrations.Infrastructure.Kotobee;

public sealed class PaymentsContractPaidOrderVerifier : IPaidOrderVerifier
{
    private readonly IOrderLookup _orderLookup;
    private readonly ILogger<PaymentsContractPaidOrderVerifier> _logger;

    public PaymentsContractPaidOrderVerifier(
        IOrderLookup orderLookup,
        ILogger<PaymentsContractPaidOrderVerifier> logger)
    {
        _orderLookup = orderLookup ?? throw new ArgumentNullException(nameof(orderLookup));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsOrderConfirmedPaidAsync(string orderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return false;
        }

        var order = await _orderLookup.GetOrderByIdAsync(orderId.Trim(), ct).ConfigureAwait(false);
        if (order is null)
        {
            // Fallback by order number
            order = await _orderLookup.GetOrderByOrderNumberAsync(orderId.Trim(), ct).ConfigureAwait(false);
        }

        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} was not found during Kotobee paid verification.", orderId);
            return false;
        }

        var isPaid = string.Equals(order.Status, "Paid", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(order.Status, "Succeeded", StringComparison.OrdinalIgnoreCase);

        if (!isPaid)
        {
            _logger.LogWarning(
                "Order {OrderId} has status '{Status}', not 'Paid'. Kotobee activation blocked.",
                orderId,
                order.Status);
        }

        return isPaid;
    }
}

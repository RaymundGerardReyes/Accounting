using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Abstractions;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Infrastructure.Persistence.Repositories;

public sealed class OrderLookup : IOrderLookup
{
    private readonly IOrderRepository _repository;

    public OrderLookup(IOrderRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<OrderLookupDto?> GetOrderByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        var order = await _repository.GetByIdAsync(orderId.Trim(), cancellationToken).ConfigureAwait(false);
        return Map(order);
    }

    public async Task<OrderLookupDto?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return null;
        }

        var order = await _repository.GetByOrderNumberAsync(orderNumber.Trim(), cancellationToken).ConfigureAwait(false);
        return Map(order);
    }

    private static OrderLookupDto? Map(Order? order)
    {
        if (order is null)
        {
            return null;
        }

        return new OrderLookupDto(
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            Title: order.Title,
            Category: order.Category,
            ReferenceCode: order.ReferenceCode,
            AmountMinor: order.Amount.AmountMinor,
            Currency: order.Amount.CurrencyCode,
            Status: order.Status,
            MetadataJson: order.MetadataJson,
            CreatedAtUtc: order.CreatedAt);
    }
}

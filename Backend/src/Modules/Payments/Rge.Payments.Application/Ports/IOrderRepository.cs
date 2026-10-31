using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Application.Ports;

/// <summary>
/// Persistence port for querying and updating Order aggregates.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListAsync(string? status = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<long> NextOrderSequenceAsync(string prefix, string yearMonth, CancellationToken cancellationToken = default);
    Task SaveAsync(Order order, CancellationToken cancellationToken = default);
}


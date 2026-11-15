using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Application.Ports;

/// <summary>
/// Persistence port for querying and updating Order aggregates.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListAsync(string? status = null, int limit = 50, string? userId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListAsync(string? customerId, string? status, int limit, CancellationToken cancellationToken = default) => ListAsync(status, limit, customerId, cancellationToken);
    Task<IReadOnlyList<Order>> ListAsync(string? userId, int limit = 250, CancellationToken cancellationToken = default) => ListAsync(null, limit, userId, cancellationToken);
    Task<long> NextOrderSequenceAsync(string prefix, string yearMonth, CancellationToken cancellationToken = default);
    Task SaveAsync(Order order, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default) => SaveAsync(order, cancellationToken);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task DeleteAsync(string orderId, CancellationToken cancellationToken = default);
}

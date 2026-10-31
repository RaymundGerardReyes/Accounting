using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Application.Orders.ListOrders;

public sealed class ListOrdersHandler : IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentAttemptRepository _attemptRepository;

    public ListOrdersHandler(
        IOrderRepository orderRepository,
        IPaymentAttemptRepository attemptRepository)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(attemptRepository);

        _orderRepository = orderRepository;
        _attemptRepository = attemptRepository;
    }

    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int effectiveLimit = Math.Clamp(query.Limit, 1, 100);
        IReadOnlyList<Order> orders = await _orderRepository
            .ListAsync(query.Status, effectiveLimit, cancellationToken)
            .ConfigureAwait(false);

        List<OrderDto> dtos = new(orders.Count);
        foreach (Order order in orders)
        {
            var latestAttempt = await _attemptRepository
                .GetLatestByOrderIdAsync(order.Id, cancellationToken)
                .ConfigureAwait(false);

            dtos.Add(order.ToDto(latestAttempt?.ToDto()));
        }

        return Result.Success<IReadOnlyList<OrderDto>>(dtos);
    }
}


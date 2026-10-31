using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Application.Orders.GetOrder;

public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentAttemptRepository _attemptRepository;

    public GetOrderHandler(
        IOrderRepository orderRepository,
        IPaymentAttemptRepository attemptRepository)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(attemptRepository);

        _orderRepository = orderRepository;
        _attemptRepository = attemptRepository;
    }

    public async Task<Result<OrderDto>> HandleAsync(
        GetOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.OrderId))
        {
            return Result.Failure<OrderDto>(Error.Validation("GetOrder.IdRequired", "Order ID is required."));
        }

        Order? order = await _orderRepository.GetByIdAsync(query.OrderId.Trim(), cancellationToken).ConfigureAwait(false)
            ?? await _orderRepository.GetByOrderNumberAsync(query.OrderId.Trim(), cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<OrderDto>(OrderErrors.NotFound(query.OrderId));
        }

        var attempts = await _attemptRepository.GetByOrderIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
        var attemptDtos = attempts.Select(a => a.ToDto()).ToList();
        var latestAttempt = attemptDtos.FirstOrDefault();

        return Result.Success(order.ToDto(latestAttempt, attemptDtos));
    }
}


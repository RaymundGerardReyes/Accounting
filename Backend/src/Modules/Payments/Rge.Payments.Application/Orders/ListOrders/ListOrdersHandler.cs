using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.Orders.ListOrders;

public sealed class ListOrdersHandler : IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly ICurrentActor _currentActor;

    public ListOrdersHandler(
        IOrderRepository orderRepository,
        IPaymentAttemptRepository attemptRepository,
        ICurrentActor currentActor)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(currentActor);

        _orderRepository = orderRepository;
        _attemptRepository = attemptRepository;
        _currentActor = currentActor;
    }

    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int effectiveLimit = Math.Clamp(query.Limit, 1, 100);

        // System/anonymous actors or unauthenticated callers should never be scoped down to an empty ledger
        bool isAnonymousOrSystemBridge = !_currentActor.IsAuthenticated
            || string.IsNullOrWhiteSpace(_currentActor.UserId)
            || string.Equals(_currentActor.UserId, "anonymous", StringComparison.OrdinalIgnoreCase)
            || _currentActor.UserId == Guid.Empty.ToString()
            || _currentActor.UserId == "00000000-0000-0000-0000-000000000000";

        // Only restrict to a single UserId if explicitly requested in the query
        // OR if the authenticated caller is strictly an isolated Customer / end-user actor.
        var isRestrictedCustomerActor =
            !isAnonymousOrSystemBridge &&
            _currentActor.IsAuthenticated &&
            _currentActor.Roles is { Count: > 0 } &&
            _currentActor.Roles.All(r =>
                string.Equals(r, "Customer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r, "user", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r, "student", StringComparison.OrdinalIgnoreCase));

        string? effectiveUserId = !string.IsNullOrWhiteSpace(query.UserId) &&
                                  !string.Equals(query.UserId, Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase) &&
                                  !string.Equals(query.UserId, "00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase)
            ? query.UserId
            : isRestrictedCustomerActor
                ? _currentActor.UserId
                : null;

        IReadOnlyList<Order> orders = await _orderRepository
            .ListAsync(query.Status, effectiveLimit, effectiveUserId, cancellationToken)
            .ConfigureAwait(false);

        if (orders.Count == 0)
        {
            return Result.Success<IReadOnlyList<OrderDto>>([]);
        }

        string[] orderIds = orders.Select(o => o.Id).ToArray();
        IReadOnlyDictionary<string, PaymentAttempt> latestAttempts = await _attemptRepository
            .GetLatestByOrderIdsAsync(orderIds, cancellationToken)
            .ConfigureAwait(false);

        List<OrderDto> dtos = new(orders.Count);
        foreach (Order order in orders)
        {
            latestAttempts.TryGetValue(order.Id, out PaymentAttempt? latestAttempt);
            dtos.Add(order.ToDto(latestAttempt?.ToDto()));
        }

        return Result.Success<IReadOnlyList<OrderDto>>(dtos);
    }
}

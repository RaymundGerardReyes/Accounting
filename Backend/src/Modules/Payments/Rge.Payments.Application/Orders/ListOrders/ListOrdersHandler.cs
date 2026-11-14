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
    private static readonly HashSet<string> PrivilegedLedgerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "SuperAdmin",
        "SystemAdmin",
        "system_admin",
        "Finance",
        "FinanceAdmin",
        "finance_admin",
        "FinanceManager",
        "Accountant",
        "Treasury",
        "Auditor"
    };

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

        bool isPrivilegedViewer = _currentActor.IsInRole("admin")
            || _currentActor.IsInRole("finance_admin")
            || _currentActor.IsInRole("system_admin")
            || (_currentActor.Roles is not null && _currentActor.Roles.Any(r => PrivilegedLedgerRoles.Contains(r)));

        bool isSystemBridge = string.IsNullOrWhiteSpace(_currentActor.UserId)
            || _currentActor.UserId == Guid.Empty.ToString()
            || _currentActor.UserId == "00000000-0000-0000-0000-000000000000";

        string? tenantUserIdFilter = isPrivilegedViewer || isSystemBridge
            ? null
            : _currentActor.UserId;

        IReadOnlyList<Order> orders = await _orderRepository
            .ListAsync(query.Status, effectiveLimit, tenantUserIdFilter, cancellationToken)
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Pagination;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Application.Orders.ListOrders;

public sealed class ListOrdersHandler :
    IQueryHandler<ListOrdersQuery, IReadOnlyList<OrderDto>>,
    IQueryHandler<ListOrdersQuery, ListOrdersResponse>,
    IHandler<ListOrdersQuery, ListOrdersResponse>
{
    private static bool IsPrivilegedRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var r = role.Trim();
        return r.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("finance", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("treasury", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("account", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("control", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("review", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("audit", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("supervisor", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("maker", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("verifier", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("manager", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("operator", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("system", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("bridge", StringComparison.OrdinalIgnoreCase);
    }

    private readonly IOrderRepository _orders;
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly ICurrentActor _actor;

    public ListOrdersHandler(
        IOrderRepository orders,
        IPaymentAttemptRepository attemptRepository,
        ICurrentActor actor)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _attemptRepository = attemptRepository ?? throw new ArgumentNullException(nameof(attemptRepository));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
    }

    public async Task<Result<IReadOnlyList<OrderDto>>> HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit <= 0 ? 100 : Math.Min(query.Limit, 500);
        var requestedFilter = !string.IsNullOrWhiteSpace(query.CustomerId) ? query.CustomerId : query.UserId;
        var effectiveCustomerId = ResolveEffectiveCustomerFilter(requestedFilter);

        var orders = await _orders
            .ListAsync(query.Status, limit, effectiveCustomerId, cancellationToken)
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

    public async Task<Result<ListOrdersResponse>> HandlePagedAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageReq = query.PageRequest ?? new PageRequest(
            page: 1,
            pageSize: query.Limit,
            status: query.Status,
            search: query.CustomerId ?? query.UserId);

        var repoResult = await _orders.ListPagedAsync(pageReq, cancellationToken).ConfigureAwait(false);

        var dtos = new List<OrderSummaryDto>(repoResult.Page.Items.Count);
        foreach (var order in repoResult.Page.Items)
        {
            var lineItems = new List<OrderLineItemDto>
            {
                new(
                    Id: Guid.NewGuid(),
                    ProductCode: order.ReferenceCode ?? "PROD-01",
                    ProductName: order.Title ?? "Order Item",
                    Quantity: 1,
                    UnitPriceCentavos: order.Amount.AmountMinor,
                    LineTotalCentavos: order.Amount.AmountMinor)
            };

            dtos.Add(new OrderSummaryDto(
                Id: Guid.TryParse(order.Id, out var gid) ? gid : Guid.NewGuid(),
                OrderNumber: order.OrderNumber,
                CustomerName: order.CustomerName ?? "Valued Customer",
                CustomerEmail: order.CustomerEmail ?? string.Empty,
                Category: order.Category,
                Status: order.Status,
                SubtotalCentavos: order.Amount.AmountMinor,
                VatCentavos: 0,
                TotalCentavos: order.Amount.AmountMinor,
                Currency: order.Amount.CurrencyCode,
                CreatedAt: order.CreatedAt,
                UpdatedAt: order.UpdatedAt,
                LineItems: lineItems));
        }

        var response = new ListOrdersResponse(
            Items: dtos,
            Page: repoResult.Page.Page,
            PageSize: repoResult.Page.PageSize,
            TotalCount: repoResult.Page.TotalCount,
            TotalPages: repoResult.Page.TotalPages,
            HasNextPage: repoResult.Page.HasNextPage,
            HasPreviousPage: repoResult.Page.HasPreviousPage,
            Timing: repoResult.Timing);

        return Result.Success(response);
    }

    async Task<Result<ListOrdersResponse>> IHandler<ListOrdersQuery, ListOrdersResponse>.HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken) =>
        await HandlePagedAsync(query, cancellationToken).ConfigureAwait(false);

    private string? ResolveEffectiveCustomerFilter(string? requestedCustomerId)
    {
        // Explicit query filter always wins when provided
        if (!string.IsNullOrWhiteSpace(requestedCustomerId))
        {
            return requestedCustomerId.Trim();
        }

        // Unauthenticated dashboard heartbeat or system actor -> return full authoritative ledger
        if (!_actor.IsAuthenticated ||
            string.IsNullOrWhiteSpace(_actor.UserId) ||
            string.Equals(_actor.UserId, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Corporate OAuth dashboard sessions (e.g. usr_oauth_...) are authenticated internal staff
        if (_actor.UserId.StartsWith("usr_oauth_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var roles = _actor.Roles;
        if (roles == null || roles.Count == 0 || roles.Any(IsPrivilegedRole))
        {
            return null;
        }

        // Non-privileged authenticated actors (e.g. employee_jane, customer, student) only see their own orders
        return _actor.UserId;
    }
}
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.Orders.ListOrders;

public sealed record ListOrdersQuery(
    string? Status = null,
    int Limit = 50,
    string? UserId = null,
    string? CustomerId = null) : IQuery<IReadOnlyList<OrderDto>>;


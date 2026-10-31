using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.Orders.ListOrders;

public sealed record ListOrdersResponse(IReadOnlyList<OrderDto> Orders);


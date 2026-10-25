using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.Orders.GetOrder;

public sealed record GetOrderQuery(string OrderId) : IQuery<OrderDto>;


using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.Orders.CreateOrder;

public sealed record CreateOrderCommand(
    string? Title = null,
    string Category = "product_order",
    string ReferenceCode = "",
    long? AmountMinor = null,
    int Quantity = 1,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    string UserId = "acme_admin") : ICommand<OrderDto>;


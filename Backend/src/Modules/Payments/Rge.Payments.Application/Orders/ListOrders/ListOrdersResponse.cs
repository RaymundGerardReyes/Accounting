using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;

namespace Rge.Payments.Application.Orders.ListOrders;

public sealed record OrderLineItemDto(
    Guid Id,
    string ProductCode,
    string ProductName,
    int Quantity,
    long UnitPriceCentavos,
    long LineTotalCentavos);

public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    string CustomerEmail,
    string Category,
    string Status,
    long SubtotalCentavos,
    long VatCentavos,
    long TotalCentavos,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderLineItemDto> LineItems);

public sealed record ListOrdersResponse(
    IReadOnlyList<OrderSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage,
    [property: JsonIgnore] RepositoryPageTiming? Timing = null)
{
    [JsonIgnore]
    public IReadOnlyList<OrderDto> Orders { get; init; } = Array.Empty<OrderDto>();

    public ListOrdersResponse(IReadOnlyList<OrderDto> orders)
        : this(
            Items: Array.Empty<OrderSummaryDto>(),
            Page: 1,
            PageSize: orders?.Count ?? 0,
            TotalCount: orders?.Count ?? 0,
            TotalPages: 1,
            HasNextPage: false,
            HasPreviousPage: false)
    {
        Orders = orders ?? Array.Empty<OrderDto>();
    }
}

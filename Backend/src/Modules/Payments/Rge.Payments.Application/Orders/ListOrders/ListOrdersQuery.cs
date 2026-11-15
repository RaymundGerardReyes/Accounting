using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Pagination;
using Rge.Payments.Application.Mapping;

namespace Rge.Payments.Application.Orders.ListOrders;

public sealed record ListOrdersQuery(
    string? Status = null,
    int Limit = 50,
    string? UserId = null,
    string? CustomerId = null,
    PageRequest? PageRequest = null) : IQuery<IReadOnlyList<OrderDto>>, IQuery<ListOrdersResponse>
{
    public ListOrdersQuery(PageRequest pageRequest)
        : this(Status: pageRequest?.Status, Limit: pageRequest?.PageSize ?? 50, PageRequest: pageRequest)
    {
    }
}

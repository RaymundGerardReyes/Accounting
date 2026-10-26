using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.PurchaseRequests.Create;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.PurchaseRequests.List;

public sealed record ListPurchaseRequestsQuery(
    string? DepartmentId = null,
    string? Status = null,
    int Limit = 50) : IQuery<IReadOnlyList<PurchaseRequestDto>>;

public sealed class ListHandler : IQueryHandler<ListPurchaseRequestsQuery, IReadOnlyList<PurchaseRequestDto>>
{
    private readonly IPurchaseRequestRepository _repository;

    public ListHandler(IPurchaseRequestRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<PurchaseRequestDto>>> HandleAsync(
        ListPurchaseRequestsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int take = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 200);
        IReadOnlyList<PurchaseRequest> list = await _repository.ListAsync(query.DepartmentId, query.Status, take, cancellationToken).ConfigureAwait(false);

        var dtos = list.Select(CreateHandler.MapToDto).ToList();
        return Result.Success<IReadOnlyList<PurchaseRequestDto>>(dtos);
    }
}

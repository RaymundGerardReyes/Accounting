using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.CostAllocation.Application.Ports;

namespace Rge.CostAllocation.Application.GetCostAllocation;

public sealed record GetCostAllocationQuery : IQuery<CostAllocationSnapshotDto>;

public sealed class GetCostAllocationHandler : IQueryHandler<GetCostAllocationQuery, CostAllocationSnapshotDto>
{
    private readonly ICostAllocationStore _store;

    public GetCostAllocationHandler(ICostAllocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<Result<CostAllocationSnapshotDto>> HandleAsync(
        GetCostAllocationQuery query,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(_store.GetSnapshot()));
    }
}


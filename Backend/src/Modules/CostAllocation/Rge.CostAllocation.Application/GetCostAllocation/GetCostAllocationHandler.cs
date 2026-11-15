using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.CostAllocation.Application.Ports;

namespace Rge.CostAllocation.Application.GetCostAllocation;

public sealed record GetCostAllocationQuery : IQuery<CostAllocationSnapshotDto>;

public sealed class GetCostAllocationHandler : IQueryHandler<GetCostAllocationQuery, CostAllocationSnapshotDto>
{
    private readonly ICostAllocationPostgresStore _store;

    public GetCostAllocationHandler(ICostAllocationPostgresStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<Result<CostAllocationSnapshotDto>> HandleAsync(
        GetCostAllocationQuery query,
        CancellationToken cancellationToken = default)
    {
        return BuildSnapshotAsync(cancellationToken);
    }

    private async Task<Result<CostAllocationSnapshotDto>> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CostAllocationDepartmentRecord> departments =
            await _store.GetActiveDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        var mapped = departments.Select(department => new CostDepartmentDto(
            department.DepartmentId,
            department.DepartmentName,
            department.CostCenterId,
            department.DirectCostCentavos,
            0,
            department.DirectCostCentavos,
            department.BudgetCapCentavos,
            department.BudgetCapCentavos - department.DirectCostCentavos,
            department.HeadcountDriverWeight,
            0,
            0)).ToList();
        return Result.Success(new CostAllocationSnapshotDto(
            mapped, [], [], mapped.Sum(x => x.DirectCostMinor), 0, "PHP"));
    }
}


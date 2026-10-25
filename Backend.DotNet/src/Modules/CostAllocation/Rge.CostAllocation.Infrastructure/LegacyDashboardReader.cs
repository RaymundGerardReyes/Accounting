using Rge.CostAllocation.Application.Ports;

namespace Rge.CostAllocation.Infrastructure;

public sealed class LegacyDashboardReader
{
    private readonly ICostAllocationStore _store;

    public LegacyDashboardReader(ICostAllocationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public object BuildDashboardSummary()
    {
        CostAllocationSnapshotDto snapshot = _store.GetSnapshot();
        return new
        {
            status = "ok",
            currency = snapshot.Currency,
            total_direct_cost_minor = snapshot.TotalDirectCostMinor,
            total_overhead_pool_minor = snapshot.TotalOverheadPoolMinor,
            departments = snapshot.Departments,
            products = snapshot.Products
        };
    }

    public object BuildVarianceAnalysis()
    {
        CostAllocationSnapshotDto snapshot = _store.GetSnapshot();
        return new
        {
            currency = snapshot.Currency,
            variances = snapshot.Departments.Select(static d => new
            {
                department = d.Name,
                budget_limit_minor = d.BudgetLimitMinor,
                actual_total_minor = d.TotalCostMinor,
                variance_minor = d.VarianceMinor,
                is_favorable = d.VarianceMinor >= 0
            }).ToList()
        };
    }
}


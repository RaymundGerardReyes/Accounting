using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.CostAllocation.Application.Ports;
using Rge.CostAllocation.Domain;

namespace Rge.CostAllocation.Infrastructure;

public sealed class JsonConfigurationStore : ICostAllocationStore
{
    private readonly object _lock = new();
    private readonly List<CostDepartment> _departments =
    [
        new("dept_mfg", "Manufacturing", "CC-001", Money.Php(45_000_00), Money.Php(60_000_00), 400, 800, 1200),
        new("dept_asm", "Assembly", "CC-002", Money.Php(30_000_00), Money.Php(45_000_00), 250, 600, 1000),
        new("dept_qa", "QA", "CC-003", Money.Php(15_000_00), Money.Php(25_000_00), 100, 300, 500),
        new("dept_log", "Logistics", "CC-004", Money.Php(20_000_00), Money.Php(30_000_00), 150, 400, 800)
    ];

    private readonly List<CostProductDto> _products =
    [
        new("prod_01", "CPA Reviewer Volume 1", 850_00, 150_00, 500),
        new("prod_02", "Cost Accounting Manual", 950_00, 180_00, 350)
    ];

    private readonly List<CostActivityDto> _activities =
    [
        new("act_01", "Machine Setup & Print Run", 18_000_00, "MACHINE_HRS", "Machine Hours", 900),
        new("act_02", "Quality & Binding Inspection", 12_000_00, "LABOR_HRS", "Labor Hours", 2100)
    ];

    private readonly Dictionary<string, object> _config = new(StringComparer.OrdinalIgnoreCase)
    {
        ["company_name"] = "R.G.E Accounting Firm Inc.",
        ["currency"] = "PHP",
        ["vat_rate_bps"] = 1200
    };

    public CostAllocationSnapshotDto GetSnapshot()
    {
        lock (_lock)
        {
            long totalMachineHours = Math.Max(1, _departments.Sum(d => d.MachineHours));
            Money totalOverheadPool = Money.Php(_activities.Sum(a => a.OverheadPoolMinor));

            List<CostDepartmentDto> deptDtos = [];
            foreach (CostDepartment dept in _departments)
            {
                Money overheadShare = AllocationCalculator.AllocatePoolShare(
                    totalOverheadPool,
                    dept.MachineHours,
                    totalMachineHours);

                Money totalCost = dept.DirectCost + overheadShare;
                Money variance = dept.BudgetLimit - totalCost;

                deptDtos.Add(new CostDepartmentDto(
                    dept.Id,
                    dept.Name,
                    dept.CostCenterCode,
                    dept.DirectCost.AmountMinor,
                    overheadShare.AmountMinor,
                    totalCost.AmountMinor,
                    dept.BudgetLimit.AmountMinor,
                    variance.AmountMinor,
                    dept.MachineHours,
                    dept.LaborHours,
                    dept.OutputVolume));
            }

            return new CostAllocationSnapshotDto(
                Departments: deptDtos,
                Products: _products,
                Activities: _activities,
                TotalDirectCostMinor: deptDtos.Sum(d => d.DirectCostMinor),
                TotalOverheadPoolMinor: totalOverheadPool.AmountMinor,
                Currency: "PHP");
        }
    }

    public CostDepartmentDto? GetDepartmentByName(string name) =>
        GetSnapshot().Departments.FirstOrDefault(
            d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(d.Id, name, StringComparison.OrdinalIgnoreCase));

    public CostDepartmentDto? UpdateDepartmentMetrics(
        string departmentName,
        long directCostMinor,
        long machineHours,
        long laborHours,
        long outputVolume)
    {
        lock (_lock)
        {
            CostDepartment? dept = _departments.FirstOrDefault(
                d => string.Equals(d.Name, departmentName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(d.Id, departmentName, StringComparison.OrdinalIgnoreCase));

            if (dept is null)
            {
                return null;
            }

            dept.UpdateMetrics(Money.Php(directCostMinor), machineHours, laborHours, outputVolume);
        }

        return GetDepartmentByName(departmentName);
    }

    public Dictionary<string, object> GetConfiguration()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>(_config, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveConfiguration(Dictionary<string, object> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_lock)
        {
            foreach (var kvp in config)
            {
                _config[kvp.Key] = kvp.Value;
            }
        }
    }
}


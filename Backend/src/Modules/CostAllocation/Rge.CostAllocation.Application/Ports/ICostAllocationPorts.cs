namespace Rge.CostAllocation.Application.Ports;

public sealed record CostAllocationSnapshotDto(
    IReadOnlyList<CostDepartmentDto> Departments,
    IReadOnlyList<CostProductDto> Products,
    IReadOnlyList<CostActivityDto> Activities,
    long TotalDirectCostMinor,
    long TotalOverheadPoolMinor,
    string Currency);

public sealed record CostDepartmentDto(
    string Id,
    string Name,
    string CostCenterCode,
    long DirectCostMinor,
    long AllocatedOverheadMinor,
    long TotalCostMinor,
    long BudgetLimitMinor,
    long VarianceMinor,
    long MachineHours,
    long LaborHours,
    long OutputVolume);

public sealed record CostProductDto(
    string Id,
    string Name,
    long UnitDirectCostMinor,
    long AllocatedOverheadMinor,
    long UnitsProduced);

public sealed record CostActivityDto(
    string Id,
    string ActivityName,
    long OverheadPoolMinor,
    string DriverCode,
    string UnitName,
    long TotalDriverUnits);

public interface ICostAllocationStore
{
    CostAllocationSnapshotDto GetSnapshot();
    CostDepartmentDto? GetDepartmentByName(string name);
    CostDepartmentDto? UpdateDepartmentMetrics(
        string departmentName,
        long directCostMinor,
        long machineHours,
        long laborHours,
        long outputVolume);
    Dictionary<string, object> GetConfiguration();
    void SaveConfiguration(Dictionary<string, object> config);
}


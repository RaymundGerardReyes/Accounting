using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.CostAllocation.Domain;

public sealed class CostDepartment : Entity<string>
{
    public string Name { get; private set; }
    public string CostCenterCode { get; private set; }
    public Money DirectCost { get; private set; }
    public Money BudgetLimit { get; private set; }
    public long MachineHours { get; private set; }
    public long LaborHours { get; private set; }
    public long OutputVolume { get; private set; }

    public CostDepartment(
        string id,
        string name,
        string costCenterCode,
        Money directCost,
        Money budgetLimit,
        long machineHours,
        long laborHours,
        long outputVolume) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        Name = Guard.AgainstNullOrWhiteSpace(name);
        CostCenterCode = Guard.AgainstNullOrWhiteSpace(costCenterCode);
        DirectCost = directCost;
        BudgetLimit = budgetLimit;
        MachineHours = Guard.AgainstNegative(machineHours, nameof(machineHours));
        LaborHours = Guard.AgainstNegative(laborHours, nameof(laborHours));
        OutputVolume = Guard.AgainstNegative(outputVolume, nameof(outputVolume));
    }

    public void UpdateMetrics(
        Money directCost,
        long machineHours,
        long laborHours,
        long outputVolume)
    {
        Guard.AgainstNegative(directCost.AmountMinor, nameof(directCost));
        DirectCost = directCost;
        MachineHours = Guard.AgainstNegative(machineHours, nameof(machineHours));
        LaborHours = Guard.AgainstNegative(laborHours, nameof(laborHours));
        OutputVolume = Guard.AgainstNegative(outputVolume, nameof(outputVolume));
    }
}


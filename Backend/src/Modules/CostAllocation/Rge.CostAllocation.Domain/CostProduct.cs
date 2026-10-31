using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.CostAllocation.Domain;

public sealed class CostProduct : Entity<string>
{
    public string Name { get; }
    public Money UnitDirectCost { get; }
    public Money AllocatedOverhead { get; }
    public long UnitsProduced { get; }

    public CostProduct(
        string id,
        string name,
        Money unitDirectCost,
        Money allocatedOverhead,
        long unitsProduced) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        Name = Guard.AgainstNullOrWhiteSpace(name);
        UnitDirectCost = unitDirectCost;
        AllocatedOverhead = allocatedOverhead;
        UnitsProduced = Guard.AgainstNonPositive(unitsProduced, nameof(unitsProduced));
    }
}


using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.CostAllocation.Domain;

public sealed class CostDriver : ValueObject
{
    public string DriverCode { get; }
    public string UnitName { get; }
    public long TotalDriverUnits { get; }

    public CostDriver(string driverCode, string unitName, long totalDriverUnits)
    {
        DriverCode = Guard.AgainstNullOrWhiteSpace(driverCode);
        UnitName = Guard.AgainstNullOrWhiteSpace(unitName);
        TotalDriverUnits = Guard.AgainstNonPositive(totalDriverUnits, nameof(totalDriverUnits));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DriverCode;
        yield return UnitName;
        yield return TotalDriverUnits;
    }
}


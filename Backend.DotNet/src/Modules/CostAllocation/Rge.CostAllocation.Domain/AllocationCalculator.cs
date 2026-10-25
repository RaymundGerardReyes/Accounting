using Rge.BuildingBlocks.SharedKernel.Money;

namespace Rge.CostAllocation.Domain;

/// <summary>
/// Pure integer-only Activity-Based Costing (ABC) allocation engine.
/// </summary>
public static class AllocationCalculator
{
    public static Money AllocatePoolShare(
        Money totalOverheadPool,
        long consumedDriverUnits,
        long totalDriverUnits)
    {
        if (consumedDriverUnits <= 0 || totalDriverUnits <= 0 || totalOverheadPool.AmountMinor <= 0)
        {
            return Money.Zero(totalOverheadPool.CurrencyCode);
        }

        long allocatedMinor = Rounding.DivideHalfUp(
            checked(totalOverheadPool.AmountMinor * consumedDriverUnits),
            totalDriverUnits);

        return new Money(allocatedMinor, totalOverheadPool.CurrencyCode);
    }
}


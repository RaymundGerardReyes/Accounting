using Rge.BuildingBlocks.SharedKernel.Money;

namespace Rge.CostAllocation.Domain;

/// <summary>
/// Pure integer-only Activity-Based Costing (ABC) allocation engine.
/// </summary>
public static class AllocationCalculator
{
    public static IReadOnlyList<Money> AllocatePoolShares(
        Money totalOverheadPool,
        IReadOnlyList<int> weightBasisPoints)
    {
        ArgumentNullException.ThrowIfNull(weightBasisPoints);

        if (totalOverheadPool.AmountMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalOverheadPool), "Cost pool cannot be negative.");
        }

        if (weightBasisPoints.Count == 0 || weightBasisPoints.Any(weight => weight <= 0))
        {
            throw new ArgumentException("At least one positive driver weight is required.", nameof(weightBasisPoints));
        }

        int totalBasisPoints = weightBasisPoints.Sum();
        if (totalBasisPoints != 10_000)
        {
            throw new ArgumentException("Driver weights must total exactly 10,000 basis points.", nameof(weightBasisPoints));
        }

        var shares = weightBasisPoints
            .Select(weight => new Money(
                Rounding.DivideHalfUp(
                    checked(totalOverheadPool.AmountMinor * weight),
                    10_000),
                totalOverheadPool.CurrencyCode))
            .ToList();

        long allocatedMinor = shares.Sum(share => share.AmountMinor);
        long remainder = checked(totalOverheadPool.AmountMinor - allocatedMinor);
        if (remainder != 0)
        {
            int largestSlice = weightBasisPoints
                .Select((weight, index) => (weight, index))
                .OrderByDescending(item => item.weight)
                .ThenBy(item => item.index)
                .First()
                .index;
            shares[largestSlice] = new Money(
                checked(shares[largestSlice].AmountMinor + remainder),
                totalOverheadPool.CurrencyCode);
        }

        return shares;
    }

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


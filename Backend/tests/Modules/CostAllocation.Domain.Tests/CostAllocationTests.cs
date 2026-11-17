using Rge.BuildingBlocks.SharedKernel.Money;
using Xunit;

namespace Rge.CostAllocation.Domain.Tests;

public sealed class CostAllocationTests
{
    [Fact]
    public void AllocatePoolShares_ConservesOddCentavoPool()
    {
        IReadOnlyList<Money> shares = AllocationCalculator.AllocatePoolShares(
            Money.Php(1_000_001),
            [3_333, 3_333, 3_334]);

        Assert.Equal(1_000_001, shares.Sum(share => share.AmountMinor));
        Assert.Equal(3, shares.Count);
        Assert.True(shares[2].AmountMinor >= shares[0].AmountMinor);
    }
}

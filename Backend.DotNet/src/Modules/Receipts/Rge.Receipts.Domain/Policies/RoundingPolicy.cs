using Rge.BuildingBlocks.SharedKernel.Money;

namespace Rge.Receipts.Domain.Policies;

/// <summary>
/// Domain rounding policy enforcing symmetric integer half-up arithmetic for BIR tax compliance.
/// </summary>
public static class RoundingPolicy
{
    public static long DivideHalfUp(long numerator, long denominator) =>
        Rounding.DivideHalfUp(numerator, denominator);
}

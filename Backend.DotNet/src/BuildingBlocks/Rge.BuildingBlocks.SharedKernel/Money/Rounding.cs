namespace Rge.BuildingBlocks.SharedKernel.Money;

/// <summary>
/// Provides integer-only half-up rounding arithmetic to satisfy the Zero-Float Invariant.
/// </summary>
public static class Rounding
{
    /// <summary>
    /// Divides <paramref name="numerator"/> by <paramref name="denominator"/> using
    /// symmetric half-up integer arithmetic (no floating-point conversion).
    /// Example: DivideHalfUp(125, 100) == 1; DivideHalfUp(150, 100) == 2.
    /// </summary>
    public static long DivideHalfUp(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
        }

        if (numerator == 0)
        {
            return 0;
        }

        long sign = numerator < 0 ? -1 : 1;
        long absNumerator = Math.Abs(numerator);
        long quotient = (absNumerator + (denominator / 2)) / denominator;

        return sign * quotient;
    }
}


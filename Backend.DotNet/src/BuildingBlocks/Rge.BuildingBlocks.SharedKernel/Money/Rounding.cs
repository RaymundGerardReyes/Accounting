namespace Rge.BuildingBlocks.SharedKernel.Money;

/// <summary>
/// Provides overflow-safe, integer-only half-up rounding arithmetic
/// to satisfy the Zero-Float Invariant across all denominators (even or odd).
/// </summary>
public static class Rounding
{
    /// <summary>
    /// Divides <paramref name="numerator"/> by <paramref name="denominator"/> using
    /// symmetric half-up integer arithmetic without floating-point conversion or overflow risk.
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

        long sign = numerator < 0 ? -1L : 1L;
        long absNumerator = Math.Abs(numerator);

        long quotient = absNumerator / denominator;
        long remainder = absNumerator % denominator;

        // Equivalent to (remainder * 2 >= denominator) without risking long overflow
        if (remainder >= (denominator - remainder))
        {
            quotient = checked(quotient + 1);
        }

        return checked(sign * quotient);
    }
}

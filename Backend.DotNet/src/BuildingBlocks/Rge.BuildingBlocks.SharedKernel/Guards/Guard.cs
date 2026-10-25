using System.Runtime.CompilerServices;

namespace Rge.BuildingBlocks.SharedKernel.Guards;

/// <summary>
/// Provides fast, zero-dependency guard clauses for domain invariants.
/// </summary>
public static class Guard
{
    public static string AgainstNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", paramName);
        }
        return value.Trim();
    }

    public static long AgainstNegative(
        long value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Amount in minor units cannot be negative.");
        }
        return value;
    }

    public static long AgainstNonPositive(
        long value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be greater than zero.");
        }
        return value;
    }
}


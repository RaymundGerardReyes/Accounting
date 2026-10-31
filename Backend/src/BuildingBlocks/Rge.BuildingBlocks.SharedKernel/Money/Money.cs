namespace Rge.BuildingBlocks.SharedKernel.Money;

/// <summary>
/// Authoritative monetary value object represented strictly in integer minor units
/// (e.g., Philippine centavos where PHP 1.00 = 100 centavos).
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public long AmountMinor { get; }
    public string CurrencyCode { get; }

    public Money(long amountMinor, string currencyCode = "PHP")
    {
        CurrencyCode = Currency.FromCode(currencyCode).Code;
        AmountMinor = amountMinor;
    }

    public static Money Zero(string currencyCode = "PHP") => new(0, currencyCode);

    public static Money Php(long centavos) => new(centavos, "PHP");

    public bool IsZero => AmountMinor == 0;
    public bool IsPositive => AmountMinor > 0;
    public bool IsNegative => AmountMinor < 0;

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(checked(left.AmountMinor + right.AmountMinor), left.CurrencyCode);
    }

    public static Money Add(Money left, Money right) => left + right;

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(checked(left.AmountMinor - right.AmountMinor), left.CurrencyCode);
    }

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money operator *(Money money, int quantity) =>
        new(checked(money.AmountMinor * quantity), money.CurrencyCode);

    public static Money Multiply(Money money, int quantity) => money * quantity;

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return AmountMinor.CompareTo(other.AmountMinor);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Currency mismatch: cannot combine '{left.CurrencyCode}' and '{right.CurrencyCode}'.");
        }
    }

    /// <summary>
    /// Formats centavos as a human-readable string (e.g., "PHP 1,234.56") using integer division only.
    /// </summary>
    public override string ToString()
    {
        long abs = Math.Abs(AmountMinor);
        long whole = abs / 100;
        long centavos = abs % 100;
        string sign = AmountMinor < 0 ? "-" : string.Empty;
        return $"{CurrencyCode} {sign}{whole:N0}.{centavos:D2}";
    }
}


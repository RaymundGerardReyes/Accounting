namespace Rge.BuildingBlocks.SharedKernel.Money;

/// <summary>
/// Computes Philippine 12% VAT (1200 bps) and Expanded Withholding Tax (EWT / BIR Form 2307)
/// using strictly integer-only half-up arithmetic.
/// </summary>
public static class VatCalculator
{
    public const int DefaultPhilippineVatBps = 1200; // 12.00%
    private const long BasisPointsDivisor = 10_000L;

    /// <summary>
    /// Extracts VATable Sales and 12% VAT from a VAT-inclusive gross amount in centavos.
    /// Formula: VatableMinor = RoundHalfUp(Gross * 10000 / 11200), VatMinor = Gross - VatableMinor.
    /// </summary>
    public static (Money VatableSales, Money VatAmount) ComputeVatInclusive(
        Money grossAmount,
        int vatRateBps = DefaultPhilippineVatBps)
    {
        if (grossAmount.AmountMinor <= 0 || vatRateBps <= 0)
        {
            return (grossAmount, Money.Zero(grossAmount.CurrencyCode));
        }

        long denominator = BasisPointsDivisor + vatRateBps; // 11,200 for 12% VAT
        long vatableMinor = Rounding.DivideHalfUp(
            checked(grossAmount.AmountMinor * BasisPointsDivisor),
            denominator);

        long vatMinor = grossAmount.AmountMinor - vatableMinor;

        return (
            new Money(vatableMinor, grossAmount.CurrencyCode),
            new Money(vatMinor, grossAmount.CurrencyCode)
        );
    }

    /// <summary>
    /// Computes Expanded Withholding Tax (EWT / BIR 2307) from net tax base and basis points (rate_bps).
    /// </summary>
    public static Money ComputeWithholdingTax(Money taxBase, int rateBps)
    {
        if (rateBps <= 0 || taxBase.AmountMinor <= 0)
        {
            return Money.Zero(taxBase.CurrencyCode);
        }

        long taxMinor = Rounding.DivideHalfUp(
            checked(taxBase.AmountMinor * rateBps),
            BasisPointsDivisor);

        return new Money(taxMinor, taxBase.CurrencyCode);
    }
}


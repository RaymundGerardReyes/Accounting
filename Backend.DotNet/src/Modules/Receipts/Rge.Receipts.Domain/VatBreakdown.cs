using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Receipts.Domain;

/// <summary>
/// Immutable Philippine BIR VAT breakdown in integer centavos (VATable, 12% VAT, Exempt, Zero-Rated).
/// </summary>
public sealed class VatBreakdown : ValueObject
{
    public Money VatableSales { get; }
    public Money VatAmount { get; }
    public Money VatExemptSales { get; }
    public Money ZeroRatedSales { get; }
    public Money GrossAmount { get; }

    public VatBreakdown(
        Money vatableSales,
        Money vatAmount,
        Money vatExemptSales,
        Money zeroRatedSales)
    {
        VatableSales = vatableSales;
        VatAmount = vatAmount;
        VatExemptSales = vatExemptSales;
        ZeroRatedSales = zeroRatedSales;
        GrossAmount = vatableSales + vatAmount + vatExemptSales + zeroRatedSales;
    }

    public static VatBreakdown FromVatInclusiveGross(
        Money grossAmount,
        int vatRateBps = VatCalculator.DefaultPhilippineVatBps)
    {
        (Money vatable, Money vat) = VatCalculator.ComputeVatInclusive(grossAmount, vatRateBps);
        Money zero = Money.Zero(grossAmount.CurrencyCode);
        return new VatBreakdown(vatable, vat, zero, zero);
    }

    public static VatBreakdown CreateVatExempt(Money grossAmount)
    {
        Money zero = Money.Zero(grossAmount.CurrencyCode);
        return new VatBreakdown(zero, zero, grossAmount, zero);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return VatableSales;
        yield return VatAmount;
        yield return VatExemptSales;
        yield return ZeroRatedSales;
        yield return GrossAmount;
    }
}

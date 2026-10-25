using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Procurement;

public sealed record ThreeWayMatchResult(
    bool IsMatched,
    Money PrAmount,
    Money InvoiceAmount,
    Money DiscrepancyAmount,
    string? DiscrepancyReason);

/// <summary>
/// Domain service enforcing three-way matching (Purchase Request, Vendor Invoice, and Goods Receipt / Acceptance).
/// </summary>
public static class ThreeWayMatch
{
    public static ThreeWayMatchResult EvaluateMatch(
        Money prAmount,
        Money invoiceAmount,
        long allowableToleranceMinor = 0)
    {
        if (!string.Equals(prAmount.CurrencyCode, invoiceAmount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return new ThreeWayMatchResult(
                IsMatched: false,
                PrAmount: prAmount,
                InvoiceAmount: invoiceAmount,
                DiscrepancyAmount: invoiceAmount - prAmount,
                DiscrepancyReason: $"Currency mismatch: PR is {prAmount.CurrencyCode}, Invoice is {invoiceAmount.CurrencyCode}.");
        }

        long diffMinor = invoiceAmount.AmountMinor - prAmount.AmountMinor;
        if (diffMinor > allowableToleranceMinor)
        {
            return new ThreeWayMatchResult(
                IsMatched: false,
                PrAmount: prAmount,
                InvoiceAmount: invoiceAmount,
                DiscrepancyAmount: new Money(diffMinor, prAmount.CurrencyCode),
                DiscrepancyReason: $"Invoice exceeds approved PR by {diffMinor} minor units (allowable tolerance: {allowableToleranceMinor}).");
        }

        return new ThreeWayMatchResult(
            IsMatched: true,
            PrAmount: prAmount,
            InvoiceAmount: invoiceAmount,
            DiscrepancyAmount: new Money(diffMinor, prAmount.CurrencyCode),
            DiscrepancyReason: null);
    }
}

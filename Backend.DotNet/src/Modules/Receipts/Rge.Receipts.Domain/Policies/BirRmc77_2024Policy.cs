using System.Diagnostics.CodeAnalysis;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Receipts.Domain.Policies;

/// <summary>
/// Validates compliance with Philippine BIR RMC No. 77-2024 and RA 11976 (Ease of Paying Taxes Act).
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches official BIR statutory regulation RMC No. 77-2024")]
public static class BirRmc77_2024Policy
{
    public const string IssuerTin = "009-842-115-00000";
    public const string IssuerRegisteredName = "R.G.E ACCOUNTING FIRM INC.";
    public const string BirPermitNumber = "FP-102026-0048192";

    public static Result ValidateReceiptInvariants(
        Money expectedGross,
        VatBreakdown breakdown,
        OfficialReceiptNumber receiptNumber,
        AuditDigest digest)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        ArgumentNullException.ThrowIfNull(receiptNumber);
        ArgumentNullException.ThrowIfNull(digest);

        if (expectedGross.AmountMinor <= 0)
        {
            return Result.Failure(
                Error.Validation("Receipts.NonPositiveAmount", "Official Receipt gross amount must be greater than zero."));
        }

        long sumComponents = checked(
            breakdown.VatableSales.AmountMinor +
            breakdown.VatAmount.AmountMinor +
            breakdown.VatExemptSales.AmountMinor +
            breakdown.ZeroRatedSales.AmountMinor);

        if (sumComponents != expectedGross.AmountMinor)
        {
            return Result.Failure(
                Error.Validation(
                    "Receipts.VatBreakdownMismatch",
                    $"VAT components sum ({sumComponents}) does not equal gross amount ({expectedGross.AmountMinor})."));
        }

        return Result.Success();
    }
}

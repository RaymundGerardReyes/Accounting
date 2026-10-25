using System.Globalization;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;

namespace Rge.Receipts.Infrastructure.Pdf;

/// <summary>
/// Builds the structured text lines for a Philippine BIR RMC No. 77-2024 Official Receipt PDF.
/// </summary>
public static class ReceiptLayout
{
    public static IReadOnlyList<string> BuildReceiptLines(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return
        [
            BirRmc77_2024Policy.IssuerRegisteredName,
            $"VAT Reg. TIN: {BirRmc77_2024Policy.IssuerTin} | BIR Permit: {BirRmc77_2024Policy.BirPermitNumber}",
            "------------------------------------------------------------------------",
            $"OFFICIAL RECEIPT NO : {receipt.ReceiptNumber.Value}",
            $"TREASURY ACK NO     : {receipt.Acknowledgement.AcknowledgementNumber}",
            $"DATE ISSUED (UTC)   : {receipt.IssuedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}",
            $"ORDER NUMBER        : {receipt.OrderNumber}",
            $"RECEIVED FROM       : {receipt.CustomerName}",
            $"PARTICULARS         : {receipt.Description}",
            $"PAYMENT METHOD      : {receipt.PaymentMethod.ToUpperInvariant()}",
            "------------------------------------------------------------------------",
            $"VATable Sales       : {receipt.VatBreakdown.VatableSales}",
            $"12% Value-Added Tax : {receipt.VatBreakdown.VatAmount}",
            $"VAT-Exempt Sales    : {receipt.VatBreakdown.VatExemptSales}",
            $"Zero-Rated Sales    : {receipt.VatBreakdown.ZeroRatedSales}",
            $"TOTAL AMOUNT PAID   : {receipt.VatBreakdown.GrossAmount}",
            "------------------------------------------------------------------------",
            $"Cryptographic Digest: {receipt.Digest.Value}",
            "Compliant with BIR RMC No. 77-2024 & RA 11976 (Ease of Paying Taxes Act)"
        ];
    }
}

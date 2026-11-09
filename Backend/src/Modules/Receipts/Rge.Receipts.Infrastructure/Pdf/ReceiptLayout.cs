using Rge.Receipts.Domain;

namespace Rge.Receipts.Infrastructure.Pdf;

/// <summary>
/// Builds the structured text lines and view-model for a Philippine BIR RMC No. 77-2024 Official Receipt.
/// </summary>
public static class ReceiptLayout
{
    public static ReceiptDocumentViewModel ToViewModel(OfficialReceipt receipt) =>
        ReceiptDocumentViewModel.FromReceipt(receipt);

    public static IReadOnlyList<string> BuildLines(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var vm = ToViewModel(receipt);

        return
        [
            vm.IssuerRegisteredName,
            $"VAT Reg. TIN: {vm.IssuerTin} | BIR Permit: {vm.BirPermitNumber}",
            "------------------------------------------------------------------------",
            $"OFFICIAL RECEIPT NO : {vm.OfficialReceiptNumber}",
            $"TREASURY ACK NO     : {vm.TreasuryAcknowledgementNumber}",
            $"DATE ISSUED (PST)   : {vm.FormattedIssuedAtPst}",
            $"DATE ISSUED (UTC)   : {vm.FormattedIssuedAtUtc}",
            $"ORDER NUMBER        : {vm.OrderNumber}",
            $"RECEIVED FROM       : {vm.CustomerName}",
            $"PARTICULARS         : {vm.Description}",
            $"PAYMENT METHOD      : {vm.PaymentMethod.ToUpperInvariant()}",
            "------------------------------------------------------------------------",
            $"VATable Sales       : {vm.FormattedVatableSales}",
            $"12% Value-Added Tax : {vm.FormattedVatAmount}",
            $"VAT-Exempt Sales    : {vm.FormattedVatExemptSales}",
            $"Zero-Rated Sales    : {vm.FormattedZeroRatedSales}",
            $"TOTAL AMOUNT PAID   : {vm.FormattedGrossAmount}",
            "------------------------------------------------------------------------",
            $"Cryptographic Digest: {vm.AuditDigest}",
            vm.LegalComplianceNote
        ];
    }

    public static IReadOnlyList<string> BuildReceiptLines(OfficialReceipt receipt) =>
        BuildLines(receipt);
}

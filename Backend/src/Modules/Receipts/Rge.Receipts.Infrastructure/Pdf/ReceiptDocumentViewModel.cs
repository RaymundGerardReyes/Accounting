using System.Globalization;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;

namespace Rge.Receipts.Infrastructure.Pdf;

public sealed record ReceiptDocumentViewModel
{
    // Identification
    public string ReceiptId { get; init; } = string.Empty;
    public string OfficialReceiptNumber { get; init; } = string.Empty;
    public string TreasuryAcknowledgementNumber { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = "BIR OFFICIAL RECEIPT & TREASURY ACKNOWLEDGEMENT";
    public string DocumentType { get; init; } = "Electronic Official Receipt";
    public string StatusBadge { get; init; } = "OFFICIAL RECEIPT — PAID";

    // Issuer (BIR RMC No. 77-2024 & RA 11976)
    public string IssuerRegisteredName { get; init; } = BirRmc77_2024Policy.IssuerRegisteredName;
    public string IssuerTin { get; init; } = BirRmc77_2024Policy.IssuerTin;
    public string BirPermitNumber { get; init; } = BirRmc77_2024Policy.BirPermitNumber;
    public string IssuerAddress { get; init; } = "Level 24, Tower One, Ayala Triangle, Ayala Ave, Makati, Metro Manila, Philippines";

    // Customer
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;

    // Transaction Details
    public string OrderId { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string PaymentAttemptId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;

    // Timestamps
    public DateTimeOffset IssuedAtUtc { get; init; }
    public DateTimeOffset IssuedAtPst { get; init; }
    public string FormattedIssuedAtUtc { get; init; } = string.Empty;
    public string FormattedIssuedAtPst { get; init; } = string.Empty;
    public string FormattedDualTimestamp { get; init; } = string.Empty;

    // Domain Money
    public Money GrossAmount { get; init; }
    public Money VatableSales { get; init; }
    public Money VatAmount { get; init; }
    public Money VatExemptSales { get; init; }
    public Money ZeroRatedSales { get; init; }

    // Formatted Peso Strings (₱#,##0.00)
    public string FormattedGrossAmount { get; init; } = string.Empty;
    public string FormattedVatableSales { get; init; } = string.Empty;
    public string FormattedVatAmount { get; init; } = string.Empty;
    public string FormattedVatExemptSales { get; init; } = string.Empty;
    public string FormattedZeroRatedSales { get; init; } = string.Empty;

    // Items
    public IReadOnlyList<ReceiptLineItemViewModel> LineItems { get; init; } = [];

    // Security & Legal
    public string AuditDigest { get; init; } = string.Empty;
    public string LegalComplianceNote { get; init; } = "Compliant with BIR RMC No. 77-2024 & RA 11976 (Ease of Paying Taxes Act)";

    public static ReceiptDocumentViewModel FromReceipt(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var pstOffset = TimeSpan.FromHours(8);
        var issuedAtPst = receipt.IssuedAtUtc.ToOffset(pstOffset);

        string grossStr = receipt.VatBreakdown.GrossAmount.ToPhilippinePesoString();
        string vatableStr = receipt.VatBreakdown.VatableSales.ToPhilippinePesoString();
        string vatStr = receipt.VatBreakdown.VatAmount.ToPhilippinePesoString();
        string exemptStr = receipt.VatBreakdown.VatExemptSales.ToPhilippinePesoString();
        string zeroStr = receipt.VatBreakdown.ZeroRatedSales.ToPhilippinePesoString();

        var item = new ReceiptLineItemViewModel(
            Description: receipt.Description,
            Quantity: 1,
            UnitPrice: grossStr,
            Amount: grossStr,
            TaxClassification: "12% VAT Inclusive");

        return new ReceiptDocumentViewModel
        {
            ReceiptId = receipt.Id,
            OfficialReceiptNumber = receipt.ReceiptNumber.Value,
            TreasuryAcknowledgementNumber = receipt.Acknowledgement.AcknowledgementNumber,
            CustomerName = receipt.CustomerName,
            CustomerEmail = receipt.CustomerEmail,
            OrderId = receipt.OrderId,
            OrderNumber = receipt.OrderNumber,
            PaymentAttemptId = receipt.PaymentAttemptId,
            Description = receipt.Description,
            PaymentMethod = receipt.PaymentMethod.ToUpperInvariant(),
            IssuedAtUtc = receipt.IssuedAtUtc,
            IssuedAtPst = issuedAtPst,
            FormattedIssuedAtUtc = receipt.IssuedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            FormattedIssuedAtPst = issuedAtPst.ToString("yyyy-MM-dd HH:mm:ss 'PST (UTC+8)'", CultureInfo.InvariantCulture),
            FormattedDualTimestamp = $"{issuedAtPst:yyyy-MM-dd HH:mm:ss} PST / {receipt.IssuedAtUtc:yyyy-MM-dd HH:mm:ss} UTC",
            GrossAmount = receipt.VatBreakdown.GrossAmount,
            VatableSales = receipt.VatBreakdown.VatableSales,
            VatAmount = receipt.VatBreakdown.VatAmount,
            VatExemptSales = receipt.VatBreakdown.VatExemptSales,
            ZeroRatedSales = receipt.VatBreakdown.ZeroRatedSales,
            FormattedGrossAmount = grossStr,
            FormattedVatableSales = vatableStr,
            FormattedVatAmount = vatStr,
            FormattedVatExemptSales = exemptStr,
            FormattedZeroRatedSales = zeroStr,
            LineItems = [item],
            AuditDigest = receipt.Digest.Value
        };
    }
}

public sealed record ReceiptLineItemViewModel(
    string Description,
    int Quantity,
    string UnitPrice,
    string Amount,
    string TaxClassification);

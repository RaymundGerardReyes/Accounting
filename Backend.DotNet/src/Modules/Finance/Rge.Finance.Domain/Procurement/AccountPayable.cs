using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Procurement;

public enum PayableStatus
{
    Open,
    PartiallyPaid,
    PaidUnverified,
    Paid,
    Reconciled,
    Disputed,
    Cancelled
}

/// <summary>
/// Aggregate root representing an Account Payable created against a verified vendor invoice.
/// </summary>
public sealed class AccountPayable : AggregateRoot<string>
{
    public string PayableNumber { get; private set; }
    public string PurchaseRequestId { get; private set; }
    public string VendorId { get; private set; }
    public string InvoiceNumber { get; private set; }
    public NormalizedInvoiceNumber NormalizedInvoiceNo { get; private set; }
    public string InvoiceDate { get; private set; }
    public string DueDate { get; private set; }
    public Money OriginalPayable { get; private set; }
    public Money CreditAdjustment { get; private set; }
    public string? VatCode { get; private set; }
    public Money VatAmount { get; private set; }
    public string? EwtCode { get; private set; }
    public Money EwtAmount { get; private set; }
    public Money NetPayable { get; private set; }
    public string TaxReviewStatus { get; private set; }
    public bool DuplicateFlag { get; private set; }
    public string? DuplicateReason { get; private set; }
    public string? PaymentTerms { get; private set; }
    public string? AccountingReference { get; private set; }
    public string PostingStatus { get; private set; }
    public PayableStatus Status { get; private set; }
    public string ValidatedById { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public AccountPayable(
        string id,
        string payableNumber,
        string purchaseRequestId,
        string vendorId,
        string invoiceNumber,
        NormalizedInvoiceNumber normalizedInvoiceNo,
        string invoiceDate,
        string dueDate,
        Money originalPayable,
        Money creditAdjustment,
        string? vatCode,
        Money vatAmount,
        string? ewtCode,
        Money ewtAmount,
        Money netPayable,
        string taxReviewStatus,
        bool duplicateFlag,
        string? duplicateReason,
        string? paymentTerms,
        string? accountingReference,
        string validatedById,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        PayableNumber = Guard.AgainstNullOrWhiteSpace(payableNumber);
        PurchaseRequestId = Guard.AgainstNullOrWhiteSpace(purchaseRequestId);
        VendorId = Guard.AgainstNullOrWhiteSpace(vendorId);
        InvoiceNumber = Guard.AgainstNullOrWhiteSpace(invoiceNumber);
        NormalizedInvoiceNo = normalizedInvoiceNo ?? throw new ArgumentNullException(nameof(normalizedInvoiceNo));
        InvoiceDate = Guard.AgainstNullOrWhiteSpace(invoiceDate);
        DueDate = Guard.AgainstNullOrWhiteSpace(dueDate);
        OriginalPayable = originalPayable;
        CreditAdjustment = creditAdjustment;
        VatCode = vatCode;
        VatAmount = vatAmount;
        EwtCode = ewtCode;
        EwtAmount = ewtAmount;
        NetPayable = netPayable;
        TaxReviewStatus = Guard.AgainstNullOrWhiteSpace(taxReviewStatus);
        DuplicateFlag = duplicateFlag;
        DuplicateReason = duplicateReason;
        PaymentTerms = paymentTerms;
        AccountingReference = accountingReference;
        PostingStatus = "pending";
        Status = PayableStatus.Open;
        ValidatedById = Guard.AgainstNullOrWhiteSpace(validatedById);
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<AccountPayable> Create(
        string payableNumber,
        string purchaseRequestId,
        string vendorId,
        string invoiceNumber,
        string invoiceDate,
        string dueDate,
        Money originalPayable,
        Money? creditAdjustment,
        string? vatCode,
        Money? vatAmount,
        string? ewtCode,
        Money? ewtAmount,
        string? paymentTerms,
        string? accountingReference,
        string validatedById,
        DateTimeOffset nowUtc)
    {
        if (originalPayable.AmountMinor <= 0)
        {
            return Result.Failure<AccountPayable>(
                FinanceErrors.Validation("NonPositivePayableAmount", "Payable amount must be greater than zero."));
        }

        var normalizedNo = new NormalizedInvoiceNumber(invoiceNumber);
        Money credit = creditAdjustment ?? Money.Zero(originalPayable.CurrencyCode);
        Money vat = vatAmount ?? Money.Zero(originalPayable.CurrencyCode);
        Money ewt = ewtAmount ?? Money.Zero(originalPayable.CurrencyCode);

        // Net Payable = Original - Credit - EWT
        long netMinor = originalPayable.AmountMinor - credit.AmountMinor - ewt.AmountMinor;
        var net = new Money(netMinor, originalPayable.CurrencyCode);

        var payable = new AccountPayable(
            id: Guid.NewGuid().ToString("D"),
            payableNumber: payableNumber,
            purchaseRequestId: purchaseRequestId,
            vendorId: vendorId,
            invoiceNumber: invoiceNumber,
            normalizedInvoiceNo: normalizedNo,
            invoiceDate: invoiceDate,
            dueDate: dueDate,
            originalPayable: originalPayable,
            creditAdjustment: credit,
            vatCode: vatCode,
            vatAmount: vat,
            ewtCode: ewtCode,
            ewtAmount: ewt,
            netPayable: net,
            taxReviewStatus: "tax_review_required",
            duplicateFlag: false,
            duplicateReason: null,
            paymentTerms: paymentTerms,
            accountingReference: accountingReference,
            validatedById: validatedById,
            nowUtc: nowUtc);

        return Result.Success(payable);
    }

    public void MarkPaid(DateTimeOffset nowUtc)
    {
        Status = PayableStatus.Paid;
        UpdatedAtUtc = nowUtc;
    }
}

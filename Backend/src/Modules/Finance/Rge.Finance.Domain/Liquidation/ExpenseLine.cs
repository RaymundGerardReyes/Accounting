using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Liquidation;

public enum ExpenseLineStatus
{
    Pending,
    Approved,
    PartiallyApproved,
    Rejected,
    Returned
}

/// <summary>
/// Entity representing an itemized receipt or expense within an ExpenseReport.
/// </summary>
public sealed class ExpenseLine : Entity<string>
{
    public string ExpenseReportId { get; private set; }
    public string EmployeeId { get; private set; }
    public string TransactionDate { get; private set; }
    public string? SchoolOrDestination { get; private set; }
    public string ExpenseCategory { get; private set; }
    public string BusinessPurpose { get; private set; }
    public string MerchantOrPayee { get; private set; }
    public string? ReceiptOrInvoiceNo { get; private set; }
    public DuplicateFingerprint? Fingerprint { get; private set; }
    public Money ClaimedAmount { get; private set; }
    public Money? ApprovedAmount { get; private set; }
    public bool OutOfBase { get; private set; }
    public string? ReceiptDocumentId { get; private set; }
    public string? MfarDocumentId { get; private set; }
    public string? TaxCode { get; private set; }
    public Money VatAmount { get; private set; }
    public bool DuplicateWarning { get; private set; }
    public string? DuplicateOfLineId { get; private set; }
    public ExpenseLineStatus LineStatus { get; private set; }

    public string? ReviewerId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? ReviewNotes { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public ExpenseLine(
        string id,
        string expenseReportId,
        string employeeId,
        string transactionDate,
        string? schoolOrDestination,
        string expenseCategory,
        string businessPurpose,
        string merchantOrPayee,
        string? receiptOrInvoiceNo,
        Money claimedAmount,
        bool outOfBase,
        string? receiptDocumentId,
        string? mfarDocumentId,
        string? taxCode,
        Money? vatAmount,
        bool duplicateWarning,
        string? duplicateOfLineId,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        ExpenseReportId = Guard.AgainstNullOrWhiteSpace(expenseReportId);
        EmployeeId = Guard.AgainstNullOrWhiteSpace(employeeId);
        TransactionDate = Guard.AgainstNullOrWhiteSpace(transactionDate);
        SchoolOrDestination = schoolOrDestination;
        ExpenseCategory = Guard.AgainstNullOrWhiteSpace(expenseCategory);
        BusinessPurpose = Guard.AgainstNullOrWhiteSpace(businessPurpose);
        MerchantOrPayee = Guard.AgainstNullOrWhiteSpace(merchantOrPayee);
        ReceiptOrInvoiceNo = receiptOrInvoiceNo;
        ClaimedAmount = claimedAmount;
        OutOfBase = outOfBase;
        ReceiptDocumentId = receiptDocumentId;
        MfarDocumentId = mfarDocumentId;
        TaxCode = taxCode;
        VatAmount = vatAmount ?? Money.Zero(claimedAmount.CurrencyCode);
        DuplicateWarning = duplicateWarning;
        DuplicateOfLineId = duplicateOfLineId;

        if (!string.IsNullOrWhiteSpace(receiptOrInvoiceNo))
        {
            Fingerprint = DuplicateFingerprint.Create(TransactionDate, ExpenseCategory, ClaimedAmount.AmountMinor, receiptOrInvoiceNo);
        }

        LineStatus = ExpenseLineStatus.Pending;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<ExpenseLine> Create(
        string expenseReportId,
        string employeeId,
        string transactionDate,
        string? schoolOrDestination,
        string expenseCategory,
        string businessPurpose,
        string merchantOrPayee,
        string? receiptOrInvoiceNo,
        Money claimedAmount,
        bool outOfBase,
        string? receiptDocumentId,
        string? mfarDocumentId,
        string? taxCode,
        Money? vatAmount,
        bool duplicateWarning,
        string? duplicateOfLineId,
        DateTimeOffset nowUtc)
    {
        if (claimedAmount.AmountMinor <= 0)
        {
            return Result.Failure<ExpenseLine>(
                FinanceErrors.Validation("NonPositiveClaimedAmount", "Claimed amount must be greater than zero."));
        }

        var line = new ExpenseLine(
            id: Guid.NewGuid().ToString("D"),
            expenseReportId: expenseReportId,
            employeeId: employeeId,
            transactionDate: transactionDate,
            schoolOrDestination: schoolOrDestination,
            expenseCategory: expenseCategory,
            businessPurpose: businessPurpose,
            merchantOrPayee: merchantOrPayee,
            receiptOrInvoiceNo: receiptOrInvoiceNo,
            claimedAmount: claimedAmount,
            outOfBase: outOfBase,
            receiptDocumentId: receiptDocumentId,
            mfarDocumentId: mfarDocumentId,
            taxCode: taxCode,
            vatAmount: vatAmount,
            duplicateWarning: duplicateWarning,
            duplicateOfLineId: duplicateOfLineId,
            nowUtc: nowUtc);

        return Result.Success(line);
    }

    public Result Review(
        string reviewerId,
        ExpenseLineStatus status,
        Money? approvedAmount,
        string? notes,
        string? rejectionReason,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(reviewerId);
        if (status is ExpenseLineStatus.Approved or ExpenseLineStatus.PartiallyApproved)
        {
            ApprovedAmount = approvedAmount ?? ClaimedAmount;
            if (ApprovedAmount.Value.AmountMinor > ClaimedAmount.AmountMinor)
            {
                return Result.Failure(FinanceErrors.Validation("ApprovedExceedsClaimed", "Approved amount cannot exceed claimed amount."));
            }
        }
        else
        {
            ApprovedAmount = Money.Zero(ClaimedAmount.CurrencyCode);
        }

        ReviewerId = reviewerId;
        LineStatus = status;
        ReviewNotes = notes;
        RejectionReason = rejectionReason;
        ReviewedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public static ExpenseLine Rehydrate(
        string id,
        string expenseReportId,
        string employeeId,
        string transactionDate,
        string? schoolOrDestination,
        string expenseCategory,
        string businessPurpose,
        string merchantOrPayee,
        string? receiptOrInvoiceNo,
        DuplicateFingerprint? fingerprint,
        Money claimedAmount,
        Money? approvedAmount,
        bool outOfBase,
        string? receiptDocumentId,
        string? mfarDocumentId,
        string? taxCode,
        Money vatAmount,
        bool duplicateWarning,
        string? duplicateOfLineId,
        ExpenseLineStatus lineStatus,
        string? reviewerId,
        DateTimeOffset? reviewedAtUtc,
        string? reviewNotes,
        string? rejectionReason,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        return new ExpenseLine(
            id: id,
            expenseReportId: expenseReportId,
            employeeId: employeeId,
            transactionDate: transactionDate,
            schoolOrDestination: schoolOrDestination,
            expenseCategory: expenseCategory,
            businessPurpose: businessPurpose,
            merchantOrPayee: merchantOrPayee,
            receiptOrInvoiceNo: receiptOrInvoiceNo,
            claimedAmount: claimedAmount,
            outOfBase: outOfBase,
            receiptDocumentId: receiptDocumentId,
            mfarDocumentId: mfarDocumentId,
            taxCode: taxCode,
            vatAmount: vatAmount,
            duplicateWarning: duplicateWarning,
            duplicateOfLineId: duplicateOfLineId,
            nowUtc: createdAtUtc)
        {
            Fingerprint = fingerprint,
            ApprovedAmount = approvedAmount,
            LineStatus = lineStatus,
            ReviewerId = reviewerId,
            ReviewedAtUtc = reviewedAtUtc,
            ReviewNotes = reviewNotes,
            RejectionReason = rejectionReason,
            UpdatedAtUtc = updatedAtUtc
        };
    }
}

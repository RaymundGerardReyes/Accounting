using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Liquidation;

public enum ExpenseReportStatus
{
    Draft,
    Submitted,
    FinanceReview,
    ReturnedForCorrection,
    Approved,
    PartiallyApproved,
    Rejected,
    Cancelled
}

public enum ReportSettlementStatus
{
    Unapplied,
    PartiallyApplied,
    Applied,
    ReimbursementPending,
    Settled
}

/// <summary>
/// Aggregate root representing an employee's monthly expense report and liquidation claims.
/// </summary>
public sealed class ExpenseReport : AggregateRoot<string>
{
    private readonly List<ExpenseLine> _lines = [];

    public string ReportNumber { get; private set; }
    public string EmployeeId { get; private set; }
    public string DepartmentId { get; private set; }
    public string CostCenterId { get; private set; }
    public string ReportingPeriod { get; private set; }
    public string BusinessPurposeSummary { get; private set; }
    public bool OutOfBaseActivity { get; private set; }
    public string? ConsolidatedReceiptDocId { get; private set; }
    public string? MfarDocId { get; private set; }
    public bool EmployeeDeclarationConfirmed { get; private set; }
    public string? Remarks { get; private set; }
    public string Currency { get; private set; }

    public ExpenseReportStatus ReviewStatus { get; private set; }
    public ReportSettlementStatus SettlementStatus { get; private set; }
    public string PostingStatus { get; private set; }

    public string? PolicyVersionId { get; private set; }
    public string? PolicyEvaluationJson { get; private set; }
    public string? ExceptionRequestId { get; private set; }

    public string? ReviewerId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? ReviewNotes { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public bool SlotConsumed { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<ExpenseLine> Lines => _lines.AsReadOnly();

    public ExpenseReport(
        string id,
        string reportNumber,
        string employeeId,
        string departmentId,
        string costCenterId,
        string reportingPeriod,
        string businessPurposeSummary,
        bool outOfBaseActivity,
        string? consolidatedReceiptDocId,
        string? mfarDocId,
        bool employeeDeclarationConfirmed,
        string? remarks,
        string currency,
        string? idempotencyKey,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        ReportNumber = Guard.AgainstNullOrWhiteSpace(reportNumber);
        EmployeeId = Guard.AgainstNullOrWhiteSpace(employeeId);
        DepartmentId = Guard.AgainstNullOrWhiteSpace(departmentId);
        CostCenterId = Guard.AgainstNullOrWhiteSpace(costCenterId);
        ReportingPeriod = Guard.AgainstNullOrWhiteSpace(reportingPeriod);
        BusinessPurposeSummary = Guard.AgainstNullOrWhiteSpace(businessPurposeSummary);
        OutOfBaseActivity = outOfBaseActivity;
        ConsolidatedReceiptDocId = consolidatedReceiptDocId;
        MfarDocId = mfarDocId;
        EmployeeDeclarationConfirmed = employeeDeclarationConfirmed;
        Remarks = remarks;
        Currency = Guard.AgainstNullOrWhiteSpace(currency).ToUpperInvariant();
        IdempotencyKey = idempotencyKey;

        ReviewStatus = ExpenseReportStatus.Draft;
        SettlementStatus = ReportSettlementStatus.Unapplied;
        PostingStatus = "not_applicable";
        SlotConsumed = false;

        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<ExpenseReport> Create(
        string reportNumber,
        string employeeId,
        string departmentId,
        string costCenterId,
        string reportingPeriod,
        string businessPurposeSummary,
        bool outOfBaseActivity,
        string? consolidatedReceiptDocId,
        string? mfarDocId,
        bool employeeDeclarationConfirmed,
        string? remarks,
        string currency,
        string? idempotencyKey,
        DateTimeOffset nowUtc)
    {
        var report = new ExpenseReport(
            id: Guid.NewGuid().ToString("D"),
            reportNumber: reportNumber,
            employeeId: employeeId,
            departmentId: departmentId,
            costCenterId: costCenterId,
            reportingPeriod: reportingPeriod,
            businessPurposeSummary: businessPurposeSummary,
            outOfBaseActivity: outOfBaseActivity,
            consolidatedReceiptDocId: consolidatedReceiptDocId,
            mfarDocId: mfarDocId,
            employeeDeclarationConfirmed: employeeDeclarationConfirmed,
            remarks: remarks,
            currency: currency,
            idempotencyKey: idempotencyKey,
            nowUtc: nowUtc);

        return Result.Success(report);
    }

    public void AddLine(ExpenseLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
    }

    public Result Submit(DateTimeOffset nowUtc)
    {
        if (ReviewStatus is not (ExpenseReportStatus.Draft or ExpenseReportStatus.ReturnedForCorrection))
        {
            return Result.Failure(FinanceErrors.InvalidTransition("ExpenseReport", ReviewStatus.ToString(), nameof(ExpenseReportStatus.Submitted)));
        }

        ReviewStatus = ExpenseReportStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        SlotConsumed = true;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Result Review(
        string reviewerId,
        ExpenseReportStatus outcomeStatus,
        string? notes,
        string? rejectionReason,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(reviewerId);
        if (string.Equals(reviewerId, EmployeeId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FinanceErrors.SodViolation("INV-SOD-001", "Employee cannot review their own expense report."));
        }

        ReviewerId = reviewerId;
        ReviewStatus = outcomeStatus;
        ReviewNotes = notes;
        RejectionReason = rejectionReason;
        ReviewedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Money CalculateTotalClaimed()
    {
        long sum = _lines.Sum(l => l.ClaimedAmount.AmountMinor);
        return new Money(sum, Currency);
    }

    public Money CalculateTotalApproved()
    {
        long sum = _lines
            .Where(l => l.LineStatus is ExpenseLineStatus.Approved or ExpenseLineStatus.PartiallyApproved)
            .Sum(l => l.ApprovedAmount?.AmountMinor ?? 0);
        return new Money(sum, Currency);
    }

    public static ExpenseReport Rehydrate(
        string id,
        string reportNumber,
        string employeeId,
        string departmentId,
        string costCenterId,
        string reportingPeriod,
        string businessPurposeSummary,
        bool outOfBaseActivity,
        string? consolidatedReceiptDocId,
        string? mfarDocId,
        bool employeeDeclarationConfirmed,
        string? remarks,
        string currency,
        ExpenseReportStatus reviewStatus,
        ReportSettlementStatus settlementStatus,
        string postingStatus,
        string? policyVersionId,
        string? policyEvaluationJson,
        string? exceptionRequestId,
        string? reviewerId,
        DateTimeOffset? reviewedAtUtc,
        string? reviewNotes,
        string? rejectionReason,
        DateTimeOffset? submittedAtUtc,
        bool slotConsumed,
        string? idempotencyKey,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        IEnumerable<ExpenseLine> lines)
    {
        var report = new ExpenseReport(
            id: id,
            reportNumber: reportNumber,
            employeeId: employeeId,
            departmentId: departmentId,
            costCenterId: costCenterId,
            reportingPeriod: reportingPeriod,
            businessPurposeSummary: businessPurposeSummary,
            outOfBaseActivity: outOfBaseActivity,
            consolidatedReceiptDocId: consolidatedReceiptDocId,
            mfarDocId: mfarDocId,
            employeeDeclarationConfirmed: employeeDeclarationConfirmed,
            remarks: remarks,
            currency: currency,
            idempotencyKey: idempotencyKey,
            nowUtc: createdAtUtc)
        {
            ReviewStatus = reviewStatus,
            SettlementStatus = settlementStatus,
            PostingStatus = postingStatus,
            PolicyVersionId = policyVersionId,
            PolicyEvaluationJson = policyEvaluationJson,
            ExceptionRequestId = exceptionRequestId,
            ReviewerId = reviewerId,
            ReviewedAtUtc = reviewedAtUtc,
            ReviewNotes = reviewNotes,
            RejectionReason = rejectionReason,
            SubmittedAtUtc = submittedAtUtc,
            SlotConsumed = slotConsumed,
            UpdatedAtUtc = updatedAtUtc
        };

        if (lines is not null)
        {
            report._lines.AddRange(lines);
        }

        return report;
    }
}

using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Procurement;

/// <summary>
/// Aggregate root representing an authoritative Purchase Request (PR).
/// </summary>
public sealed class PurchaseRequest : AggregateRoot<string>
{
    private readonly List<PurchaseRequestLine> _lines = [];

    public string RequestNumber { get; private set; }
    public string RequestorId { get; private set; }
    public string VendorId { get; private set; }
    public string PaymentCategory { get; private set; }
    public bool IsRestrictedCategory { get; private set; }
    public string BusinessPurpose { get; private set; }
    public string DepartmentId { get; private set; }
    public string CostCenterId { get; private set; }
    public string? BudgetReference { get; private set; }
    public Money RequestedAmount { get; private set; }
    public Money? ApprovedAmount { get; private set; }
    public string RequestedPaymentDate { get; private set; }
    public string? DueDate { get; private set; }
    public IReadOnlyList<string> SupportingDocIds { get; private set; }
    public string? InvoiceDocId { get; private set; }
    public string? Remarks { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }
    public string WorkflowStatus { get; private set; }
    public string PostingStatus { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<PurchaseRequestLine> Lines => _lines.AsReadOnly();

    public PurchaseRequest(
        string id,
        string requestNumber,
        string requestorId,
        string vendorId,
        string paymentCategory,
        bool isRestrictedCategory,
        string businessPurpose,
        string departmentId,
        string costCenterId,
        string? budgetReference,
        Money requestedAmount,
        string requestedPaymentDate,
        string? dueDate,
        IReadOnlyList<string> supportingDocIds,
        string? invoiceDocId,
        string? remarks,
        string? idempotencyKey,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        RequestNumber = Guard.AgainstNullOrWhiteSpace(requestNumber);
        RequestorId = Guard.AgainstNullOrWhiteSpace(requestorId);
        VendorId = Guard.AgainstNullOrWhiteSpace(vendorId);
        PaymentCategory = Guard.AgainstNullOrWhiteSpace(paymentCategory);
        IsRestrictedCategory = isRestrictedCategory;
        BusinessPurpose = Guard.AgainstNullOrWhiteSpace(businessPurpose);
        DepartmentId = Guard.AgainstNullOrWhiteSpace(departmentId);
        CostCenterId = Guard.AgainstNullOrWhiteSpace(costCenterId);
        BudgetReference = budgetReference;
        RequestedAmount = requestedAmount;
        RequestedPaymentDate = Guard.AgainstNullOrWhiteSpace(requestedPaymentDate);
        DueDate = dueDate;
        SupportingDocIds = supportingDocIds ?? Array.Empty<string>();
        InvoiceDocId = invoiceDocId;
        Remarks = remarks;
        IdempotencyKey = idempotencyKey;

        ApprovalStatus = ApprovalStatus.Draft;
        WorkflowStatus = "draft";
        PostingStatus = "not_applicable";

        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<PurchaseRequest> Create(
        string requestNumber,
        string requestorId,
        string vendorId,
        string paymentCategory,
        bool isRestrictedCategory,
        string businessPurpose,
        string departmentId,
        string costCenterId,
        string? budgetReference,
        Money requestedAmount,
        string requestedPaymentDate,
        string? dueDate,
        IReadOnlyList<string>? supportingDocIds,
        string? invoiceDocId,
        string? remarks,
        string? idempotencyKey,
        DateTimeOffset nowUtc)
    {
        if (requestedAmount.AmountMinor <= 0)
        {
            return Result.Failure<PurchaseRequest>(
                FinanceErrors.Validation("NonPositiveRequestedAmount", "Requested amount must be greater than zero."));
        }

        var pr = new PurchaseRequest(
            id: Guid.NewGuid().ToString("D"),
            requestNumber: requestNumber,
            requestorId: requestorId,
            vendorId: vendorId,
            paymentCategory: paymentCategory,
            isRestrictedCategory: isRestrictedCategory,
            businessPurpose: businessPurpose,
            departmentId: departmentId,
            costCenterId: costCenterId,
            budgetReference: budgetReference,
            requestedAmount: requestedAmount,
            requestedPaymentDate: requestedPaymentDate,
            dueDate: dueDate,
            supportingDocIds: supportingDocIds ?? Array.Empty<string>(),
            invoiceDocId: invoiceDocId,
            remarks: remarks,
            idempotencyKey: idempotencyKey,
            nowUtc: nowUtc);

        return Result.Success(pr);
    }

    public void AddLine(PurchaseRequestLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
    }

    public Result Submit(DateTimeOffset nowUtc)
    {
        Result<ApprovalStatus> transition = StateTransitionTables.TransitionApproval(ApprovalStatus, ApprovalStatus.Submitted);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        ApprovalStatus = transition.Value;
        WorkflowStatus = "submitted";
        SubmittedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Result Approve(string approverId, Money approvedAmount, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        if (string.Equals(approverId, RequestorId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FinanceErrors.SodViolation("INV-SOD-001", "Requestor cannot approve own purchase request."));
        }

        Result<ApprovalStatus> transition = StateTransitionTables.TransitionApproval(ApprovalStatus, ApprovalStatus.Approved);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        ApprovalStatus = transition.Value;
        WorkflowStatus = "approved";
        ApprovedAmount = approvedAmount;
        ApprovedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Result Reject(string approverId, string reason, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        Result<ApprovalStatus> transition = StateTransitionTables.TransitionApproval(ApprovalStatus, ApprovalStatus.Rejected);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        ApprovalStatus = transition.Value;
        WorkflowStatus = "rejected";
        Remarks = reason;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }
}

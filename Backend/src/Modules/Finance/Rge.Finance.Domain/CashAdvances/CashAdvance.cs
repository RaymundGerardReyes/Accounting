using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.CashAdvances.Events;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.CashAdvances;

/// <summary>
/// Authoritative Aggregate Root for an Employee Cash Advance.
/// Governed by 5 independent state machines (Approval, Payment, Liquidation, Settlement, Posting).
/// </summary>
public sealed class CashAdvance : AggregateRoot<string>
{
    public AdvanceNumber AdvanceNumber { get; private set; }
    public string RequestorId { get; private set; }
    public string EmployeeId { get; private set; }
    public string DepartmentId { get; private set; }
    public string CostCenterId { get; private set; }
    public PurposeCode PurposeCode { get; private set; }
    public string BusinessJustification { get; private set; }
    public string? DestinationOrSite { get; private set; }
    public Money RequestedAmount { get; private set; }
    public Money? ApprovedAmount { get; private set; }
    public string DateNeeded { get; private set; }
    public string? ActivityStartDate { get; private set; }
    public string? ActivityEndDate { get; private set; }
    public string? LiquidationDueDate { get; private set; }
    public string? Remarks { get; private set; }
    public IReadOnlyList<string> AttachmentIds { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public LiquidationReviewStatus LiquidationReviewStatus { get; private set; }
    public SettlementStatus SettlementStatus { get; private set; }
    public PostingStatus PostingStatus { get; private set; }

    public string? PolicyVersionId { get; private set; }
    public string? PolicyEvaluationJson { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public string? ClosedById { get; private set; }
    public string? ClosureReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public CashAdvance(
        string id,
        AdvanceNumber advanceNumber,
        string requestorId,
        string employeeId,
        string departmentId,
        string costCenterId,
        PurposeCode purposeCode,
        string businessJustification,
        string? destinationOrSite,
        Money requestedAmount,
        string dateNeeded,
        string? activityStartDate,
        string? activityEndDate,
        string? liquidationDueDate,
        string? remarks,
        IReadOnlyList<string> attachmentIds,
        string? idempotencyKey,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        AdvanceNumber = advanceNumber ?? throw new ArgumentNullException(nameof(advanceNumber));
        RequestorId = Guard.AgainstNullOrWhiteSpace(requestorId);
        EmployeeId = Guard.AgainstNullOrWhiteSpace(employeeId);
        DepartmentId = Guard.AgainstNullOrWhiteSpace(departmentId);
        CostCenterId = Guard.AgainstNullOrWhiteSpace(costCenterId);
        PurposeCode = purposeCode ?? throw new ArgumentNullException(nameof(purposeCode));
        BusinessJustification = Guard.AgainstNullOrWhiteSpace(businessJustification);
        DestinationOrSite = destinationOrSite;
        RequestedAmount = requestedAmount;
        DateNeeded = Guard.AgainstNullOrWhiteSpace(dateNeeded);
        ActivityStartDate = activityStartDate;
        ActivityEndDate = activityEndDate;
        LiquidationDueDate = liquidationDueDate;
        Remarks = remarks;
        AttachmentIds = attachmentIds ?? Array.Empty<string>();
        IdempotencyKey = idempotencyKey;

        ApprovalStatus = ApprovalStatus.Draft;
        PaymentStatus = PaymentStatus.NotStarted;
        LiquidationReviewStatus = LiquidationReviewStatus.NotStarted;
        SettlementStatus = SettlementStatus.Open;
        PostingStatus = PostingStatus.NotApplicable;

        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new CashAdvanceCreated(
            Id,
            AdvanceNumber.Value,
            RequestorId,
            EmployeeId,
            RequestedAmount.AmountMinor,
            RequestedAmount.CurrencyCode,
            nowUtc));
    }

    public static Result<CashAdvance> Create(
        AdvanceNumber advanceNumber,
        string requestorId,
        string employeeId,
        string departmentId,
        string costCenterId,
        PurposeCode purposeCode,
        string businessJustification,
        string? destinationOrSite,
        Money requestedAmount,
        string dateNeeded,
        string? activityStartDate,
        string? activityEndDate,
        string? liquidationDueDate,
        string? remarks,
        IReadOnlyList<string>? attachmentIds,
        string? idempotencyKey,
        DateTimeOffset nowUtc)
    {
        if (requestedAmount.AmountMinor <= 0)
        {
            return Result.Failure<CashAdvance>(
                FinanceErrors.Validation("NonPositiveAmount", "Requested amount must be greater than zero."));
        }

        var advance = new CashAdvance(
            id: Guid.NewGuid().ToString("D"),
            advanceNumber: advanceNumber,
            requestorId: requestorId,
            employeeId: employeeId,
            departmentId: departmentId,
            costCenterId: costCenterId,
            purposeCode: purposeCode,
            businessJustification: businessJustification,
            destinationOrSite: destinationOrSite,
            requestedAmount: requestedAmount,
            dateNeeded: dateNeeded,
            activityStartDate: activityStartDate,
            activityEndDate: activityEndDate,
            liquidationDueDate: liquidationDueDate,
            remarks: remarks,
            attachmentIds: attachmentIds ?? Array.Empty<string>(),
            idempotencyKey: idempotencyKey,
            nowUtc: nowUtc);

        return Result.Success(advance);
    }

    public Result Submit(DateTimeOffset nowUtc)
    {
        Result<ApprovalStatus> transition = StateTransitionTables.TransitionApproval(ApprovalStatus, ApprovalStatus.Submitted);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        ApprovalStatus = transition.Value;
        SubmittedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new CashAdvanceSubmitted(Id, nowUtc));
        return Result.Success();
    }

    public Result Approve(string approverId, Money approvedAmount, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        if (string.Equals(approverId, RequestorId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FinanceErrors.SodViolation("INV-SOD-001", "Requestor cannot approve own cash advance."));
        }

        Result<ApprovalStatus> transition = StateTransitionTables.TransitionApproval(ApprovalStatus, ApprovalStatus.Approved);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        ApprovalStatus = transition.Value;
        ApprovedAmount = approvedAmount;
        ApprovedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new CashAdvanceApproved(Id, approverId, approvedAmount.AmountMinor, nowUtc));
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
        Remarks = reason;
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new CashAdvanceRejected(Id, approverId, reason, nowUtc));
        return Result.Success();
    }

    public Result ReturnForCorrection(string approverId, string notes, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        Result<ApprovalStatus> transition = StateTransitionTables.TransitionApproval(ApprovalStatus, ApprovalStatus.ReturnedForCorrection);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        ApprovalStatus = transition.Value;
        Remarks = notes;
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new CashAdvanceReturnedForCorrection(Id, approverId, notes, nowUtc));
        return Result.Success();
    }

    public Result Cancel(string actorId, string reason, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        Result<ApprovalStatus> transition = StateTransitionTables.TransitionApproval(ApprovalStatus, ApprovalStatus.Cancelled);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        ApprovalStatus = transition.Value;
        ClosureReason = reason;
        ClosedAtUtc = nowUtc;
        ClosedById = actorId;
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new CashAdvanceCancelled(Id, actorId, reason, nowUtc));
        return Result.Success();
    }

    public Result TransitionPaymentStatus(PaymentStatus nextStatus, DateTimeOffset nowUtc)
    {
        Result<PaymentStatus> transition = StateTransitionTables.TransitionPayment(PaymentStatus, nextStatus);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        PaymentStatus = transition.Value;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result TransitionLiquidationStatus(LiquidationReviewStatus nextStatus, DateTimeOffset nowUtc)
    {
        Result<LiquidationReviewStatus> transition = StateTransitionTables.TransitionLiquidation(LiquidationReviewStatus, nextStatus);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        LiquidationReviewStatus = transition.Value;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result TransitionSettlementStatus(SettlementStatus nextStatus, DateTimeOffset nowUtc)
    {
        Result<SettlementStatus> transition = StateTransitionTables.TransitionSettlement(SettlementStatus, nextStatus);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        SettlementStatus = transition.Value;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Close(string controllerId, string reason, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(controllerId);
        Result<SettlementStatus> transition = StateTransitionTables.TransitionSettlement(SettlementStatus, SettlementStatus.Closed);
        if (transition.IsFailure)
        {
            return Result.Failure(transition.Error);
        }

        SettlementStatus = transition.Value;
        ClosedAtUtc = nowUtc;
        ClosedById = controllerId;
        ClosureReason = reason;
        UpdatedAtUtc = nowUtc;

        RaiseDomainEvent(new CashAdvanceClosed(Id, controllerId, reason, nowUtc));
        return Result.Success();
    }

    public string DeriveOverallStage()
    {
        if (SettlementStatus == SettlementStatus.Closed) return "Closed";
        if (SettlementStatus == SettlementStatus.Reconciled) return "Reconciled";
        if (SettlementStatus == SettlementStatus.ShortfallReceivable) return "Shortfall Due from Employee";
        if (LiquidationReviewStatus == LiquidationReviewStatus.Approved) return "Liquidation Approved";
        if (LiquidationReviewStatus is LiquidationReviewStatus.Submitted or LiquidationReviewStatus.FinanceReview) return "Liquidation Under Review";
        if (PaymentStatus == PaymentStatus.EvidenceVerified) return "Released";
        if (PaymentStatus is PaymentStatus.Prepared or PaymentStatus.Verified or PaymentStatus.BankApproved or PaymentStatus.ExecutedUnverified) return "Payment Processing";
        if (ApprovalStatus == ApprovalStatus.Approved) return "Approved (Unreleased)";
        if (ApprovalStatus == ApprovalStatus.Submitted) return "Submitted";
        if (ApprovalStatus == ApprovalStatus.ReturnedForCorrection) return "Returned for Correction";
        if (ApprovalStatus == ApprovalStatus.Rejected) return "Rejected";
        if (ApprovalStatus == ApprovalStatus.Cancelled) return "Cancelled";
        return "Draft";
    }

    public static CashAdvance Rehydrate(
        string id,
        AdvanceNumber advanceNumber,
        string requestorId,
        string employeeId,
        string departmentId,
        string costCenterId,
        PurposeCode purposeCode,
        string businessJustification,
        string? destinationOrSite,
        Money requestedAmount,
        Money? approvedAmount,
        string dateNeeded,
        string? activityStartDate,
        string? activityEndDate,
        string? liquidationDueDate,
        string? remarks,
        IReadOnlyList<string> attachmentIds,
        ApprovalStatus approvalStatus,
        PaymentStatus paymentStatus,
        LiquidationReviewStatus liquidationStatus,
        SettlementStatus settlementStatus,
        PostingStatus postingStatus,
        string? policyVersionId,
        string? policyEvaluationJson,
        string? idempotencyKey,
        DateTimeOffset? submittedAtUtc,
        DateTimeOffset? approvedAtUtc,
        DateTimeOffset? closedAtUtc,
        string? closedById,
        string? closureReason,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var advance = new CashAdvance(
            id: id,
            advanceNumber: advanceNumber,
            requestorId: requestorId,
            employeeId: employeeId,
            departmentId: departmentId,
            costCenterId: costCenterId,
            purposeCode: purposeCode,
            businessJustification: businessJustification,
            destinationOrSite: destinationOrSite,
            requestedAmount: requestedAmount,
            dateNeeded: dateNeeded,
            activityStartDate: activityStartDate,
            activityEndDate: activityEndDate,
            liquidationDueDate: liquidationDueDate,
            remarks: remarks,
            attachmentIds: attachmentIds,
            idempotencyKey: idempotencyKey,
            nowUtc: createdAtUtc)
        {
            ApprovedAmount = approvedAmount,
            ApprovalStatus = approvalStatus,
            PaymentStatus = paymentStatus,
            LiquidationReviewStatus = liquidationStatus,
            SettlementStatus = settlementStatus,
            PostingStatus = postingStatus,
            PolicyVersionId = policyVersionId,
            PolicyEvaluationJson = policyEvaluationJson,
            SubmittedAtUtc = submittedAtUtc,
            ApprovedAtUtc = approvedAtUtc,
            ClosedAtUtc = closedAtUtc,
            ClosedById = closedById,
            ClosureReason = closureReason,
            UpdatedAtUtc = updatedAtUtc
        };

        return advance;
    }
}

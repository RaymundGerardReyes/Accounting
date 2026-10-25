using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Finance.Domain.CashAdvances.StateMachines;

public enum ApprovalStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    ReturnedForCorrection,
    Cancelled
}

public enum PaymentStatus
{
    NotStarted,
    Prepared,
    Verified,
    BankApproved,
    ExecutedUnverified,
    EvidenceVerified,
    Failed,
    Unknown,
    Cancelled
}

public enum LiquidationReviewStatus
{
    NotStarted,
    Submitted,
    FinanceReview,
    Approved
}

public enum SettlementStatus
{
    Open,
    Reconciled,
    ShortfallReceivable,
    Closed
}

public enum PostingStatus
{
    NotApplicable,
    Posted
}

/// <summary>
/// Transition tables for all five independent state machines of a CashAdvance.
/// </summary>
public static class StateTransitionTables
{
    public static Result<ApprovalStatus> TransitionApproval(ApprovalStatus from, ApprovalStatus to)
    {
        bool valid = (from, to) switch
        {
            (ApprovalStatus.Draft, ApprovalStatus.Submitted) => true,
            (ApprovalStatus.Draft, ApprovalStatus.Cancelled) => true,
            (ApprovalStatus.Submitted, ApprovalStatus.Approved) => true,
            (ApprovalStatus.Submitted, ApprovalStatus.Rejected) => true,
            (ApprovalStatus.Submitted, ApprovalStatus.ReturnedForCorrection) => true,
            (ApprovalStatus.Submitted, ApprovalStatus.Cancelled) => true,
            (ApprovalStatus.ReturnedForCorrection, ApprovalStatus.Submitted) => true,
            (ApprovalStatus.ReturnedForCorrection, ApprovalStatus.Cancelled) => true,
            _ => false
        };

        return valid
            ? Result.Success(to)
            : Result.Failure<ApprovalStatus>(
                Error.Validation("Finance.InvalidApprovalTransition", $"Cannot transition approval_status from '{from}' to '{to}'."));
    }

    public static Result<PaymentStatus> TransitionPayment(PaymentStatus from, PaymentStatus to)
    {
        bool valid = (from, to) switch
        {
            (PaymentStatus.NotStarted, PaymentStatus.Prepared) => true,
            (PaymentStatus.Prepared, PaymentStatus.Verified) => true,
            (PaymentStatus.Prepared, PaymentStatus.Cancelled) => true,
            (PaymentStatus.Verified, PaymentStatus.BankApproved) => true,
            (PaymentStatus.Verified, PaymentStatus.Cancelled) => true,
            (PaymentStatus.BankApproved, PaymentStatus.ExecutedUnverified) => true,
            (PaymentStatus.BankApproved, PaymentStatus.Failed) => true,
            (PaymentStatus.BankApproved, PaymentStatus.Unknown) => true,
            (PaymentStatus.ExecutedUnverified, PaymentStatus.EvidenceVerified) => true,
            (PaymentStatus.Unknown, PaymentStatus.ExecutedUnverified) => true,
            (PaymentStatus.Unknown, PaymentStatus.Failed) => true,
            _ => false
        };

        return valid
            ? Result.Success(to)
            : Result.Failure<PaymentStatus>(
                Error.Validation("Finance.InvalidPaymentTransition", $"Cannot transition payment_status from '{from}' to '{to}'."));
    }

    public static Result<LiquidationReviewStatus> TransitionLiquidation(LiquidationReviewStatus from, LiquidationReviewStatus to)
    {
        bool valid = (from, to) switch
        {
            (LiquidationReviewStatus.NotStarted, LiquidationReviewStatus.Submitted) => true,
            (LiquidationReviewStatus.Submitted, LiquidationReviewStatus.FinanceReview) => true,
            (LiquidationReviewStatus.FinanceReview, LiquidationReviewStatus.Approved) => true,
            (LiquidationReviewStatus.FinanceReview, LiquidationReviewStatus.Submitted) => true,
            _ => false
        };

        return valid
            ? Result.Success(to)
            : Result.Failure<LiquidationReviewStatus>(
                Error.Validation("Finance.InvalidLiquidationTransition", $"Cannot transition liquidation_review_status from '{from}' to '{to}'."));
    }

    public static Result<SettlementStatus> TransitionSettlement(SettlementStatus from, SettlementStatus to)
    {
        bool valid = (from, to) switch
        {
            (SettlementStatus.Open, SettlementStatus.Reconciled) => true,
            (SettlementStatus.Open, SettlementStatus.ShortfallReceivable) => true,
            (SettlementStatus.ShortfallReceivable, SettlementStatus.Reconciled) => true,
            (SettlementStatus.Reconciled, SettlementStatus.Closed) => true,
            _ => false
        };

        return valid
            ? Result.Success(to)
            : Result.Failure<SettlementStatus>(
                Error.Validation("Finance.InvalidSettlementTransition", $"Cannot transition settlement_status from '{from}' to '{to}'."));
    }

    public static Result<PostingStatus> TransitionPosting(PostingStatus from, PostingStatus to)
    {
        bool valid = (from, to) switch
        {
            (PostingStatus.NotApplicable, PostingStatus.Posted) => true,
            (PostingStatus.Posted, PostingStatus.Posted) => true,
            _ => false
        };

        return valid
            ? Result.Success(to)
            : Result.Failure<PostingStatus>(
                Error.Validation("Finance.InvalidPostingTransition", $"Cannot transition posting_status from '{from}' to '{to}'."));
    }
}

using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Disbursements;

/// <summary>
/// Aggregate root representing a 4-stage separation-of-duties bank disbursement.
/// Enforces: Maker != Verifier != Bank Approver != Evidence Verifier (INV-SOD-002).
/// </summary>
public sealed class Disbursement : AggregateRoot<string>
{
    public string DisbursementNumber { get; private set; }
    public string AdvanceId { get; private set; }
    public Money PaymentAmount { get; private set; }
    public Money? ExecutedAmount { get; private set; }
    public string PaymentMethod { get; private set; }
    public string? RecipientAccountRef { get; private set; }
    public DisbursementStage Status { get; private set; }

    public string MakerId { get; private set; }
    public DateTimeOffset PreparedAtUtc { get; private set; }

    public string? VerifierId { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public string? VerificationNotes { get; private set; }

    public string? BankApproverId { get; private set; }
    public DateTimeOffset? BankApprovedAtUtc { get; private set; }
    public string? BankApprovalNotes { get; private set; }

    public string? ExecutedById { get; private set; }
    public DateTimeOffset? ExecutedAtUtc { get; private set; }
    public BankReference? BankReference { get; private set; }

    public string? EvidenceDocumentId { get; private set; }
    public string? EvidenceVerifierId { get; private set; }
    public DateTimeOffset? EvidenceVerifiedAtUtc { get; private set; }
    public string? EvidenceVerificationNotes { get; private set; }

    public string? FailureReason { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Disbursement(
        string id,
        string disbursementNumber,
        string advanceId,
        Money paymentAmount,
        string paymentMethod,
        string? recipientAccountRef,
        string makerId,
        string? idempotencyKey,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        DisbursementNumber = Guard.AgainstNullOrWhiteSpace(disbursementNumber);
        AdvanceId = Guard.AgainstNullOrWhiteSpace(advanceId);
        PaymentAmount = paymentAmount;
        PaymentMethod = Guard.AgainstNullOrWhiteSpace(paymentMethod);
        RecipientAccountRef = recipientAccountRef;
        MakerId = Guard.AgainstNullOrWhiteSpace(makerId);
        IdempotencyKey = idempotencyKey;

        Status = DisbursementStage.Prepared;
        PreparedAtUtc = nowUtc;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<Disbursement> Prepare(
        string disbursementNumber,
        string advanceId,
        Money paymentAmount,
        string paymentMethod,
        string? recipientAccountRef,
        string makerId,
        string? idempotencyKey,
        DateTimeOffset nowUtc)
    {
        if (paymentAmount.AmountMinor <= 0)
        {
            return Result.Failure<Disbursement>(
                FinanceErrors.Validation("NonPositivePaymentAmount", "Payment amount must be greater than zero."));
        }

        var disbursement = new Disbursement(
            id: Guid.NewGuid().ToString("D"),
            disbursementNumber: disbursementNumber,
            advanceId: advanceId,
            paymentAmount: paymentAmount,
            paymentMethod: paymentMethod,
            recipientAccountRef: recipientAccountRef,
            makerId: makerId,
            idempotencyKey: idempotencyKey,
            nowUtc: nowUtc);

        return Result.Success(disbursement);
    }

    public Result Verify(string verifierId, string? notes, DateTimeOffset nowUtc)
    {
        if (Status != DisbursementStage.Prepared)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("Disbursement", Status.ToString(), nameof(DisbursementStage.Verified)));
        }

        Result sodCheck = FourEyesRule.EnsureDistinctDisbursementActors(MakerId, verifierId: verifierId);
        if (sodCheck.IsFailure)
        {
            return Result.Failure(sodCheck.Error);
        }

        VerifierId = verifierId;
        VerifiedAtUtc = nowUtc;
        VerificationNotes = notes;
        Status = DisbursementStage.Verified;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Result BankApprove(string bankApproverId, string? notes, DateTimeOffset nowUtc)
    {
        if (Status != DisbursementStage.Verified)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("Disbursement", Status.ToString(), nameof(DisbursementStage.BankApproved)));
        }

        Result sodCheck = FourEyesRule.EnsureDistinctDisbursementActors(MakerId, verifierId: VerifierId, bankApproverId: bankApproverId);
        if (sodCheck.IsFailure)
        {
            return Result.Failure(sodCheck.Error);
        }

        BankApproverId = bankApproverId;
        BankApprovedAtUtc = nowUtc;
        BankApprovalNotes = notes;
        Status = DisbursementStage.BankApproved;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Result Execute(string executedById, BankReference bankRef, Money? executedAmount, DateTimeOffset nowUtc)
    {
        if (Status != DisbursementStage.BankApproved)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("Disbursement", Status.ToString(), nameof(DisbursementStage.ExecutedUnverified)));
        }

        ExecutedById = Guard.AgainstNullOrWhiteSpace(executedById);
        BankReference = bankRef ?? throw new ArgumentNullException(nameof(bankRef));
        ExecutedAmount = executedAmount ?? PaymentAmount;
        ExecutedAtUtc = nowUtc;
        Status = DisbursementStage.ExecutedUnverified;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Result VerifyEvidence(string evidenceVerifierId, string evidenceDocId, string? notes, DateTimeOffset nowUtc)
    {
        if (Status != DisbursementStage.ExecutedUnverified)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("Disbursement", Status.ToString(), nameof(DisbursementStage.EvidenceVerified)));
        }

        Result sodCheck = FourEyesRule.EnsureDistinctDisbursementActors(
            MakerId,
            verifierId: VerifierId,
            bankApproverId: BankApproverId,
            evidenceVerifierId: evidenceVerifierId);

        if (sodCheck.IsFailure)
        {
            return Result.Failure(sodCheck.Error);
        }

        EvidenceVerifierId = evidenceVerifierId;
        EvidenceDocumentId = Guard.AgainstNullOrWhiteSpace(evidenceDocId);
        EvidenceVerificationNotes = notes;
        EvidenceVerifiedAtUtc = nowUtc;
        Status = DisbursementStage.EvidenceVerified;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public Result SetExceptionState(DisbursementStage targetState, string reason, DateTimeOffset nowUtc)
    {
        if (targetState is not (DisbursementStage.Failed or DisbursementStage.Unknown or DisbursementStage.Cancelled))
        {
            return Result.Failure(FinanceErrors.Validation("InvalidExceptionState", $"State '{targetState}' is not a valid disbursement exception state."));
        }

        FailureReason = reason;
        Status = targetState;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public static Disbursement Rehydrate(
        string id,
        string disbursementNumber,
        string advanceId,
        Money paymentAmount,
        Money? executedAmount,
        string paymentMethod,
        string? recipientAccountRef,
        DisbursementStage status,
        string makerId,
        DateTimeOffset preparedAtUtc,
        string? verifierId,
        DateTimeOffset? verifiedAtUtc,
        string? verificationNotes,
        string? bankApproverId,
        DateTimeOffset? bankApprovedAtUtc,
        string? bankApprovalNotes,
        string? executedById,
        DateTimeOffset? executedAtUtc,
        BankReference? bankReference,
        string? evidenceDocumentId,
        string? evidenceVerifierId,
        DateTimeOffset? evidenceVerifiedAtUtc,
        string? evidenceVerificationNotes,
        string? failureReason,
        string? idempotencyKey,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        return new Disbursement(
            id: id,
            disbursementNumber: disbursementNumber,
            advanceId: advanceId,
            paymentAmount: paymentAmount,
            paymentMethod: paymentMethod,
            recipientAccountRef: recipientAccountRef,
            makerId: makerId,
            idempotencyKey: idempotencyKey,
            nowUtc: createdAtUtc)
        {
            ExecutedAmount = executedAmount,
            Status = status,
            PreparedAtUtc = preparedAtUtc,
            VerifierId = verifierId,
            VerifiedAtUtc = verifiedAtUtc,
            VerificationNotes = verificationNotes,
            BankApproverId = bankApproverId,
            BankApprovedAtUtc = bankApprovedAtUtc,
            BankApprovalNotes = bankApprovalNotes,
            ExecutedById = executedById,
            ExecutedAtUtc = executedAtUtc,
            BankReference = bankReference,
            EvidenceDocumentId = evidenceDocumentId,
            EvidenceVerifierId = evidenceVerifierId,
            EvidenceVerifiedAtUtc = evidenceVerifiedAtUtc,
            EvidenceVerificationNotes = evidenceVerificationNotes,
            FailureReason = failureReason,
            UpdatedAtUtc = updatedAtUtc
        };
    }
}

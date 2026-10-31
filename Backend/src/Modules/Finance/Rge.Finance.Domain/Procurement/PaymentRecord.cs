using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Procurement;

public enum PaymentRecordStatus
{
    Prepared,
    Verified,
    BankApproved,
    PaidUnverified,
    Reconciled,
    Failed,
    PaymentUnknown,
    Cancelled
}

/// <summary>
/// Aggregate root representing an authoritative vendor payment record for Accounts Payable.
/// Governed by Four-Eyes SoD (Maker != Verifier != Bank Approver != Evidence Verifier).
/// </summary>
public sealed class PaymentRecord : AggregateRoot<string>
{
    public string PaymentNumber { get; private set; }
    public string PayableId { get; private set; }
    public string PurchaseRequestId { get; private set; }
    public string VendorId { get; private set; }
    public Money PaymentAmount { get; private set; }
    public Money? ExecutedAmount { get; private set; }
    public string PaymentMethod { get; private set; }
    public PaymentRecordStatus Status { get; private set; }

    public string MakerId { get; private set; }
    public DateTimeOffset PreparedAtUtc { get; private set; }

    public string? VerifierId { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    public string? BankApproverId { get; private set; }
    public DateTimeOffset? BankApprovedAtUtc { get; private set; }

    public string? ExecutedById { get; private set; }
    public string? ExecutionDate { get; private set; }
    public DateTimeOffset? ExecutedAtUtc { get; private set; }
    public BankReference? BankReference { get; private set; }
    public string? RemittanceDocId { get; private set; }

    public string? EvidenceVerifierId { get; private set; }
    public DateTimeOffset? EvidenceVerifiedAtUtc { get; private set; }
    public string ReconciliationStatus { get; private set; }
    public string? ReconciliationNotes { get; private set; }
    public string? FailureReason { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public PaymentRecord(
        string id,
        string paymentNumber,
        string payableId,
        string purchaseRequestId,
        string vendorId,
        Money paymentAmount,
        string paymentMethod,
        string makerId,
        string? idempotencyKey,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        PaymentNumber = Guard.AgainstNullOrWhiteSpace(paymentNumber);
        PayableId = Guard.AgainstNullOrWhiteSpace(payableId);
        PurchaseRequestId = Guard.AgainstNullOrWhiteSpace(purchaseRequestId);
        VendorId = Guard.AgainstNullOrWhiteSpace(vendorId);
        PaymentAmount = paymentAmount;
        PaymentMethod = Guard.AgainstNullOrWhiteSpace(paymentMethod);
        MakerId = Guard.AgainstNullOrWhiteSpace(makerId);
        IdempotencyKey = idempotencyKey;

        Status = PaymentRecordStatus.Prepared;
        ReconciliationStatus = "unreconciled";
        PreparedAtUtc = nowUtc;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<PaymentRecord> Prepare(
        string paymentNumber,
        string payableId,
        string purchaseRequestId,
        string vendorId,
        Money paymentAmount,
        string paymentMethod,
        string makerId,
        string? idempotencyKey,
        DateTimeOffset nowUtc)
    {
        if (paymentAmount.AmountMinor <= 0)
        {
            return Result.Failure<PaymentRecord>(
                FinanceErrors.Validation("NonPositivePaymentAmount", "Payment amount must be greater than zero."));
        }

        var record = new PaymentRecord(
            id: Guid.NewGuid().ToString("D"),
            paymentNumber: paymentNumber,
            payableId: payableId,
            purchaseRequestId: purchaseRequestId,
            vendorId: vendorId,
            paymentAmount: paymentAmount,
            paymentMethod: paymentMethod,
            makerId: makerId,
            idempotencyKey: idempotencyKey,
            nowUtc: nowUtc);

        return Result.Success(record);
    }

    public Result Verify(string verifierId, DateTimeOffset nowUtc)
    {
        Result sodCheck = FourEyesRule.EnsureDistinctDisbursementActors(MakerId, verifierId: verifierId);
        if (sodCheck.IsFailure)
        {
            return Result.Failure(sodCheck.Error);
        }

        VerifierId = verifierId;
        VerifiedAtUtc = nowUtc;
        Status = PaymentRecordStatus.Verified;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result BankApprove(string bankApproverId, DateTimeOffset nowUtc)
    {
        Result sodCheck = FourEyesRule.EnsureDistinctDisbursementActors(MakerId, verifierId: VerifierId, bankApproverId: bankApproverId);
        if (sodCheck.IsFailure)
        {
            return Result.Failure(sodCheck.Error);
        }

        BankApproverId = bankApproverId;
        BankApprovedAtUtc = nowUtc;
        Status = PaymentRecordStatus.BankApproved;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Execute(string executedById, string executionDate, BankReference bankRef, string? remittanceDocId, Money? executedAmount, DateTimeOffset nowUtc)
    {
        ExecutedById = Guard.AgainstNullOrWhiteSpace(executedById);
        ExecutionDate = Guard.AgainstNullOrWhiteSpace(executionDate);
        BankReference = bankRef ?? throw new ArgumentNullException(nameof(bankRef));
        RemittanceDocId = remittanceDocId;
        ExecutedAmount = executedAmount ?? PaymentAmount;
        ExecutedAtUtc = nowUtc;
        Status = PaymentRecordStatus.PaidUnverified;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Reconcile(string evidenceVerifierId, string? notes, DateTimeOffset nowUtc)
    {
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
        EvidenceVerifiedAtUtc = nowUtc;
        ReconciliationStatus = "matched";
        ReconciliationNotes = notes;
        Status = PaymentRecordStatus.Reconciled;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result SetExceptionState(PaymentRecordStatus targetState, string reason, DateTimeOffset nowUtc)
    {
        FailureReason = reason;
        Status = targetState;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

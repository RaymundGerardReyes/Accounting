using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Liquidation;

/// <summary>
/// Frozen immutable closing snapshot of an advance, its expenses, liquidations, and settlements.
/// </summary>
public sealed class ClosingSnapshot : Entity<string>
{
    public string AdvanceId { get; private set; }
    public Money ReleasedAmount { get; private set; }
    public Money ClaimedExpense { get; private set; }
    public Money ApprovedExpense { get; private set; }
    public Money RejectedExpense { get; private set; }
    public Money CashReturned { get; private set; }
    public Money Reimbursement { get; private set; }
    public Money FinalOutstanding { get; private set; }
    public string? AuthorizedExceptionId { get; private set; }
    public string ControllerId { get; private set; }
    public string ClosureReason { get; private set; }
    public string SnapshotJson { get; private set; }
    public DateTimeOffset ClosedAtUtc { get; private set; }

    public ClosingSnapshot(
        string id,
        string advanceId,
        Money releasedAmount,
        Money claimedExpense,
        Money approvedExpense,
        Money rejectedExpense,
        Money cashReturned,
        Money reimbursement,
        Money finalOutstanding,
        string? authorizedExceptionId,
        string controllerId,
        string closureReason,
        string snapshotJson,
        DateTimeOffset closedAtUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        AdvanceId = Guard.AgainstNullOrWhiteSpace(advanceId);
        ReleasedAmount = releasedAmount;
        ClaimedExpense = claimedExpense;
        ApprovedExpense = approvedExpense;
        RejectedExpense = rejectedExpense;
        CashReturned = cashReturned;
        Reimbursement = reimbursement;
        FinalOutstanding = finalOutstanding;
        AuthorizedExceptionId = authorizedExceptionId;
        ControllerId = Guard.AgainstNullOrWhiteSpace(controllerId);
        ClosureReason = Guard.AgainstNullOrWhiteSpace(closureReason);
        SnapshotJson = Guard.AgainstNullOrWhiteSpace(snapshotJson);
        ClosedAtUtc = closedAtUtc;
    }

    public static ClosingSnapshot Create(
        string advanceId,
        Money releasedAmount,
        Money claimedExpense,
        Money approvedExpense,
        Money rejectedExpense,
        Money cashReturned,
        Money reimbursement,
        Money finalOutstanding,
        string? authorizedExceptionId,
        string controllerId,
        string closureReason,
        string snapshotJson,
        DateTimeOffset closedAtUtc)
    {
        return new ClosingSnapshot(
            id: Guid.NewGuid().ToString("D"),
            advanceId: advanceId,
            releasedAmount: releasedAmount,
            claimedExpense: claimedExpense,
            approvedExpense: approvedExpense,
            rejectedExpense: rejectedExpense,
            cashReturned: cashReturned,
            reimbursement: reimbursement,
            finalOutstanding: finalOutstanding,
            authorizedExceptionId: authorizedExceptionId,
            controllerId: controllerId,
            closureReason: closureReason,
            snapshotJson: snapshotJson,
            closedAtUtc: closedAtUtc);
    }
}

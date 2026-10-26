using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.CashAdvances.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Ledger;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.CashAdvances.Close;

public sealed record CloseCashAdvanceCommand(
    string AdvanceId,
    string ActorId,
    string ClosureReason,
    string? AuthorizedExceptionId = null) : ICommand<CashAdvanceDto>;

public sealed class CloseHandler : ICommandHandler<CloseCashAdvanceCommand, CashAdvanceDto>
{
    private readonly ICashAdvanceRepository _repository;
    private readonly IAdvanceLedgerStore _ledgerStore;
    private readonly IClock _clock;

    public CloseHandler(
        ICashAdvanceRepository repository,
        IAdvanceLedgerStore ledgerStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(ledgerStore);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _ledgerStore = ledgerStore;
        _clock = clock;
    }

    public async Task<Result<CashAdvanceDto>> HandleAsync(
        CloseCashAdvanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ClosureReason) || command.ClosureReason.Trim().Length < 5)
        {
            return Result.Failure<CashAdvanceDto>(Error.Validation(
                "Finance.Close.InvalidReason",
                "A detailed Controller closure_reason is required."));
        }

        CashAdvance? advance = await _repository.GetByIdAsync(command.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is null)
        {
            return Result.Failure<CashAdvanceDto>(FinanceErrors.AdvanceNotFound(command.AdvanceId));
        }

        if (advance.SettlementStatus == SettlementStatus.Closed)
        {
            return Result.Failure<CashAdvanceDto>(Error.Conflict(
                "Finance.Close.AlreadyClosed",
                "Cash advance is already closed."));
        }

        if (advance.PaymentStatus != PaymentStatus.EvidenceVerified)
        {
            return Result.Failure<CashAdvanceDto>(Error.Conflict(
                "Finance.Close.NotReleased",
                "Cannot close an advance that has not been released with verified bank evidence."));
        }

        // SoD: Requestor / Beneficiary cannot close
        Result sodResult = FourEyesRule.EnsureRequestorIsNotApprover(advance.RequestorId, command.ActorId);
        if (sodResult.IsFailure)
        {
            return Result.Failure<CashAdvanceDto>(sodResult.Error);
        }
        if (string.Equals(advance.EmployeeId, command.ActorId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<CashAdvanceDto>(Error.SodViolation(
                "Finance.SoD.EmployeeCannotCloseOwnAdvance",
                "Beneficiary employee cannot authorize final closure of their own cash advance."));
        }

        IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(advance.Id, cancellationToken).ConfigureAwait(false);
        var balance = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, advance.RequestedAmount.CurrencyCode);

        if (balance.AmountMinor != 0 && string.IsNullOrWhiteSpace(command.AuthorizedExceptionId))
        {
            return Result.Failure<CashAdvanceDto>(Error.Conflict(
                "Finance.Close.NonZeroBalance",
                $"Cannot close cash advance with non-zero outstanding balance ({balance.AmountMinor} centavos) without an authorized exception ID."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result closeResult = advance.Close(command.ActorId, command.ClosureReason, nowUtc);
        if (closeResult.IsFailure)
        {
            return Result.Failure<CashAdvanceDto>(closeResult.Error);
        }

        // Freeze closing snapshot
        long totalDisbursed = entries.Where(e => e.EntryType == LedgerEntryType.DisbursementRelease).Sum(e => e.AmountMinor);
        long totalExpense = entries.Where(e => e.EntryType == LedgerEntryType.ExpenseApplied).Sum(e => e.AmountMinor);
        long totalReturn = entries.Where(e => e.EntryType == LedgerEntryType.CashReturn).Sum(e => e.AmountMinor);
        long totalReimbursement = entries.Where(e => e.EntryType == LedgerEntryType.ReimbursementSettled).Sum(e => e.AmountMinor);

        var zeroMoney = Money.Zero(advance.RequestedAmount.CurrencyCode);
        var snapshot = ClosingSnapshot.Create(
            advanceId: advance.Id,
            releasedAmount: new Money(totalDisbursed, advance.RequestedAmount.CurrencyCode),
            claimedExpense: new Money(totalExpense, advance.RequestedAmount.CurrencyCode),
            approvedExpense: new Money(totalExpense, advance.RequestedAmount.CurrencyCode),
            rejectedExpense: zeroMoney,
            cashReturned: new Money(totalReturn, advance.RequestedAmount.CurrencyCode),
            reimbursement: new Money(totalReimbursement, advance.RequestedAmount.CurrencyCode),
            finalOutstanding: balance,
            authorizedExceptionId: command.AuthorizedExceptionId,
            controllerId: command.ActorId,
            closureReason: command.ClosureReason,
            snapshotJson: "{}",
            closedAtUtc: nowUtc);

        await _ledgerStore.SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        await _repository.SaveAsync(advance, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateCashAdvanceHandler.MapToDto(advance));
    }
}

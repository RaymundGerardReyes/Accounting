using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Ledger;

namespace Rge.Finance.Application.CashAdvances.ReimbursementSettle;

public sealed record RecordReimbursementSettlementCommand(
    string AdvanceId,
    string ActorId,
    long SettledAmountMinor,
    string BankReference,
    string EvidenceDocumentId,
    string? Notes = null) : ICommand<ReimbursementSettlementResultDto>;

public sealed record ReimbursementSettlementResultDto(
    string LedgerEntryId,
    string AdvanceId,
    long SettledAmountMinor,
    long NewOutstandingBalanceMinor);

public sealed class ReimbursementSettleHandler : ICommandHandler<RecordReimbursementSettlementCommand, ReimbursementSettlementResultDto>
{
    private readonly ICashAdvanceRepository _repository;
    private readonly IAdvanceLedgerStore _ledgerStore;
    private readonly IClock _clock;

    public ReimbursementSettleHandler(
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

    public async Task<Result<ReimbursementSettlementResultDto>> HandleAsync(
        RecordReimbursementSettlementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CashAdvance? advance = await _repository.GetByIdAsync(command.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is null)
        {
            return Result.Failure<ReimbursementSettlementResultDto>(FinanceErrors.AdvanceNotFound(command.AdvanceId));
        }

        if (advance.SettlementStatus == SettlementStatus.Closed)
        {
            return Result.Failure<ReimbursementSettlementResultDto>(Error.Conflict(
                "Finance.Reimbursement.AlreadyClosed",
                "Cannot record reimbursement settlement on a closed cash advance."));
        }

        if (command.SettledAmountMinor <= 0)
        {
            return Result.Failure<ReimbursementSettlementResultDto>(Error.Validation(
                "Finance.Reimbursement.InvalidAmount",
                "Settled amount must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(command.BankReference))
        {
            return Result.Failure<ReimbursementSettlementResultDto>(Error.Validation(
                "Finance.Reimbursement.MissingBankReference",
                "Bank payment reference is required for employee reimbursement settlement."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        var settledMoney = new Money(command.SettledAmountMinor, advance.RequestedAmount.CurrencyCode);
        var entry = new AdvanceLedgerEntry(
            id: Guid.NewGuid().ToString("D"),
            advanceId: advance.Id,
            employeeId: advance.EmployeeId,
            entryType: LedgerEntryType.ReimbursementSettled,
            amount: settledMoney,
            signedAmountMinor: -command.SettledAmountMinor,
            createdByActorId: command.ActorId,
            occurredAtUtc: nowUtc);

        await _ledgerStore.SaveEntryAsync(entry, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(advance.Id, cancellationToken).ConfigureAwait(false);
        var newBalance = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, advance.RequestedAmount.CurrencyCode);

        return Result.Success(new ReimbursementSettlementResultDto(
            LedgerEntryId: entry.Id,
            AdvanceId: advance.Id,
            SettledAmountMinor: command.SettledAmountMinor,
            NewOutstandingBalanceMinor: newBalance.AmountMinor));
    }
}

using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Ledger;

namespace Rge.Finance.Application.CashAdvances.CashReturn;

public sealed record RecordCashReturnCommand(
    string AdvanceId,
    string ActorId,
    long ReturnedAmountMinor,
    string BankReference,
    string EvidenceDocumentId,
    string? Notes = null) : ICommand<CashReturnResultDto>;

public sealed record CashReturnResultDto(
    string LedgerEntryId,
    string AdvanceId,
    long ReturnedAmountMinor,
    long NewOutstandingBalanceMinor);

public sealed class CashReturnHandler : ICommandHandler<RecordCashReturnCommand, CashReturnResultDto>
{
    private readonly ICashAdvanceRepository _repository;
    private readonly IAdvanceLedgerStore _ledgerStore;
    private readonly IClock _clock;

    public CashReturnHandler(
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

    public async Task<Result<CashReturnResultDto>> HandleAsync(
        RecordCashReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CashAdvance? advance = await _repository.GetByIdAsync(command.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is null)
        {
            return Result.Failure<CashReturnResultDto>(FinanceErrors.AdvanceNotFound(command.AdvanceId));
        }

        if (advance.PaymentStatus != PaymentStatus.EvidenceVerified)
        {
            return Result.Failure<CashReturnResultDto>(Error.Conflict(
                "Finance.CashReturn.NotReleased",
                "Cannot record cash return on an advance that has not been released with verified bank evidence."));
        }

        if (advance.SettlementStatus == SettlementStatus.Closed)
        {
            return Result.Failure<CashReturnResultDto>(Error.Conflict(
                "Finance.CashReturn.AlreadyClosed",
                "Cannot record cash return on a closed cash advance."));
        }

        if (string.IsNullOrWhiteSpace(command.BankReference) || command.BankReference.Trim().Length < 3)
        {
            return Result.Failure<CashReturnResultDto>(Error.Validation(
                "Finance.CashReturn.InvalidBankReference",
                "Bank deposit or official receipt reference is required for cash return."));
        }

        if (string.IsNullOrWhiteSpace(command.EvidenceDocumentId))
        {
            return Result.Failure<CashReturnResultDto>(Error.Validation(
                "Finance.CashReturn.MissingEvidence",
                "Cash return deposit/receipt evidence document is required."));
        }

        IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(advance.Id, cancellationToken).ConfigureAwait(false);
        var currentBalance = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, advance.RequestedAmount.CurrencyCode);

        if (command.ReturnedAmountMinor <= 0 || command.ReturnedAmountMinor > currentBalance.AmountMinor)
        {
            return Result.Failure<CashReturnResultDto>(Error.Validation(
                "Finance.CashReturn.InvalidAmount",
                $"Returned amount ({command.ReturnedAmountMinor}) must be > 0 and <= current outstanding balance ({currentBalance.AmountMinor})."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        var returnMoney = new Money(command.ReturnedAmountMinor, advance.RequestedAmount.CurrencyCode);
        var entry = new AdvanceLedgerEntry(
            id: Guid.NewGuid().ToString("D"),
            advanceId: advance.Id,
            employeeId: advance.EmployeeId,
            entryType: LedgerEntryType.CashReturn,
            amount: returnMoney,
            signedAmountMinor: -command.ReturnedAmountMinor,
            createdByActorId: command.ActorId,
            occurredAtUtc: nowUtc);

        await _ledgerStore.SaveEntryAsync(entry, cancellationToken).ConfigureAwait(false);

        long newBalance = currentBalance.AmountMinor - command.ReturnedAmountMinor;
        if (newBalance == 0)
        {
            advance.TransitionSettlementStatus(SettlementStatus.FullySettled, nowUtc);
            await _repository.SaveAsync(advance, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(new CashReturnResultDto(
            LedgerEntryId: entry.Id,
            AdvanceId: advance.Id,
            ReturnedAmountMinor: command.ReturnedAmountMinor,
            NewOutstandingBalanceMinor: newBalance));
    }
}

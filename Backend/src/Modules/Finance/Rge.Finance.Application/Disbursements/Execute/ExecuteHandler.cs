using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Disbursements.Prepare;
using Rge.Finance.Application.GeneralLedger;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Ledger;

namespace Rge.Finance.Application.Disbursements.Execute;

public sealed record ExecuteDisbursementCommand(
    string DisbursementId,
    string ExecutorId,
    string BankReference,
    string? RemittanceDocumentId = null,
    long? ExecutedAmountMinor = null,
    bool IsSettledImmediatelyByBank = true) : ICommand<DisbursementDto>;

public sealed class ExecuteHandler : ICommandHandler<ExecuteDisbursementCommand, DisbursementDto>
{
    private readonly IDisbursementRepository _repository;
    private readonly ICashAdvanceRepository _advanceRepository;
    private readonly IAdvanceLedgerStore _ledgerStore;
    private readonly ISubledgerGlPostingService _glPostingService;
    private readonly IClock _clock;

    public ExecuteHandler(
        IDisbursementRepository repository,
        ICashAdvanceRepository advanceRepository,
        IAdvanceLedgerStore ledgerStore,
        ISubledgerGlPostingService glPostingService,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(advanceRepository);
        ArgumentNullException.ThrowIfNull(ledgerStore);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _advanceRepository = advanceRepository;
        _ledgerStore = ledgerStore;
        _glPostingService = glPostingService;
        _clock = clock;
    }

    public async Task<Result<DisbursementDto>> HandleAsync(
        ExecuteDisbursementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Disbursement? disbursement = await _repository.GetByIdAsync(command.DisbursementId, cancellationToken).ConfigureAwait(false);
        if (disbursement is null)
        {
            return Result.Failure<DisbursementDto>(FinanceErrors.DisbursementNotFound(command.DisbursementId));
        }

        var bankRef = new BankReference(command.BankReference);
        Money? executedMoney = command.ExecutedAmountMinor.HasValue
            ? new Money(command.ExecutedAmountMinor.Value, disbursement.PaymentAmount.CurrencyCode)
            : null;

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result execResult = disbursement.Execute(
            command.ExecutorId,
            bankRef,
            executedMoney,
            nowUtc,
            command.IsSettledImmediatelyByBank);
        if (execResult.IsFailure)
        {
            return Result.Failure<DisbursementDto>(execResult.Error);
        }

        // Recognize the employee advance receivable at bank execution (cut-off), not at evidence verification.
        CashAdvance? advance = await _advanceRepository.GetByIdAsync(disbursement.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is not null)
        {
            Result paymentTransition = advance.TransitionPaymentStatus(PaymentStatus.ExecutedUnverified, nowUtc);
            if (paymentTransition.IsFailure)
            {
                return Result.Failure<DisbursementDto>(paymentTransition.Error);
            }

            Money recognizedAmount = disbursement.ExecutedAmount ?? disbursement.PaymentAmount;
            var ledgerEntry = new AdvanceLedgerEntry(
                id: Guid.NewGuid().ToString("D"),
                advanceId: advance.Id,
                employeeId: advance.EmployeeId,
                entryType: LedgerEntryType.DisbursementRelease,
                amount: recognizedAmount,
                signedAmountMinor: recognizedAmount.AmountMinor,
                createdByActorId: command.ExecutorId,
                occurredAtUtc: nowUtc);

            await _ledgerStore.SaveEntryAsync(ledgerEntry, cancellationToken).ConfigureAwait(false);
            Result<GlJournalBatchRecord> glResult = await _glPostingService.PostAutomaticSubledgerBatchAsync(
                "CashAdvance",
                advance.Id,
                disbursement.DisbursementNumber,
                DateOnly.FromDateTime(nowUtc.UtcDateTime),
                "AUTO-GL-v1",
                "BIR-EOPT-RR7-2024",
                $"Cash advance bank disbursement executed ({disbursement.DisbursementNumber})",
                [
                    new GlJournalLineRecord(
                        Guid.Empty, Guid.Empty, 1, "1140", "Employee Advances Receivable",
                        recognizedAmount.AmountMinor, 0, advance.DepartmentId, advance.CostCenterId,
                        "CASH_ADVANCE", "NONE", "Advance receivable recognized at bank execution"),
                    new GlJournalLineRecord(
                        Guid.Empty, Guid.Empty, 2,
                        command.IsSettledImmediatelyByBank ? "1010" : "1015",
                        command.IsSettledImmediatelyByBank
                            ? "Cash in Bank - Operating PHP"
                            : "Payment in Transit Clearing",
                        0, recognizedAmount.AmountMinor, "DEPT-TREASURY", "CC-CORP-00",
                        "CASH_ADVANCE", "NONE",
                        command.IsSettledImmediatelyByBank
                            ? "Operating cash disbursement"
                            : "Disbursement awaiting bank settlement")
                ],
                cancellationToken).ConfigureAwait(false);
            if (glResult.IsFailure)
            {
                return Result.Failure<DisbursementDto>(glResult.Error);
            }
            await _advanceRepository.SaveAsync(advance, cancellationToken).ConfigureAwait(false);
        }

        await _repository.SaveAsync(disbursement, cancellationToken).ConfigureAwait(false);

        return Result.Success(PrepareHandler.MapToDto(disbursement));
    }
}

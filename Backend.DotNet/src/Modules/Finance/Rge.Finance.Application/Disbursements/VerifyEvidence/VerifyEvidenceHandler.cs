using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Disbursements.Prepare;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Ledger;

namespace Rge.Finance.Application.Disbursements.VerifyEvidence;

public sealed record VerifyEvidenceCommand(
    string DisbursementId,
    string EvidenceVerifierId,
    string EvidenceDocId,
    string? Notes = null) : ICommand<DisbursementDto>;

public sealed class VerifyEvidenceHandler : ICommandHandler<VerifyEvidenceCommand, DisbursementDto>
{
    private readonly IDisbursementRepository _disbursementRepository;
    private readonly ICashAdvanceRepository _advanceRepository;
    private readonly IAdvanceLedgerStore _ledgerStore;
    private readonly IClock _clock;

    public VerifyEvidenceHandler(
        IDisbursementRepository disbursementRepository,
        ICashAdvanceRepository advanceRepository,
        IAdvanceLedgerStore ledgerStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(disbursementRepository);
        ArgumentNullException.ThrowIfNull(advanceRepository);
        ArgumentNullException.ThrowIfNull(ledgerStore);
        ArgumentNullException.ThrowIfNull(clock);

        _disbursementRepository = disbursementRepository;
        _advanceRepository = advanceRepository;
        _ledgerStore = ledgerStore;
        _clock = clock;
    }

    public async Task<Result<DisbursementDto>> HandleAsync(
        VerifyEvidenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Disbursement? disbursement = await _disbursementRepository.GetByIdAsync(command.DisbursementId, cancellationToken).ConfigureAwait(false);
        if (disbursement is null)
        {
            return Result.Failure<DisbursementDto>(FinanceErrors.DisbursementNotFound(command.DisbursementId));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result verifyResult = disbursement.VerifyEvidence(command.EvidenceVerifierId, command.EvidenceDocId, command.Notes, nowUtc);
        if (verifyResult.IsFailure)
        {
            return Result.Failure<DisbursementDto>(verifyResult.Error);
        }

        // Post ledger entry to linked CashAdvance and transition its payment status to evidence_verified
        CashAdvance? advance = await _advanceRepository.GetByIdAsync(disbursement.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is not null)
        {
            var ledgerEntry = new AdvanceLedgerEntry(
                id: Guid.NewGuid().ToString("D"),
                advanceId: advance.Id,
                employeeId: advance.EmployeeId,
                entryType: LedgerEntryType.DisbursementRelease,
                amount: disbursement.PaymentAmount,
                signedAmountMinor: disbursement.PaymentAmount.AmountMinor,
                createdByActorId: command.EvidenceVerifierId,
                occurredAtUtc: nowUtc);

            await _ledgerStore.SaveEntryAsync(ledgerEntry, cancellationToken).ConfigureAwait(false);

            advance.TransitionPaymentStatus(PaymentStatus.EvidenceVerified, nowUtc);
            await _advanceRepository.SaveAsync(advance, cancellationToken).ConfigureAwait(false);
        }

        await _disbursementRepository.SaveAsync(disbursement, cancellationToken).ConfigureAwait(false);

        return Result.Success(PrepareHandler.MapToDto(disbursement));
    }
}

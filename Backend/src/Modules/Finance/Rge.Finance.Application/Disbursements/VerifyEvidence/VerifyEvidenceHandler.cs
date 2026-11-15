using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Disbursements.Prepare;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.CashAdvances.StateMachines;
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
    private readonly IClock _clock;

    public VerifyEvidenceHandler(
        IDisbursementRepository disbursementRepository,
        ICashAdvanceRepository advanceRepository,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(disbursementRepository);
        ArgumentNullException.ThrowIfNull(advanceRepository);
        ArgumentNullException.ThrowIfNull(clock);

        _disbursementRepository = disbursementRepository;
        _advanceRepository = advanceRepository;
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

        // Evidence verification clears the compliance requirement only; the cash disbursement
        // ledger entry is recognized once, at bank execution (see ExecuteHandler).
        CashAdvance? advance = await _advanceRepository.GetByIdAsync(disbursement.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is not null)
        {
            Result paymentTransition = advance.TransitionPaymentStatus(PaymentStatus.EvidenceVerified, nowUtc);
            if (paymentTransition.IsFailure)
            {
                return Result.Failure<DisbursementDto>(paymentTransition.Error);
            }

            await _advanceRepository.SaveAsync(advance, cancellationToken).ConfigureAwait(false);
        }

        await _disbursementRepository.SaveAsync(disbursement, cancellationToken).ConfigureAwait(false);

        return Result.Success(PrepareHandler.MapToDto(disbursement));
    }
}

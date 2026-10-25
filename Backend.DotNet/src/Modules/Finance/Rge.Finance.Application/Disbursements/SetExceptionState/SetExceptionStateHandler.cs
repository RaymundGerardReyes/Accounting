using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Disbursements.Prepare;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.Disbursements.SetExceptionState;

public sealed record SetDisbursementExceptionStateCommand(
    string DisbursementId,
    string ActorId,
    string TargetState,
    string Reason) : ICommand<DisbursementDto>;

public sealed class SetExceptionStateHandler : ICommandHandler<SetDisbursementExceptionStateCommand, DisbursementDto>
{
    private readonly IDisbursementRepository _repository;
    private readonly IClock _clock;

    public SetExceptionStateHandler(IDisbursementRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<DisbursementDto>> HandleAsync(
        SetDisbursementExceptionStateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Disbursement? disbursement = await _repository.GetByIdAsync(command.DisbursementId, cancellationToken).ConfigureAwait(false);
        if (disbursement is null)
        {
            return Result.Failure<DisbursementDto>(FinanceErrors.DisbursementNotFound(command.DisbursementId));
        }

        DisbursementStage targetStage = command.TargetState.Trim().ToLowerInvariant() switch
        {
            "failed" => DisbursementStage.Failed,
            "payment_unknown" => DisbursementStage.PaymentUnknown,
            "cancelled" => DisbursementStage.Cancelled,
            _ => DisbursementStage.Failed
        };

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result stateResult = disbursement.SetExceptionState(targetStage, command.Reason, nowUtc);
        if (stateResult.IsFailure)
        {
            return Result.Failure<DisbursementDto>(stateResult.Error);
        }

        await _repository.SaveAsync(disbursement, cancellationToken).ConfigureAwait(false);

        return Result.Success(PrepareHandler.MapToDto(disbursement));
    }
}

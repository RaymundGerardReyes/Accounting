using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Disbursements.Prepare;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.Disbursements.Verify;

public sealed record VerifyDisbursementCommand(
    string DisbursementId,
    string VerifierId,
    string? Notes = null) : ICommand<DisbursementDto>;

public sealed class VerifyHandler : ICommandHandler<VerifyDisbursementCommand, DisbursementDto>
{
    private readonly IDisbursementRepository _repository;
    private readonly IClock _clock;

    public VerifyHandler(IDisbursementRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<DisbursementDto>> HandleAsync(
        VerifyDisbursementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Disbursement? disbursement = await _repository.GetByIdAsync(command.DisbursementId, cancellationToken).ConfigureAwait(false);
        if (disbursement is null)
        {
            return Result.Failure<DisbursementDto>(FinanceErrors.DisbursementNotFound(command.DisbursementId));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result verifyResult = disbursement.Verify(command.VerifierId, command.Notes, nowUtc);
        if (verifyResult.IsFailure)
        {
            return Result.Failure<DisbursementDto>(verifyResult.Error);
        }

        await _repository.SaveAsync(disbursement, cancellationToken).ConfigureAwait(false);

        return Result.Success(PrepareHandler.MapToDto(disbursement));
    }
}

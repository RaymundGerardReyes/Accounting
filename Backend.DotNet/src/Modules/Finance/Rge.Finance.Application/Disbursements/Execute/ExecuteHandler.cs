using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Disbursements.Prepare;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.Disbursements.Execute;

public sealed record ExecuteDisbursementCommand(
    string DisbursementId,
    string ExecutorId,
    string BankReference,
    string? RemittanceDocumentId = null,
    long? ExecutedAmountMinor = null) : ICommand<DisbursementDto>;

public sealed class ExecuteHandler : ICommandHandler<ExecuteDisbursementCommand, DisbursementDto>
{
    private readonly IDisbursementRepository _repository;
    private readonly IClock _clock;

    public ExecuteHandler(IDisbursementRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
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

        var bankRef = new BankReference(command.BankReference, command.RemittanceDocumentId);
        Money? executedMoney = command.ExecutedAmountMinor.HasValue
            ? new Money(command.ExecutedAmountMinor.Value, disbursement.PaymentAmount.CurrencyCode)
            : null;

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result execResult = disbursement.Execute(command.ExecutorId, bankRef, executedMoney, nowUtc);
        if (execResult.IsFailure)
        {
            return Result.Failure<DisbursementDto>(execResult.Error);
        }

        await _repository.SaveAsync(disbursement, cancellationToken).ConfigureAwait(false);

        return Result.Success(PrepareHandler.MapToDto(disbursement));
    }
}

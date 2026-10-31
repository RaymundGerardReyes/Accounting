using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.CashAdvances.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.CashAdvances.Submit;

public sealed record SubmitCashAdvanceCommand(
    string AdvanceId,
    string ActorId) : ICommand<CashAdvanceDto>;

public sealed class SubmitHandler : ICommandHandler<SubmitCashAdvanceCommand, CashAdvanceDto>
{
    private readonly ICashAdvanceRepository _repository;
    private readonly IClock _clock;

    public SubmitHandler(ICashAdvanceRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<CashAdvanceDto>> HandleAsync(
        SubmitCashAdvanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CashAdvance? advance = await _repository.GetByIdAsync(command.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is null)
        {
            return Result.Failure<CashAdvanceDto>(FinanceErrors.AdvanceNotFound(command.AdvanceId));
        }

        Result submitResult = advance.Submit(_clock.UtcNow);
        if (submitResult.IsFailure)
        {
            return Result.Failure<CashAdvanceDto>(submitResult.Error);
        }

        await _repository.SaveAsync(advance, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateCashAdvanceHandler.MapToDto(advance));
    }
}

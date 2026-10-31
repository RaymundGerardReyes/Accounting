using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.PurchaseRequests.Create;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.PurchaseRequests.Submit;

public sealed record SubmitPurchaseRequestCommand(
    string RequestId,
    string ActorId) : ICommand<PurchaseRequestDto>;

public sealed class SubmitHandler : ICommandHandler<SubmitPurchaseRequestCommand, PurchaseRequestDto>
{
    private readonly IPurchaseRequestRepository _repository;
    private readonly IClock _clock;

    public SubmitHandler(IPurchaseRequestRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PurchaseRequestDto>> HandleAsync(
        SubmitPurchaseRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RequestId))
        {
            return Result.Failure<PurchaseRequestDto>(FinanceErrors.Validation("MissingRequestId", "Purchase Request ID is required."));
        }

        PurchaseRequest? pr = await _repository.GetByIdAsync(command.RequestId.Trim(), cancellationToken).ConfigureAwait(false);
        if (pr is null)
        {
            return Result.Failure<PurchaseRequestDto>(FinanceErrors.Validation("PrNotFound", $"Purchase Request '{command.RequestId}' was not found."));
        }

        Result submitResult = pr.Submit(_clock.UtcNow);
        if (submitResult.IsFailure)
        {
            return Result.Failure<PurchaseRequestDto>(submitResult.Error);
        }

        await _repository.SaveAsync(pr, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateHandler.MapToDto(pr));
    }
}

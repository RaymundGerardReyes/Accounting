using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.PurchaseRequests.Create;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.PurchaseRequests.Decide;

public sealed record DecidePurchaseRequestCommand(
    string RequestId,
    string Decision,
    string ActorId,
    long? ApprovedAmountMinor = null,
    string? Reason = null) : ICommand<PurchaseRequestDto>;

public sealed class DecideHandler : ICommandHandler<DecidePurchaseRequestCommand, PurchaseRequestDto>
{
    private readonly IPurchaseRequestRepository _repository;
    private readonly IClock _clock;

    public DecideHandler(IPurchaseRequestRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PurchaseRequestDto>> HandleAsync(
        DecidePurchaseRequestCommand command,
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

        DateTimeOffset nowUtc = _clock.UtcNow;

        if (string.Equals(command.Decision, "approved", StringComparison.OrdinalIgnoreCase))
        {
            long minor = command.ApprovedAmountMinor ?? pr.RequestedAmount.AmountMinor;
            var amount = new Money(minor, pr.RequestedAmount.CurrencyCode);

            Result approveResult = pr.Approve(command.ActorId, amount, nowUtc);
            if (approveResult.IsFailure)
            {
                return Result.Failure<PurchaseRequestDto>(approveResult.Error);
            }
        }
        else if (string.Equals(command.Decision, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            Result rejectResult = pr.Reject(command.ActorId, command.Reason ?? "Rejected by approver", nowUtc);
            if (rejectResult.IsFailure)
            {
                return Result.Failure<PurchaseRequestDto>(rejectResult.Error);
            }
        }
        else
        {
            return Result.Failure<PurchaseRequestDto>(FinanceErrors.Validation("InvalidDecision", $"Decision '{command.Decision}' must be 'approved' or 'rejected'."));
        }

        await _repository.SaveAsync(pr, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateHandler.MapToDto(pr));
    }
}

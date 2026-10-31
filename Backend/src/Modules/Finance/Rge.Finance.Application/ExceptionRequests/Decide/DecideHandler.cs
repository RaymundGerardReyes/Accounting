using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExceptionRequests.Submit;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.SecurityGuards;
using Rge.Finance.Domain.Approvals;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.ExceptionRequests.Decide;

public sealed record DecideExceptionRequestCommand(
    string ExceptionId,
    string ApproverId,
    IReadOnlyList<string> Roles,
    string Decision,
    long? ApprovedAmountMinor = null,
    string? Reason = null,
    DateTimeOffset? ExpiresAtUtc = null) : ICommand<ExceptionRequestDto>;

public sealed class DecideHandler : ICommandHandler<DecideExceptionRequestCommand, ExceptionRequestDto>
{
    private readonly IExceptionRequestRepository _repository;
    private readonly IClock _clock;

    public DecideHandler(IExceptionRequestRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<ExceptionRequestDto>> HandleAsync(
        DecideExceptionRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ExceptionRequest? exc = await _repository.GetByIdAsync(command.ExceptionId, cancellationToken).ConfigureAwait(false);
        if (exc is null)
        {
            return Result.Failure<ExceptionRequestDto>(Error.NotFound(
                "Finance.ExceptionRequest.NotFound",
                $"Exception request '{command.ExceptionId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        string decisionNorm = command.Decision.Trim().ToLowerInvariant();
        bool isApproved = decisionNorm == "approved";

        if (isApproved)
        {
            long targetMinor = command.ApprovedAmountMinor ?? exc.RequestedAmount.AmountMinor;
            Result<ApprovalMatrix> limitCheck = ApprovalLimitGuard.CheckApprovalLimit(
                command.ApproverId,
                command.Roles,
                "exception_request",
                "controller_approval",
                targetMinor);

            if (limitCheck.IsFailure)
            {
                return Result.Failure<ExceptionRequestDto>(limitCheck.Error);
            }
        }

        Money? approvedMoney = command.ApprovedAmountMinor.HasValue
            ? new Money(command.ApprovedAmountMinor.Value, exc.RequestedAmount.CurrencyCode)
            : null;

        Result decideResult = exc.Decide(
            approverId: command.ApproverId,
            approved: isApproved,
            reason: command.Reason,
            approvedAmount: approvedMoney,
            expiresAt: command.ExpiresAtUtc,
            nowUtc: nowUtc);

        if (decideResult.IsFailure)
        {
            return Result.Failure<ExceptionRequestDto>(decideResult.Error);
        }

        await _repository.SaveAsync(exc, cancellationToken).ConfigureAwait(false);

        return Result.Success(SubmitHandler.MapToDto(exc));
    }
}

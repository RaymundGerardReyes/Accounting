using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.CashAdvances.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.SecurityGuards;
using Rge.Finance.Domain.Approvals;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.CashAdvances.Decide;

public sealed record DecideCashAdvanceCommand(
    string AdvanceId,
    string ActorId,
    IReadOnlyList<string> ActorRoles,
    string Decision,
    long? ApprovedAmountMinor = null,
    string? Reason = null) : ICommand<CashAdvanceDto>;

public sealed class DecideHandler : ICommandHandler<DecideCashAdvanceCommand, CashAdvanceDto>
{
    private readonly ICashAdvanceRepository _repository;
    private readonly IClock _clock;

    public DecideHandler(ICashAdvanceRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<CashAdvanceDto>> HandleAsync(
        DecideCashAdvanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CashAdvance? advance = await _repository.GetByIdAsync(command.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is null)
        {
            return Result.Failure<CashAdvanceDto>(FinanceErrors.AdvanceNotFound(command.AdvanceId));
        }

        // SoD Four-Eyes Rule: Requestor / Employee cannot approve their own advance
        Result sodResult = FourEyesRule.EnsureRequestorIsNotApprover(advance.RequestorId, command.ActorId);
        if (sodResult.IsFailure)
        {
            return Result.Failure<CashAdvanceDto>(sodResult.Error);
        }
        if (string.Equals(advance.EmployeeId, command.ActorId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<CashAdvanceDto>(Error.SodViolation(
                "Finance.SoD.EmployeeCannotApproveSelf",
                "Beneficiary employee cannot approve their own cash advance."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        string decisionNorm = command.Decision.Trim().ToLowerInvariant();

        switch (decisionNorm)
        {
            case "approved":
            {
                long targetMinor = command.ApprovedAmountMinor ?? advance.RequestedAmount.AmountMinor;
                Result<ApprovalMatrix> limitCheck = ApprovalLimitGuard.CheckApprovalLimit(
                    command.ActorId,
                    command.ActorRoles,
                    "cash_advance",
                    "business_approval",
                    targetMinor,
                    advance.DepartmentId);

                if (limitCheck.IsFailure)
                {
                    return Result.Failure<CashAdvanceDto>(limitCheck.Error);
                }

                var approvedMoney = new Money(targetMinor, advance.RequestedAmount.CurrencyCode);
                Result approveResult = advance.Approve(command.ActorId, approvedMoney, nowUtc);
                if (approveResult.IsFailure)
                {
                    return Result.Failure<CashAdvanceDto>(approveResult.Error);
                }
                break;
            }

            case "rejected":
            {
                string reason = string.IsNullOrWhiteSpace(command.Reason) ? "Rejected by business authority." : command.Reason.Trim();
                Result rejectResult = advance.Reject(command.ActorId, reason, nowUtc);
                if (rejectResult.IsFailure)
                {
                    return Result.Failure<CashAdvanceDto>(rejectResult.Error);
                }
                break;
            }

            case "returned":
            {
                string notes = string.IsNullOrWhiteSpace(command.Reason) ? "Returned for correction." : command.Reason.Trim();
                Result returnResult = advance.ReturnForCorrection(command.ActorId, notes, nowUtc);
                if (returnResult.IsFailure)
                {
                    return Result.Failure<CashAdvanceDto>(returnResult.Error);
                }
                break;
            }

            case "cancelled":
            {
                string reason = string.IsNullOrWhiteSpace(command.Reason) ? "Cancelled by actor." : command.Reason.Trim();
                Result cancelResult = advance.Cancel(command.ActorId, reason, nowUtc);
                if (cancelResult.IsFailure)
                {
                    return Result.Failure<CashAdvanceDto>(cancelResult.Error);
                }
                break;
            }

            default:
                return Result.Failure<CashAdvanceDto>(Error.Validation(
                    "Finance.Advance.InvalidDecision",
                    $"Decision '{command.Decision}' is invalid. Must be 'approved', 'rejected', 'returned', or 'cancelled'."));
        }

        await _repository.SaveAsync(advance, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateCashAdvanceHandler.MapToDto(advance));
    }
}

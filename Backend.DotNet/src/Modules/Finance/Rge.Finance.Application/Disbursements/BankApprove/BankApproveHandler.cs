using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Disbursements.Prepare;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.SecurityGuards;
using Rge.Finance.Domain.Approvals;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.Disbursements.BankApprove;

public sealed record BankApproveDisbursementCommand(
    string DisbursementId,
    string BankApproverId,
    IReadOnlyList<string> Roles,
    string? Notes = null) : ICommand<DisbursementDto>;

public sealed class BankApproveHandler : ICommandHandler<BankApproveDisbursementCommand, DisbursementDto>
{
    private readonly IDisbursementRepository _repository;
    private readonly IClock _clock;

    public BankApproveHandler(IDisbursementRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<DisbursementDto>> HandleAsync(
        BankApproveDisbursementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Disbursement? disbursement = await _repository.GetByIdAsync(command.DisbursementId, cancellationToken).ConfigureAwait(false);
        if (disbursement is null)
        {
            return Result.Failure<DisbursementDto>(FinanceErrors.DisbursementNotFound(command.DisbursementId));
        }

        Result<ApprovalMatrix> limitCheck = ApprovalLimitGuard.CheckApprovalLimit(
            command.BankApproverId,
            command.Roles,
            "disbursement_bank",
            "bank_approval",
            disbursement.PaymentAmount.AmountMinor);

        if (limitCheck.IsFailure)
        {
            return Result.Failure<DisbursementDto>(limitCheck.Error);
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result approveResult = disbursement.BankApprove(command.BankApproverId, command.Notes, nowUtc);
        if (approveResult.IsFailure)
        {
            return Result.Failure<DisbursementDto>(approveResult.Error);
        }

        await _repository.SaveAsync(disbursement, cancellationToken).ConfigureAwait(false);

        return Result.Success(PrepareHandler.MapToDto(disbursement));
    }
}

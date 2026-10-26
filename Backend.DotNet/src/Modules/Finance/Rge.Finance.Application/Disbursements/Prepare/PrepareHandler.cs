using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.Disbursements.Prepare;

public sealed record PrepareDisbursementCommand(
    string AdvanceId,
    string MakerId,
    long PaymentAmountMinor,
    string PaymentMethod,
    string? RecipientAccountRef = null,
    string? IdempotencyKey = null) : ICommand<DisbursementDto>;

public sealed record DisbursementDto(
    string Id,
    string DisbursementNumber,
    string AdvanceId,
    long PaymentAmountMinor,
    string Currency,
    string PaymentMethod,
    string? RecipientAccountRef,
    string Status,
    string MakerId,
    string? VerifierId,
    string? BankApproverId,
    string? ExecutedById,
    string? EvidenceVerifierId,
    string? BankReference,
    DateTimeOffset CreatedAtUtc);

public sealed class PrepareHandler : ICommandHandler<PrepareDisbursementCommand, DisbursementDto>
{
    private readonly IDisbursementRepository _disbursementRepository;
    private readonly ICashAdvanceRepository _advanceRepository;
    private readonly IClock _clock;

    public PrepareHandler(
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
        PrepareDisbursementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CashAdvance? advance = await _advanceRepository.GetByIdAsync(command.AdvanceId, cancellationToken).ConfigureAwait(false);
        if (advance is null)
        {
            return Result.Failure<DisbursementDto>(FinanceErrors.AdvanceNotFound(command.AdvanceId));
        }

        if (advance.ApprovalStatus != ApprovalStatus.Approved)
        {
            return Result.Failure<DisbursementDto>(Error.Conflict(
                "Finance.Disbursement.AdvanceNotApproved",
                $"Cannot prepare disbursement for advance in '{advance.ApprovalStatus}' approval state. Must be 'approved'."));
        }

        // Maker cannot be the requestor or employee
        Result makerSod = FourEyesRule.EnsureRequestorIsNotApprover(advance.RequestorId, command.MakerId);
        if (makerSod.IsFailure)
        {
            return Result.Failure<DisbursementDto>(makerSod.Error);
        }
        if (string.Equals(advance.EmployeeId, command.MakerId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<DisbursementDto>(Error.SodViolation(
                "Finance.SoD.EmployeeCannotMakeDisbursement",
                "Beneficiary employee cannot prepare disbursement for their own cash advance."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        long seq = await _disbursementRepository.NextDisbursementSequenceAsync(cancellationToken).ConfigureAwait(false);
        string disbNo = $"DISB-{nowUtc:yyyyMM}-{seq:D4}";

        var amount = new Money(command.PaymentAmountMinor, advance.RequestedAmount.CurrencyCode);
        Result<Disbursement> disbResult = Disbursement.Prepare(
            disbursementNumber: disbNo,
            advanceId: advance.Id,
            paymentAmount: amount,
            paymentMethod: command.PaymentMethod,
            recipientAccountRef: command.RecipientAccountRef,
            makerId: command.MakerId,
            idempotencyKey: command.IdempotencyKey,
            nowUtc: nowUtc);

        if (disbResult.IsFailure)
        {
            return Result.Failure<DisbursementDto>(disbResult.Error);
        }

        await _disbursementRepository.SaveAsync(disbResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(disbResult.Value));
    }

    public static DisbursementDto MapToDto(Disbursement d)
    {
        ArgumentNullException.ThrowIfNull(d);

        return new DisbursementDto(
            Id: d.Id,
            DisbursementNumber: d.DisbursementNumber,
            AdvanceId: d.AdvanceId,
            PaymentAmountMinor: d.PaymentAmount.AmountMinor,
            Currency: d.PaymentAmount.CurrencyCode,
            PaymentMethod: d.PaymentMethod,
            RecipientAccountRef: d.RecipientAccountRef,
            Status: d.Status.ToString().ToLowerInvariant(),
            MakerId: d.MakerId,
            VerifierId: d.VerifierId,
            BankApproverId: d.BankApproverId,
            ExecutedById: d.ExecutedById,
            EvidenceVerifierId: d.EvidenceVerifierId,
            BankReference: d.BankReference?.Value,
            CreatedAtUtc: d.CreatedAtUtc);
    }
}

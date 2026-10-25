using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;

namespace Rge.Finance.Application.CashAdvances.Create;

public sealed record CreateCashAdvanceCommand(
    string RequestorId,
    string EmployeeId,
    string DepartmentId,
    string CostCenterId,
    string PurposeCode,
    string BusinessJustification,
    string? DestinationOrSite,
    long RequestedAmountMinor,
    string Currency,
    string DateNeeded,
    string? ActivityStartDate = null,
    string? ActivityEndDate = null,
    string? Remarks = null,
    IReadOnlyList<string>? AttachmentIds = null,
    string? IdempotencyKey = null) : ICommand<CashAdvanceDto>;

public sealed record CashAdvanceDto(
    string Id,
    string AdvanceNumber,
    string RequestorId,
    string EmployeeId,
    string DepartmentId,
    string CostCenterId,
    string PurposeCode,
    string BusinessJustification,
    string? DestinationOrSite,
    long RequestedAmountMinor,
    long? ApprovedAmountMinor,
    string Currency,
    string DateNeeded,
    string? ActivityStartDate,
    string? ActivityEndDate,
    string? LiquidationDueDate,
    string? Remarks,
    IReadOnlyList<string> AttachmentIds,
    string ApprovalStatus,
    string PaymentStatus,
    string LiquidationReviewStatus,
    string SettlementStatus,
    string PostingStatus,
    string OverallStage,
    DateTimeOffset CreatedAtUtc);

public sealed class CreateCashAdvanceHandler : ICommandHandler<CreateCashAdvanceCommand, CashAdvanceDto>
{
    private readonly ICashAdvanceRepository _repository;
    private readonly IClock _clock;

    public CreateCashAdvanceHandler(ICashAdvanceRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<CashAdvanceDto>> HandleAsync(
        CreateCashAdvanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;
        long seq = await _repository.NextAdvanceSequenceAsync(cancellationToken).ConfigureAwait(false);
        var advanceNo = AdvanceNumber.Create(nowUtc, seq);
        var purpose = new PurposeCode(command.PurposeCode);
        var amount = new Money(command.RequestedAmountMinor, command.Currency);

        Result<CashAdvance> advanceResult = CashAdvance.Create(
            advanceNumber: advanceNo,
            requestorId: command.RequestorId,
            employeeId: command.EmployeeId,
            departmentId: command.DepartmentId,
            costCenterId: command.CostCenterId,
            purposeCode: purpose,
            businessJustification: command.BusinessJustification,
            destinationOrSite: command.DestinationOrSite,
            requestedAmount: amount,
            dateNeeded: command.DateNeeded,
            activityStartDate: command.ActivityStartDate,
            activityEndDate: command.ActivityEndDate,
            liquidationDueDate: null,
            remarks: command.Remarks,
            attachmentIds: command.AttachmentIds,
            idempotencyKey: command.IdempotencyKey,
            nowUtc: nowUtc);

        if (advanceResult.IsFailure)
        {
            return Result.Failure<CashAdvanceDto>(advanceResult.Error);
        }

        await _repository.SaveAsync(advanceResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(advanceResult.Value));
    }

    public static CashAdvanceDto MapToDto(CashAdvance a)
    {
        ArgumentNullException.ThrowIfNull(a);

        return new CashAdvanceDto(
            Id: a.Id,
            AdvanceNumber: a.AdvanceNumber.Value,
            RequestorId: a.RequestorId,
            EmployeeId: a.EmployeeId,
            DepartmentId: a.DepartmentId,
            CostCenterId: a.CostCenterId,
            PurposeCode: a.PurposeCode.Value,
            BusinessJustification: a.BusinessJustification,
            DestinationOrSite: a.DestinationOrSite,
            RequestedAmountMinor: a.RequestedAmount.AmountMinor,
            ApprovedAmountMinor: a.ApprovedAmount?.AmountMinor,
            Currency: a.RequestedAmount.CurrencyCode,
            DateNeeded: a.DateNeeded,
            ActivityStartDate: a.ActivityStartDate,
            ActivityEndDate: a.ActivityEndDate,
            LiquidationDueDate: a.LiquidationDueDate,
            Remarks: a.Remarks,
            AttachmentIds: a.AttachmentIds,
            ApprovalStatus: a.ApprovalStatus.ToString().ToLowerInvariant(),
            PaymentStatus: a.PaymentStatus.ToString().ToLowerInvariant(),
            LiquidationReviewStatus: a.LiquidationReviewStatus.ToString().ToLowerInvariant(),
            SettlementStatus: a.SettlementStatus.ToString().ToLowerInvariant(),
            PostingStatus: a.PostingStatus.ToString().ToLowerInvariant(),
            OverallStage: a.DeriveOverallStage(),
            CreatedAtUtc: a.CreatedAtUtc);
    }
}

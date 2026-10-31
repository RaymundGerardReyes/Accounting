using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.ExceptionRequests.Submit;

public sealed record SubmitExceptionRequestCommand(
    string EmployeeId,
    string RequesterId,
    string TargetEntityType,
    string? TargetEntityId,
    string ReportingPeriod,
    string PolicyVersionId,
    string ViolatedRuleCode,
    string ExceptionType,
    long RequestedAmountMinor,
    string Currency,
    string BusinessJustification,
    IReadOnlyList<string>? SupportingDocumentIds = null) : ICommand<ExceptionRequestDto>;

public sealed record ExceptionRequestDto(
    string Id,
    string ExceptionNumber,
    string EmployeeId,
    string RequesterId,
    string TargetEntityType,
    string? TargetEntityId,
    string ReportingPeriod,
    string PolicyVersionId,
    string ViolatedRuleCode,
    string ExceptionType,
    long RequestedAmountMinor,
    long? ApprovedMaxAmountMinor,
    string Currency,
    string BusinessJustification,
    string Status,
    string? ApproverId,
    DateTimeOffset CreatedAtUtc);

public sealed class SubmitHandler : ICommandHandler<SubmitExceptionRequestCommand, ExceptionRequestDto>
{
    private readonly IExceptionRequestRepository _repository;
    private readonly IClock _clock;

    public SubmitHandler(IExceptionRequestRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<ExceptionRequestDto>> HandleAsync(
        SubmitExceptionRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;
        long seq = await _repository.NextExceptionSequenceAsync(cancellationToken).ConfigureAwait(false);
        string excNo = $"EXC-{nowUtc:yyyyMM}-{seq:D4}";

        var amount = new Money(command.RequestedAmountMinor, command.Currency);
        Result<ExceptionRequest> createResult = ExceptionRequest.Create(
            exceptionNumber: excNo,
            employeeId: command.EmployeeId,
            requesterId: command.RequesterId,
            targetEntityType: command.TargetEntityType,
            targetEntityId: command.TargetEntityId,
            reportingPeriod: command.ReportingPeriod,
            policyVersionId: command.PolicyVersionId,
            violatedRuleCode: command.ViolatedRuleCode,
            exceptionType: command.ExceptionType,
            requestedAmount: amount,
            businessJustification: command.BusinessJustification,
            supportingDocumentIds: command.SupportingDocumentIds,
            nowUtc: nowUtc);

        if (createResult.IsFailure)
        {
            return Result.Failure<ExceptionRequestDto>(createResult.Error);
        }

        ExceptionRequest exc = createResult.Value;
        exc.Submit(nowUtc);

        await _repository.SaveAsync(exc, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(exc));
    }

    public static ExceptionRequestDto MapToDto(ExceptionRequest e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return new ExceptionRequestDto(
            Id: e.Id,
            ExceptionNumber: e.ExceptionNumber,
            EmployeeId: e.EmployeeId,
            RequesterId: e.RequesterId,
            TargetEntityType: e.TargetEntityType,
            TargetEntityId: e.TargetEntityId,
            ReportingPeriod: e.ReportingPeriod,
            PolicyVersionId: e.PolicyVersionId,
            ViolatedRuleCode: e.ViolatedRuleCode,
            ExceptionType: e.ExceptionType,
            RequestedAmountMinor: e.RequestedAmount.AmountMinor,
            ApprovedMaxAmountMinor: e.ApprovedMaxAmount?.AmountMinor,
            Currency: e.RequestedAmount.CurrencyCode,
            BusinessJustification: e.BusinessJustification,
            Status: e.Status.ToString().ToLowerInvariant(),
            ApproverId: e.ApproverId,
            CreatedAtUtc: e.CreatedAtUtc);
    }
}

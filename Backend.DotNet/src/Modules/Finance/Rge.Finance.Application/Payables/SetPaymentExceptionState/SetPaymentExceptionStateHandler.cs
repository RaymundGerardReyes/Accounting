using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Payables.PreparePayment;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.Payables.SetPaymentExceptionState;

public sealed record SetPaymentExceptionStateCommand(
    string PaymentId,
    string TargetState,
    string Reason) : ICommand<PaymentRecordDto>;

public sealed class SetPaymentExceptionStateHandler : ICommandHandler<SetPaymentExceptionStateCommand, PaymentRecordDto>
{
    private readonly IPaymentRecordRepository _repository;
    private readonly IClock _clock;

    public SetPaymentExceptionStateHandler(IPaymentRecordRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PaymentRecordDto>> HandleAsync(
        SetPaymentExceptionStateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PaymentId))
        {
            return Result.Failure<PaymentRecordDto>(FinanceErrors.Validation("MissingPaymentId", "Payment ID is required."));
        }

        PaymentRecord? record = await _repository.GetByIdAsync(command.PaymentId.Trim(), cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Failure<PaymentRecordDto>(FinanceErrors.Validation("PaymentRecordNotFound", $"Payment record '{command.PaymentId}' was not found."));
        }

        PaymentRecordStatus targetStatus = command.TargetState?.Trim().ToLowerInvariant() switch
        {
            "failed" => PaymentRecordStatus.Failed,
            "payment_unknown" => PaymentRecordStatus.PaymentUnknown,
            "cancelled" => PaymentRecordStatus.Cancelled,
            _ => PaymentRecordStatus.Failed
        };

        Result exceptionResult = record.SetExceptionState(targetStatus, command.Reason, _clock.UtcNow);
        if (exceptionResult.IsFailure)
        {
            return Result.Failure<PaymentRecordDto>(exceptionResult.Error);
        }

        await _repository.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        return Result.Success(PreparePaymentHandler.MapToDto(record));
    }
}

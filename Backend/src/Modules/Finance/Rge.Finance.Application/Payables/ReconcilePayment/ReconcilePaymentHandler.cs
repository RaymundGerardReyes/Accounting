using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Payables.PreparePayment;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.Payables.ReconcilePayment;

public sealed record ReconcilePaymentCommand(
    string PaymentId,
    string EvidenceVerifierId,
    string? Notes = null) : ICommand<PaymentRecordDto>;

public sealed class ReconcilePaymentHandler : ICommandHandler<ReconcilePaymentCommand, PaymentRecordDto>
{
    private readonly IPaymentRecordRepository _repository;
    private readonly IClock _clock;

    public ReconcilePaymentHandler(IPaymentRecordRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PaymentRecordDto>> HandleAsync(
        ReconcilePaymentCommand command,
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

        Result reconcileResult = record.Reconcile(command.EvidenceVerifierId, command.Notes, _clock.UtcNow);
        if (reconcileResult.IsFailure)
        {
            return Result.Failure<PaymentRecordDto>(reconcileResult.Error);
        }

        await _repository.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        return Result.Success(PreparePaymentHandler.MapToDto(record));
    }
}

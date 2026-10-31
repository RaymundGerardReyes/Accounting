using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Payables.PreparePayment;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.Payables.ExecutePayment;

public sealed record ExecutePaymentCommand(
    string PaymentId,
    string ExecutedById,
    string ExecutionDate,
    string BankReference,
    string? RemittanceDocId = null,
    long? ExecutedAmountMinor = null) : ICommand<PaymentRecordDto>;

public sealed class ExecutePaymentHandler : ICommandHandler<ExecutePaymentCommand, PaymentRecordDto>
{
    private readonly IPaymentRecordRepository _paymentRepository;
    private readonly IAccountPayableRepository _payableRepository;
    private readonly IClock _clock;

    public ExecutePaymentHandler(
        IPaymentRecordRepository paymentRepository,
        IAccountPayableRepository payableRepository,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(paymentRepository);
        ArgumentNullException.ThrowIfNull(payableRepository);
        ArgumentNullException.ThrowIfNull(clock);

        _paymentRepository = paymentRepository;
        _payableRepository = payableRepository;
        _clock = clock;
    }

    public async Task<Result<PaymentRecordDto>> HandleAsync(
        ExecutePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PaymentId))
        {
            return Result.Failure<PaymentRecordDto>(FinanceErrors.Validation("MissingPaymentId", "Payment ID is required."));
        }

        PaymentRecord? record = await _paymentRepository.GetByIdAsync(command.PaymentId.Trim(), cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Failure<PaymentRecordDto>(FinanceErrors.Validation("PaymentRecordNotFound", $"Payment record '{command.PaymentId}' was not found."));
        }

        if (string.IsNullOrWhiteSpace(command.BankReference))
        {
            return Result.Failure<PaymentRecordDto>(FinanceErrors.Validation("MissingBankReference", "Bank reference is required."));
        }

        var bankRef = new BankReference(command.BankReference);
        DateTimeOffset nowUtc = _clock.UtcNow;
        Money? executedAmt = command.ExecutedAmountMinor.HasValue
            ? new Money(command.ExecutedAmountMinor.Value, record.PaymentAmount.CurrencyCode)
            : null;

        Result execResult = record.Execute(
            command.ExecutedById,
            command.ExecutionDate,
            bankRef,
            command.RemittanceDocId,
            executedAmt,
            nowUtc);

        if (execResult.IsFailure)
        {
            return Result.Failure<PaymentRecordDto>(execResult.Error);
        }

        await _paymentRepository.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        // Update payable status
        AccountPayable? ap = await _payableRepository.GetByIdAsync(record.PayableId, cancellationToken).ConfigureAwait(false);
        if (ap is not null)
        {
            ap.MarkPaid(nowUtc);
            await _payableRepository.SaveAsync(ap, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(PreparePaymentHandler.MapToDto(record));
    }
}

using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.Payables.PreparePayment;

public sealed record PreparePaymentCommand(
    string PayableId,
    long PaymentAmountMinor,
    string PaymentMethod,
    string MakerId,
    string Currency = "PHP",
    string? IdempotencyKey = null) : ICommand<PaymentRecordDto>;

public sealed record PaymentRecordDto(
    string Id,
    string PaymentNumber,
    string PayableId,
    string PurchaseRequestId,
    string VendorId,
    long PaymentAmountMinor,
    long? ExecutedAmountMinor,
    string Currency,
    string PaymentMethod,
    string Status,
    string MakerId,
    DateTimeOffset PreparedAtUtc,
    string? VerifierId,
    DateTimeOffset? VerifiedAtUtc,
    string? BankApproverId,
    DateTimeOffset? BankApprovedAtUtc,
    string? ExecutedById,
    string? ExecutionDate,
    DateTimeOffset? ExecutedAtUtc,
    string? BankReference,
    string? RemittanceDocId,
    string? EvidenceVerifierId,
    DateTimeOffset? EvidenceVerifiedAtUtc,
    string ReconciliationStatus,
    string? ReconciliationNotes,
    string? FailureReason,
    DateTimeOffset CreatedAtUtc);

public sealed class PreparePaymentHandler : ICommandHandler<PreparePaymentCommand, PaymentRecordDto>
{
    private readonly IAccountPayableRepository _payableRepository;
    private readonly IPaymentRecordRepository _paymentRepository;
    private readonly IClock _clock;

    public PreparePaymentHandler(
        IAccountPayableRepository payableRepository,
        IPaymentRecordRepository paymentRepository,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(payableRepository);
        ArgumentNullException.ThrowIfNull(paymentRepository);
        ArgumentNullException.ThrowIfNull(clock);

        _payableRepository = payableRepository;
        _paymentRepository = paymentRepository;
        _clock = clock;
    }

    public async Task<Result<PaymentRecordDto>> HandleAsync(
        PreparePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PayableId))
        {
            return Result.Failure<PaymentRecordDto>(FinanceErrors.Validation("MissingPayableId", "Payable ID is required."));
        }

        AccountPayable? ap = await _payableRepository.GetByIdAsync(command.PayableId.Trim(), cancellationToken).ConfigureAwait(false);
        if (ap is null)
        {
            return Result.Failure<PaymentRecordDto>(FinanceErrors.Validation("PayableNotFound", $"Account payable '{command.PayableId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        long seq = await _paymentRepository.NextPaymentRecordSequenceAsync(cancellationToken).ConfigureAwait(false);
        string payNo = $"PAY-{nowUtc:yyyyMM}-{seq:D4}";

        var amount = new Money(command.PaymentAmountMinor, command.Currency);

        Result<PaymentRecord> prepareResult = PaymentRecord.Prepare(
            paymentNumber: payNo,
            payableId: ap.Id,
            purchaseRequestId: ap.PurchaseRequestId,
            vendorId: ap.VendorId,
            paymentAmount: amount,
            paymentMethod: command.PaymentMethod,
            makerId: command.MakerId,
            idempotencyKey: command.IdempotencyKey,
            nowUtc: nowUtc);

        if (prepareResult.IsFailure)
        {
            return Result.Failure<PaymentRecordDto>(prepareResult.Error);
        }

        await _paymentRepository.SaveAsync(prepareResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(prepareResult.Value));
    }

    public static PaymentRecordDto MapToDto(PaymentRecord p)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new PaymentRecordDto(
            Id: p.Id,
            PaymentNumber: p.PaymentNumber,
            PayableId: p.PayableId,
            PurchaseRequestId: p.PurchaseRequestId,
            VendorId: p.VendorId,
            PaymentAmountMinor: p.PaymentAmount.AmountMinor,
            ExecutedAmountMinor: p.ExecutedAmount?.AmountMinor,
            Currency: p.PaymentAmount.CurrencyCode,
            PaymentMethod: p.PaymentMethod,
            Status: p.Status.ToString().ToLowerInvariant(),
            MakerId: p.MakerId,
            PreparedAtUtc: p.PreparedAtUtc,
            VerifierId: p.VerifierId,
            VerifiedAtUtc: p.VerifiedAtUtc,
            BankApproverId: p.BankApproverId,
            BankApprovedAtUtc: p.BankApprovedAtUtc,
            ExecutedById: p.ExecutedById,
            ExecutionDate: p.ExecutionDate,
            ExecutedAtUtc: p.ExecutedAtUtc,
            BankReference: p.BankReference?.Value,
            RemittanceDocId: p.RemittanceDocId,
            EvidenceVerifierId: p.EvidenceVerifierId,
            EvidenceVerifiedAtUtc: p.EvidenceVerifiedAtUtc,
            ReconciliationStatus: p.ReconciliationStatus,
            ReconciliationNotes: p.ReconciliationNotes,
            FailureReason: p.FailureReason,
            CreatedAtUtc: p.CreatedAtUtc);
    }
}

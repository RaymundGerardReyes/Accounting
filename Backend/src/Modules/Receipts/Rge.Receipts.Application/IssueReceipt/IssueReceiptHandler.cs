using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;

namespace Rge.Receipts.Application.IssueReceipt;

public sealed record IssueReceiptCommand(
    string OrderId,
    string OrderNumber,
    string PaymentAttemptId,
    long AmountMinor,
    string Currency,
    string CustomerName,
    string CustomerEmail,
    string Description,
    string PaymentMethod) : ICommand<ReceiptDto>
{
    public IssueReceiptCommand(string paymentId) : this(
        OrderId: $"ord_{paymentId}",
        OrderNumber: $"ORD-{paymentId}",
        PaymentAttemptId: paymentId,
        AmountMinor: 0,
        Currency: "PHP",
        CustomerName: "Valued Customer",
        CustomerEmail: string.Empty,
        Description: "Official Receipt",
        PaymentMethod: "card")
    {
    }
}

public sealed record IssueReceiptResponse(ReceiptDto Receipt);

public sealed record ReceiptDto(
    string ReceiptId,
    string OrderId,
    string OrderNumber,
    string PaymentAttemptId,
    string OfficialReceiptNumber,
    string TreasuryAcknowledgementNumber,
    string IssuerName,
    string IssuerTin,
    string BirPermitNumber,
    string CustomerName,
    string CustomerEmail,
    string Description,
    string PaymentMethod,
    long GrossAmountMinor,
    long VatableSalesMinor,
    long VatAmountMinor,
    long VatExemptSalesMinor,
    long ZeroRatedSalesMinor,
    string Currency,
    string AuditDigest,
    DateTimeOffset IssuedAtUtc);

/// <summary>
/// Issues a collision-safe BIR Official Receipt (OR-YYYYMM-XXXX-HASH8) and Treasury Acknowledgement.
/// Idempotent: returns the existing receipt if already issued for the given payment or order.
/// </summary>
public class IssueReceiptHandler : ICommandHandler<IssueReceiptCommand, ReceiptDto>, IHandler<IssueReceiptCommand, IssueReceiptResponse>
{
    private readonly IReceiptRepository _repository;
    private readonly ICanonicalJsonSerializer _canonicalSerializer;
    private readonly IClock _clock;
    private readonly IReceiptEmailQueue? _emailQueue;

    public IssueReceiptHandler(
        IReceiptRepository repository,
        ICanonicalJsonSerializer canonicalSerializer,
        IClock clock)
        : this(repository, canonicalSerializer, clock, null)
    {
    }

    public IssueReceiptHandler(
        IReceiptRepository repository,
        ICanonicalJsonSerializer canonicalSerializer,
        IClock clock,
        IReceiptEmailQueue? emailQueue)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(canonicalSerializer);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _canonicalSerializer = canonicalSerializer;
        _clock = clock;
        _emailQueue = emailQueue;
    }

    public virtual async Task<Result<ReceiptDto>> HandleAsync(
        IssueReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        OfficialReceipt? existing = await _repository
            .GetByPaymentAttemptIdAsync(command.PaymentAttemptId, cancellationToken)
            .ConfigureAwait(false)
            ?? await _repository.GetByOrderIdAsync(command.OrderId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Success(MapToDto(existing));
        }

        DateTimeOffset issuedAtUtc = _clock.UtcNow;
        long sequence = await _repository.NextReceiptSequenceAsync(cancellationToken).ConfigureAwait(false);
        var grossMoney = new Money(command.AmountMinor, command.Currency);
        var vatBreakdown = VatBreakdown.FromVatInclusiveGross(grossMoney);

        var canonicalPayload = new
        {
            order_id = command.OrderId,
            order_number = command.OrderNumber,
            payment_attempt_id = command.PaymentAttemptId,
            gross_amount_minor = grossMoney.AmountMinor,
            vatable_sales_minor = vatBreakdown.VatableSales.AmountMinor,
            vat_amount_minor = vatBreakdown.VatAmount.AmountMinor,
            currency = grossMoney.CurrencyCode,
            issuer_tin = BirRmc77_2024Policy.IssuerTin,
            issued_at_utc = issuedAtUtc.ToString("O")
        };

        AuditDigest digest = _canonicalSerializer.ComputeReceiptDigest(canonicalPayload);

        Result<OfficialReceipt> receiptResult = OfficialReceipt.Issue(
            orderId: command.OrderId,
            orderNumber: command.OrderNumber,
            paymentAttemptId: command.PaymentAttemptId,
            sequenceNumber: sequence,
            customerName: command.CustomerName,
            customerEmail: command.CustomerEmail,
            description: command.Description,
            paymentMethod: command.PaymentMethod,
            grossAmount: grossMoney,
            digest: digest,
            issuedAtUtc: issuedAtUtc);

        if (receiptResult.IsFailure)
        {
            return Result.Failure<ReceiptDto>(receiptResult.Error);
        }

        await _repository.SaveAsync(receiptResult.Value, cancellationToken).ConfigureAwait(false);

        // Non-blocking in-memory channel write (< 0.02ms); never blocks receipt generation
        if (_emailQueue is not null && !string.IsNullOrWhiteSpace(receiptResult.Value.CustomerEmail))
        {
            await _emailQueue.EnqueueAsync(receiptResult.Value, forceResend: false, CancellationToken.None).ConfigureAwait(false);
        }

        return Result.Success(MapToDto(receiptResult.Value));
    }

    async Task<Result<IssueReceiptResponse>> IHandler<IssueReceiptCommand, IssueReceiptResponse>.HandleAsync(
        IssueReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var result = await HandleAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<IssueReceiptResponse>(result.Error);
        }
        return Result.Success(new IssueReceiptResponse(result.Value));
    }

    public static ReceiptDto MapToDto(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new ReceiptDto(
            ReceiptId: receipt.Id,
            OrderId: receipt.OrderId,
            OrderNumber: receipt.OrderNumber,
            PaymentAttemptId: receipt.PaymentAttemptId,
            OfficialReceiptNumber: receipt.ReceiptNumber.Value,
            TreasuryAcknowledgementNumber: receipt.Acknowledgement.AcknowledgementNumber,
            IssuerName: BirRmc77_2024Policy.IssuerRegisteredName,
            IssuerTin: BirRmc77_2024Policy.IssuerTin,
            BirPermitNumber: BirRmc77_2024Policy.BirPermitNumber,
            CustomerName: receipt.CustomerName,
            CustomerEmail: receipt.CustomerEmail,
            Description: receipt.Description,
            PaymentMethod: receipt.PaymentMethod,
            GrossAmountMinor: receipt.VatBreakdown.GrossAmount.AmountMinor,
            VatableSalesMinor: receipt.VatBreakdown.VatableSales.AmountMinor,
            VatAmountMinor: receipt.VatBreakdown.VatAmount.AmountMinor,
            VatExemptSalesMinor: receipt.VatBreakdown.VatExemptSales.AmountMinor,
            ZeroRatedSalesMinor: receipt.VatBreakdown.ZeroRatedSales.AmountMinor,
            Currency: receipt.VatBreakdown.GrossAmount.CurrencyCode,
            AuditDigest: receipt.Digest.Value,
            IssuedAtUtc: receipt.IssuedAtUtc);
    }
}


using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.Payables.Register;

public sealed record RegisterPayableCommand(
    string PurchaseRequestId,
    string VendorId,
    string InvoiceNumber,
    string InvoiceDate,
    string DueDate,
    long OriginalPayableMinor,
    string Currency = "PHP",
    long? CreditAdjustmentMinor = null,
    string? VatCode = null,
    long? VatAmountMinor = null,
    string? EwtCode = null,
    long? EwtAmountMinor = null,
    string? PaymentTerms = null,
    string? AccountingReference = null,
    string ValidatedById = "system") : ICommand<AccountPayableDto>;

public sealed record ListPayablesQuery(
    string? VendorId = null,
    string? Status = null,
    int Limit = 50) : IQuery<IReadOnlyList<AccountPayableDto>>;

public sealed record AccountPayableDto(
    string Id,
    string PayableNumber,
    string PurchaseRequestId,
    string VendorId,
    string InvoiceNumber,
    string NormalizedInvoiceNo,
    string InvoiceDate,
    string DueDate,
    long OriginalPayableMinor,
    long CreditAdjustmentMinor,
    string? VatCode,
    long VatAmountMinor,
    string? EwtCode,
    long EwtAmountMinor,
    long NetPayableMinor,
    string Currency,
    string TaxReviewStatus,
    bool DuplicateFlag,
    string? DuplicateReason,
    string? PaymentTerms,
    string? AccountingReference,
    string Status,
    string ValidatedById,
    DateTimeOffset CreatedAtUtc);

public sealed class RegisterHandler :
    ICommandHandler<RegisterPayableCommand, AccountPayableDto>,
    IQueryHandler<ListPayablesQuery, IReadOnlyList<AccountPayableDto>>
{
    private readonly IAccountPayableRepository _repository;
    private readonly IClock _clock;

    public RegisterHandler(IAccountPayableRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<AccountPayableDto>> HandleAsync(
        RegisterPayableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;

        // Check duplicate normalized invoice
        var normalized = new NormalizedInvoiceNumber(command.InvoiceNumber);
        AccountPayable? existing = await _repository.GetByNormalizedInvoiceAsync(
            command.VendorId,
            normalized.NormalizedValue,
            command.Currency,
            cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Failure<AccountPayableDto>(
                FinanceErrors.Validation("DuplicateInvoice", $"Invoice '{command.InvoiceNumber}' for vendor '{command.VendorId}' already registered as {existing.PayableNumber}."));
        }

        long seq = await _repository.NextPayableSequenceAsync(cancellationToken).ConfigureAwait(false);
        string payableNo = $"AP-{nowUtc:yyyyMM}-{seq:D4}";

        var orig = new Money(command.OriginalPayableMinor, command.Currency);
        Money? cred = command.CreditAdjustmentMinor.HasValue ? new Money(command.CreditAdjustmentMinor.Value, command.Currency) : null;
        Money? vat = command.VatAmountMinor.HasValue ? new Money(command.VatAmountMinor.Value, command.Currency) : null;
        Money? ewt = command.EwtAmountMinor.HasValue ? new Money(command.EwtAmountMinor.Value, command.Currency) : null;

        Result<AccountPayable> createResult = AccountPayable.Create(
            payableNumber: payableNo,
            purchaseRequestId: command.PurchaseRequestId,
            vendorId: command.VendorId,
            invoiceNumber: command.InvoiceNumber,
            invoiceDate: command.InvoiceDate,
            dueDate: command.DueDate,
            originalPayable: orig,
            creditAdjustment: cred,
            vatCode: command.VatCode,
            vatAmount: vat,
            ewtCode: command.EwtCode,
            ewtAmount: ewt,
            paymentTerms: command.PaymentTerms,
            accountingReference: command.AccountingReference,
            validatedById: command.ValidatedById,
            nowUtc: nowUtc);

        if (createResult.IsFailure)
        {
            return Result.Failure<AccountPayableDto>(createResult.Error);
        }

        await _repository.SaveAsync(createResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(createResult.Value));
    }

    public async Task<Result<IReadOnlyList<AccountPayableDto>>> HandleAsync(
        ListPayablesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int take = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 200);
        IReadOnlyList<AccountPayable> list = await _repository.ListAsync(query.VendorId, query.Status, take, cancellationToken).ConfigureAwait(false);

        var dtos = list.Select(MapToDto).ToList();
        return Result.Success<IReadOnlyList<AccountPayableDto>>(dtos);
    }

    public static AccountPayableDto MapToDto(AccountPayable ap)
    {
        ArgumentNullException.ThrowIfNull(ap);

        return new AccountPayableDto(
            Id: ap.Id,
            PayableNumber: ap.PayableNumber,
            PurchaseRequestId: ap.PurchaseRequestId,
            VendorId: ap.VendorId,
            InvoiceNumber: ap.InvoiceNumber,
            NormalizedInvoiceNo: ap.NormalizedInvoiceNo.NormalizedValue,
            InvoiceDate: ap.InvoiceDate,
            DueDate: ap.DueDate,
            OriginalPayableMinor: ap.OriginalPayable.AmountMinor,
            CreditAdjustmentMinor: ap.CreditAdjustment.AmountMinor,
            VatCode: ap.VatCode,
            VatAmountMinor: ap.VatAmount.AmountMinor,
            EwtCode: ap.EwtCode,
            EwtAmountMinor: ap.EwtAmount.AmountMinor,
            NetPayableMinor: ap.NetPayable.AmountMinor,
            Currency: ap.OriginalPayable.CurrencyCode,
            TaxReviewStatus: ap.TaxReviewStatus,
            DuplicateFlag: ap.DuplicateFlag,
            DuplicateReason: ap.DuplicateReason,
            PaymentTerms: ap.PaymentTerms,
            AccountingReference: ap.AccountingReference,
            Status: ap.Status.ToString().ToLowerInvariant(),
            ValidatedById: ap.ValidatedById,
            CreatedAtUtc: ap.CreatedAtUtc);
    }
}

using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.PurchaseRequests.Create;

public sealed record CreatePurchaseRequestCommand(
    string RequestorId,
    string VendorId,
    string PaymentCategory,
    bool IsRestrictedCategory,
    string BusinessPurpose,
    string DepartmentId,
    string CostCenterId,
    long RequestedAmountMinor,
    string RequestedPaymentDate,
    string Currency = "PHP",
    string? BudgetReference = null,
    string? DueDate = null,
    IReadOnlyList<string>? SupportingDocIds = null,
    string? InvoiceDocId = null,
    string? Remarks = null,
    string? IdempotencyKey = null) : ICommand<PurchaseRequestDto>;

public sealed record PurchaseRequestDto(
    string Id,
    string RequestNumber,
    string RequestorId,
    string VendorId,
    string PaymentCategory,
    bool IsRestrictedCategory,
    string BusinessPurpose,
    string DepartmentId,
    string CostCenterId,
    string? BudgetReference,
    long RequestedAmountMinor,
    long? ApprovedAmountMinor,
    string Currency,
    string RequestedPaymentDate,
    string? DueDate,
    string ApprovalStatus,
    string WorkflowStatus,
    IReadOnlyList<string> SupportingDocIds,
    string? InvoiceDocId,
    string? Remarks,
    DateTimeOffset CreatedAtUtc);

public sealed class CreateHandler : ICommandHandler<CreatePurchaseRequestCommand, PurchaseRequestDto>
{
    private readonly IPurchaseRequestRepository _repository;
    private readonly IClock _clock;

    public CreateHandler(IPurchaseRequestRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<PurchaseRequestDto>> HandleAsync(
        CreatePurchaseRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;
        long seq = await _repository.NextPrSequenceAsync(cancellationToken).ConfigureAwait(false);
        string prNo = $"PR-{nowUtc:yyyyMM}-{seq:D4}";

        var amount = new Money(command.RequestedAmountMinor, command.Currency);

        Result<PurchaseRequest> createResult = PurchaseRequest.Create(
            requestNumber: prNo,
            requestorId: command.RequestorId,
            vendorId: command.VendorId,
            paymentCategory: command.PaymentCategory,
            isRestrictedCategory: command.IsRestrictedCategory,
            businessPurpose: command.BusinessPurpose,
            departmentId: command.DepartmentId,
            costCenterId: command.CostCenterId,
            budgetReference: command.BudgetReference,
            requestedAmount: amount,
            requestedPaymentDate: command.RequestedPaymentDate,
            dueDate: command.DueDate,
            supportingDocIds: command.SupportingDocIds,
            invoiceDocId: command.InvoiceDocId,
            remarks: command.Remarks,
            idempotencyKey: command.IdempotencyKey,
            nowUtc: nowUtc);

        if (createResult.IsFailure)
        {
            return Result.Failure<PurchaseRequestDto>(createResult.Error);
        }

        await _repository.SaveAsync(createResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(createResult.Value));
    }

    public static PurchaseRequestDto MapToDto(PurchaseRequest pr)
    {
        ArgumentNullException.ThrowIfNull(pr);

        return new PurchaseRequestDto(
            Id: pr.Id,
            RequestNumber: pr.RequestNumber,
            RequestorId: pr.RequestorId,
            VendorId: pr.VendorId,
            PaymentCategory: pr.PaymentCategory,
            IsRestrictedCategory: pr.IsRestrictedCategory,
            BusinessPurpose: pr.BusinessPurpose,
            DepartmentId: pr.DepartmentId,
            CostCenterId: pr.CostCenterId,
            BudgetReference: pr.BudgetReference,
            RequestedAmountMinor: pr.RequestedAmount.AmountMinor,
            ApprovedAmountMinor: pr.ApprovedAmount?.AmountMinor,
            Currency: pr.RequestedAmount.CurrencyCode,
            RequestedPaymentDate: pr.RequestedPaymentDate,
            DueDate: pr.DueDate,
            ApprovalStatus: pr.ApprovalStatus.ToString().ToLowerInvariant(),
            WorkflowStatus: pr.WorkflowStatus,
            SupportingDocIds: pr.SupportingDocIds,
            InvoiceDocId: pr.InvoiceDocId,
            Remarks: pr.Remarks,
            CreatedAtUtc: pr.CreatedAtUtc);
    }
}

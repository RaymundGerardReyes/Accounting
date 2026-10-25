using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Vendors;

public enum BankChangeStatus
{
    PendingApproval,
    Approved,
    Rejected
}

/// <summary>
/// Entity tracking requested changes to a vendor's bank account credentials with dual approval.
/// </summary>
public sealed class VendorBankChange : Entity<string>
{
    public string VendorId { get; private set; }
    public string? PreviousBankRef { get; private set; }
    public string NewBankName { get; private set; }
    public MaskedBankAccount NewBankAccountMasked { get; private set; }
    public string NewBankAccountRef { get; private set; }
    public string? SupportingDocumentId { get; private set; }
    public string Reason { get; private set; }
    public BankChangeStatus Status { get; private set; }

    public string RequestedById { get; private set; }
    public string? ApprovedById { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public VendorBankChange(
        string id,
        string vendorId,
        string? previousBankRef,
        string newBankName,
        MaskedBankAccount newBankAccountMasked,
        string newBankAccountRef,
        string? supportingDocumentId,
        string reason,
        string requestedById,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        VendorId = Guard.AgainstNullOrWhiteSpace(vendorId);
        PreviousBankRef = previousBankRef;
        NewBankName = Guard.AgainstNullOrWhiteSpace(newBankName);
        NewBankAccountMasked = newBankAccountMasked ?? throw new ArgumentNullException(nameof(newBankAccountMasked));
        NewBankAccountRef = Guard.AgainstNullOrWhiteSpace(newBankAccountRef);
        SupportingDocumentId = supportingDocumentId;
        Reason = Guard.AgainstNullOrWhiteSpace(reason);
        RequestedById = Guard.AgainstNullOrWhiteSpace(requestedById);
        Status = BankChangeStatus.PendingApproval;
        RequestedAtUtc = nowUtc;
    }

    public static Result<VendorBankChange> Create(
        string vendorId,
        string? previousBankRef,
        string newBankName,
        string rawNewAccountNumber,
        string newBankAccountRef,
        string? supportingDocumentId,
        string reason,
        string requestedById,
        DateTimeOffset nowUtc)
    {
        var masked = new MaskedBankAccount(rawNewAccountNumber);

        var change = new VendorBankChange(
            id: Guid.NewGuid().ToString("D"),
            vendorId: vendorId,
            previousBankRef: previousBankRef,
            newBankName: newBankName,
            newBankAccountMasked: masked,
            newBankAccountRef: newBankAccountRef,
            supportingDocumentId: supportingDocumentId,
            reason: reason,
            requestedById: requestedById,
            nowUtc: nowUtc);

        return Result.Success(change);
    }

    public Result Decide(string approverId, bool approved, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        if (string.Equals(approverId, RequestedById, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FinanceErrors.SodViolation("INV-SOD-001", "Requestor cannot approve vendor bank detail change."));
        }

        ApprovedById = approverId;
        Status = approved ? BankChangeStatus.Approved : BankChangeStatus.Rejected;
        DecidedAtUtc = nowUtc;

        return Result.Success();
    }
}

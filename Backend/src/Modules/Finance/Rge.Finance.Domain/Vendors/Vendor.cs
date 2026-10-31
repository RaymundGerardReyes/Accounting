using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Vendors;

public enum VendorVerificationStatus
{
    PendingVerification,
    Verified,
    Suspended,
    Rejected
}

/// <summary>
/// Aggregate root representing an approved Vendor and payee master profile.
/// </summary>
public sealed class Vendor : AggregateRoot<string>
{
    public string VendorCode { get; private set; }
    public string RegisteredName { get; private set; }
    public string PayeeType { get; private set; }
    public string? Tin { get; private set; }
    public string? Address { get; private set; }
    public string? ContactPerson { get; private set; }
    public string? ContactEmail { get; private set; }
    public string PaymentMethod { get; private set; }
    public string? BankName { get; private set; }
    public MaskedBankAccount? BankAccountMasked { get; private set; }
    public string? BankAccountRef { get; private set; }
    public VendorVerificationStatus VerificationStatus { get; private set; }
    public string? KycDocumentId { get; private set; }

    public string CreatedById { get; private set; }
    public string? VerifiedById { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Vendor(
        string id,
        string vendorCode,
        string registeredName,
        string payeeType,
        string? tin,
        string? address,
        string? contactPerson,
        string? contactEmail,
        string paymentMethod,
        string? bankName,
        MaskedBankAccount? bankAccountMasked,
        string? bankAccountRef,
        string? kycDocumentId,
        string createdById,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        VendorCode = Guard.AgainstNullOrWhiteSpace(vendorCode).Trim().ToUpperInvariant();
        RegisteredName = Guard.AgainstNullOrWhiteSpace(registeredName);
        PayeeType = Guard.AgainstNullOrWhiteSpace(payeeType);
        Tin = tin;
        Address = address;
        ContactPerson = contactPerson;
        ContactEmail = contactEmail;
        PaymentMethod = Guard.AgainstNullOrWhiteSpace(paymentMethod);
        BankName = bankName;
        BankAccountMasked = bankAccountMasked;
        BankAccountRef = bankAccountRef;
        VerificationStatus = VendorVerificationStatus.PendingVerification;
        KycDocumentId = kycDocumentId;
        CreatedById = Guard.AgainstNullOrWhiteSpace(createdById);
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public static Result<Vendor> Create(
        string vendorCode,
        string registeredName,
        string payeeType,
        string? tin,
        string? address,
        string? contactPerson,
        string? contactEmail,
        string paymentMethod,
        string? bankName,
        string? rawAccountNumber,
        string? bankAccountRef,
        string? kycDocumentId,
        string createdById,
        DateTimeOffset nowUtc)
    {
        MaskedBankAccount? masked = !string.IsNullOrWhiteSpace(rawAccountNumber)
            ? new MaskedBankAccount(rawAccountNumber)
            : null;

        var vendor = new Vendor(
            id: Guid.NewGuid().ToString("D"),
            vendorCode: vendorCode,
            registeredName: registeredName,
            payeeType: payeeType,
            tin: tin,
            address: address,
            contactPerson: contactPerson,
            contactEmail: contactEmail,
            paymentMethod: paymentMethod,
            bankName: bankName,
            bankAccountMasked: masked,
            bankAccountRef: bankAccountRef,
            kycDocumentId: kycDocumentId,
            createdById: createdById,
            nowUtc: nowUtc);

        return Result.Success(vendor);
    }

    public Result Verify(string verifiedById, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(verifiedById);
        if (string.Equals(verifiedById, CreatedById, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FinanceErrors.SodViolation("INV-SOD-001", "Creator cannot verify vendor master record."));
        }

        VerificationStatus = VendorVerificationStatus.Verified;
        VerifiedById = verifiedById;
        VerifiedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public void UpdateBankDetails(string bankName, MaskedBankAccount maskedAccount, string bankAccountRef, DateTimeOffset nowUtc)
    {
        BankName = Guard.AgainstNullOrWhiteSpace(bankName);
        BankAccountMasked = maskedAccount ?? throw new ArgumentNullException(nameof(maskedAccount));
        BankAccountRef = Guard.AgainstNullOrWhiteSpace(bankAccountRef);
        UpdatedAtUtc = nowUtc;
    }
}

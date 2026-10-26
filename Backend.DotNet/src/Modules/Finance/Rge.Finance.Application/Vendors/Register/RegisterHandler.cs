using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.Vendors.Register;

public sealed record RegisterVendorCommand(
    string RegisteredName,
    string PayeeType,
    string PaymentMethod,
    string CreatedById,
    string? Tin = null,
    string? Address = null,
    string? ContactPerson = null,
    string? ContactEmail = null,
    string? BankName = null,
    string? RawAccountNumber = null,
    string? BankAccountRef = null,
    string? KycDocumentId = null) : ICommand<VendorDto>;

public sealed record VendorDto(
    string Id,
    string VendorCode,
    string RegisteredName,
    string PayeeType,
    string? Tin,
    string? Address,
    string? ContactPerson,
    string? ContactEmail,
    string PaymentMethod,
    string? BankName,
    string? MaskedAccountNumber,
    string VerificationStatus,
    string CreatedById,
    string? VerifiedById,
    DateTimeOffset CreatedAtUtc);

public sealed class RegisterHandler : ICommandHandler<RegisterVendorCommand, VendorDto>
{
    private readonly IVendorRepository _repository;
    private readonly IClock _clock;

    public RegisterHandler(IVendorRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<VendorDto>> HandleAsync(
        RegisterVendorCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;
        long seq = await _repository.NextVendorSequenceAsync(cancellationToken).ConfigureAwait(false);
        string vendorCode = $"VND-{seq:D5}";

        Result<Vendor> createResult = Vendor.Create(
            vendorCode: vendorCode,
            registeredName: command.RegisteredName,
            payeeType: command.PayeeType,
            tin: command.Tin,
            address: command.Address,
            contactPerson: command.ContactPerson,
            contactEmail: command.ContactEmail,
            paymentMethod: command.PaymentMethod,
            bankName: command.BankName,
            rawAccountNumber: command.RawAccountNumber,
            bankAccountRef: command.BankAccountRef,
            kycDocumentId: command.KycDocumentId,
            createdById: command.CreatedById,
            nowUtc: nowUtc);

        if (createResult.IsFailure)
        {
            return Result.Failure<VendorDto>(createResult.Error);
        }

        await _repository.SaveAsync(createResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(createResult.Value));
    }

    public static VendorDto MapToDto(Vendor v)
    {
        ArgumentNullException.ThrowIfNull(v);

        return new VendorDto(
            Id: v.Id,
            VendorCode: v.VendorCode,
            RegisteredName: v.RegisteredName,
            PayeeType: v.PayeeType,
            Tin: v.Tin,
            Address: v.Address,
            ContactPerson: v.ContactPerson,
            ContactEmail: v.ContactEmail,
            PaymentMethod: v.PaymentMethod,
            BankName: v.BankName,
            MaskedAccountNumber: v.BankAccountMasked?.MaskedValue,
            VerificationStatus: v.VerificationStatus.ToString().ToLowerInvariant(),
            CreatedById: v.CreatedById,
            VerifiedById: v.VerifiedById,
            CreatedAtUtc: v.CreatedAtUtc);
    }
}

using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.Vendors.RequestBankChange;

public sealed record RequestVendorBankChangeCommand(
    string VendorId,
    string NewBankName,
    string RawNewAccountNumber,
    string NewBankAccountRef,
    string Reason,
    string RequestorId,
    string? SupportingDocumentId = null) : ICommand<VendorBankChangeDto>;

public sealed record VendorBankChangeDto(
    string Id,
    string VendorId,
    string? PreviousBankRef,
    string NewBankName,
    string MaskedAccountNumber,
    string NewBankAccountRef,
    string Reason,
    string Status,
    string RequestedById,
    string? ApprovedById,
    DateTimeOffset RequestedAtUtc);

public sealed class RequestBankChangeHandler : ICommandHandler<RequestVendorBankChangeCommand, VendorBankChangeDto>
{
    private readonly IVendorRepository _repository;
    private readonly IClock _clock;

    public RequestBankChangeHandler(IVendorRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<VendorBankChangeDto>> HandleAsync(
        RequestVendorBankChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Vendor? vendor = await _repository.GetByIdAsync(command.VendorId, cancellationToken).ConfigureAwait(false);
        if (vendor is null)
        {
            return Result.Failure<VendorBankChangeDto>(Error.NotFound(
                "Finance.Vendor.NotFound",
                $"Vendor with ID '{command.VendorId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result<VendorBankChange> changeResult = VendorBankChange.Create(
            vendorId: vendor.Id,
            previousBankRef: vendor.BankAccountRef,
            newBankName: command.NewBankName,
            rawNewAccountNumber: command.RawNewAccountNumber,
            newBankAccountRef: command.NewBankAccountRef,
            supportingDocumentId: command.SupportingDocumentId,
            reason: command.Reason,
            requestedById: command.RequestorId,
            nowUtc: nowUtc);

        if (changeResult.IsFailure)
        {
            return Result.Failure<VendorBankChangeDto>(changeResult.Error);
        }

        await _repository.SaveBankChangeAsync(changeResult.Value, cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToDto(changeResult.Value));
    }

    public static VendorBankChangeDto MapToDto(VendorBankChange c)
    {
        ArgumentNullException.ThrowIfNull(c);

        return new VendorBankChangeDto(
            Id: c.Id,
            VendorId: c.VendorId,
            PreviousBankRef: c.PreviousBankRef,
            NewBankName: c.NewBankName,
            MaskedAccountNumber: c.NewBankAccountMasked.MaskedValue,
            NewBankAccountRef: c.NewBankAccountRef,
            Reason: c.Reason,
            Status: c.Status.ToString().ToLowerInvariant(),
            RequestedById: c.RequestedById,
            ApprovedById: c.ApprovedById,
            RequestedAtUtc: c.RequestedAtUtc);
    }
}

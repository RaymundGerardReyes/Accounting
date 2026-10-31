using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.Vendors.Register;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.Vendors.Verify;

public sealed record VerifyVendorCommand(
    string VendorId,
    string VerifierId) : ICommand<VendorDto>;

public sealed class VerifyHandler : ICommandHandler<VerifyVendorCommand, VendorDto>
{
    private readonly IVendorRepository _repository;
    private readonly IClock _clock;

    public VerifyHandler(IVendorRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<VendorDto>> HandleAsync(
        VerifyVendorCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Vendor? vendor = await _repository.GetByIdAsync(command.VendorId, cancellationToken).ConfigureAwait(false);
        if (vendor is null)
        {
            return Result.Failure<VendorDto>(Error.NotFound(
                "Finance.Vendor.NotFound",
                $"Vendor with ID '{command.VendorId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        Result verifyResult = vendor.Verify(command.VerifierId, nowUtc);
        if (verifyResult.IsFailure)
        {
            return Result.Failure<VendorDto>(verifyResult.Error);
        }

        await _repository.SaveAsync(vendor, cancellationToken).ConfigureAwait(false);

        return Result.Success(RegisterHandler.MapToDto(vendor));
    }
}

using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.Vendors.RequestBankChange;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.Vendors.DecideBankChange;

public sealed record DecideVendorBankChangeCommand(
    string ChangeId,
    string ApproverId,
    string Decision) : ICommand<VendorBankChangeDto>;

public sealed class DecideBankChangeHandler : ICommandHandler<DecideVendorBankChangeCommand, VendorBankChangeDto>
{
    private readonly IVendorRepository _repository;
    private readonly IClock _clock;

    public DecideBankChangeHandler(IVendorRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<VendorBankChangeDto>> HandleAsync(
        DecideVendorBankChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        VendorBankChange? change = await _repository.GetBankChangeByIdAsync(command.ChangeId, cancellationToken).ConfigureAwait(false);
        if (change is null)
        {
            return Result.Failure<VendorBankChangeDto>(Error.NotFound(
                "Finance.VendorBankChange.NotFound",
                $"Vendor bank change request '{command.ChangeId}' was not found."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        string decisionNorm = command.Decision.Trim().ToLowerInvariant();
        bool isApproved = decisionNorm == "approved";

        Result decideResult = change.Decide(command.ApproverId, isApproved, nowUtc);
        if (decideResult.IsFailure)
        {
            return Result.Failure<VendorBankChangeDto>(decideResult.Error);
        }

        if (isApproved)
        {
            Vendor? vendor = await _repository.GetByIdAsync(change.VendorId, cancellationToken).ConfigureAwait(false);
            if (vendor is not null)
            {
                vendor.UpdateBankDetails(change.NewBankName, change.NewBankAccountMasked, change.NewBankAccountRef, nowUtc);
                await _repository.SaveAsync(vendor, cancellationToken).ConfigureAwait(false);
            }
        }

        await _repository.SaveBankChangeAsync(change, cancellationToken).ConfigureAwait(false);

        return Result.Success(RequestBankChangeHandler.MapToDto(change));
    }
}

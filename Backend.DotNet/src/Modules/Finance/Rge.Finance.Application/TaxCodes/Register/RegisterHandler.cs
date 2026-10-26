using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.TaxCodes.List;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.TaxCodes.Register;

public sealed record RegisterTaxCodeCommand(
    string Code,
    string Description,
    string TaxType,
    long RateBps,
    bool RequiresBir2307 = false,
    bool CpaApproved = false,
    string? ApprovedById = null) : ICommand<TaxCodeDto>;

public sealed class RegisterHandler : ICommandHandler<RegisterTaxCodeCommand, TaxCodeDto>
{
    private readonly ITaxCodeRepository _repository;
    private readonly IClock _clock;

    public RegisterHandler(ITaxCodeRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<TaxCodeDto>> HandleAsync(
        RegisterTaxCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;
        var taxCode = new TaxCode(
            code: command.Code,
            description: command.Description,
            taxType: command.TaxType,
            rateBps: command.RateBps,
            requiresBir2307: command.RequiresBir2307,
            cpaApproved: command.CpaApproved,
            approvedById: command.ApprovedById,
            nowUtc: nowUtc);

        await _repository.SaveAsync(taxCode, cancellationToken).ConfigureAwait(false);

        return Result.Success(new TaxCodeDto(
            Code: taxCode.Code,
            Description: taxCode.Description,
            TaxType: taxCode.TaxType,
            RateBps: taxCode.RateBps,
            RequiresBir2307: taxCode.RequiresBir2307,
            CpaApproved: taxCode.CpaApproved,
            ApprovedById: taxCode.ApprovedById,
            CreatedAtUtc: taxCode.CreatedAtUtc));
    }
}

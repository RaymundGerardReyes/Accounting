using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.TaxCodes.List;

public sealed record ListTaxCodesQuery : IQuery<IReadOnlyList<TaxCodeDto>>;

public sealed record TaxCodeDto(
    string Code,
    string Description,
    string TaxType,
    long RateBps,
    bool RequiresBir2307,
    bool CpaApproved,
    string? ApprovedById,
    DateTimeOffset CreatedAtUtc);

public sealed class ListHandler : IQueryHandler<ListTaxCodesQuery, IReadOnlyList<TaxCodeDto>>
{
    private readonly ITaxCodeRepository _repository;

    public ListHandler(ITaxCodeRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<TaxCodeDto>>> HandleAsync(
        ListTaxCodesQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaxCode> taxCodes = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TaxCodeDto> dtos = taxCodes.Select(t => new TaxCodeDto(
            Code: t.Code,
            Description: t.Description,
            TaxType: t.TaxType,
            RateBps: t.RateBps,
            RequiresBir2307: t.RequiresBir2307,
            CpaApproved: t.CpaApproved,
            ApprovedById: t.ApprovedById,
            CreatedAtUtc: t.CreatedAtUtc)).ToList();

        return Result.Success(dtos);
    }
}

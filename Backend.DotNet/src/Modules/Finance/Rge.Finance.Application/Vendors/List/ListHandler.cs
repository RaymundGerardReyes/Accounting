using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.Vendors.Register;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.Vendors.List;

public sealed record ListVendorsQuery(
    string? Status = null,
    int Limit = 50) : IQuery<IReadOnlyList<VendorDto>>;

public sealed class ListHandler : IQueryHandler<ListVendorsQuery, IReadOnlyList<VendorDto>>
{
    private readonly IVendorRepository _repository;

    public ListHandler(IVendorRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<VendorDto>>> HandleAsync(
        ListVendorsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 200);
        IReadOnlyList<Vendor> vendors = await _repository.ListAsync(query.Status, limit, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<VendorDto> dtos = vendors.Select(RegisterHandler.MapToDto).ToList();
        return Result.Success(dtos);
    }
}

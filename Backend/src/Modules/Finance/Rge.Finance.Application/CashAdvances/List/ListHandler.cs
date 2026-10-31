using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.CashAdvances.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;

namespace Rge.Finance.Application.CashAdvances.List;

public sealed record ListCashAdvancesQuery(
    string? DepartmentId = null,
    string? Status = null,
    int Limit = 50) : IQuery<IReadOnlyList<CashAdvanceDto>>;

public sealed class ListHandler : IQueryHandler<ListCashAdvancesQuery, IReadOnlyList<CashAdvanceDto>>
{
    private readonly ICashAdvanceRepository _repository;

    public ListHandler(ICashAdvanceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<CashAdvanceDto>>> HandleAsync(
        ListCashAdvancesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int effectiveLimit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 200);
        IReadOnlyList<CashAdvance> advances = await _repository.ListAsync(
            query.DepartmentId,
            query.Status,
            effectiveLimit,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CashAdvanceDto> dtos = advances.Select(CreateCashAdvanceHandler.MapToDto).ToList();
        return Result.Success(dtos);
    }
}

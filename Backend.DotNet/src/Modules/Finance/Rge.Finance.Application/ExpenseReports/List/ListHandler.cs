using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExpenseReports.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.ExpenseReports.List;

public sealed record ListExpenseReportsQuery(
    string? EmployeeId = null,
    string? Status = null,
    int Limit = 50) : IQuery<IReadOnlyList<ExpenseReportDto>>;

public sealed class ListHandler : IQueryHandler<ListExpenseReportsQuery, IReadOnlyList<ExpenseReportDto>>
{
    private readonly IExpenseReportRepository _repository;

    public ListHandler(IExpenseReportRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ExpenseReportDto>>> HandleAsync(
        ListExpenseReportsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int take = query.Limit <= 0 ? 50 : Math.Min(query.Limit, 200);
        IReadOnlyList<ExpenseReport> list = await _repository.ListAsync(query.EmployeeId, query.Status, take, cancellationToken).ConfigureAwait(false);

        var dtos = list.Select(CreateHandler.MapToDto).ToList();
        return Result.Success<IReadOnlyList<ExpenseReportDto>>(dtos);
    }
}

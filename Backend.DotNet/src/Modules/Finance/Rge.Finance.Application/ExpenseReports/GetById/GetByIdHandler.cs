using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExpenseReports.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.ExpenseReports.GetById;

public sealed record GetExpenseReportByIdQuery(string Id) : IQuery<ExpenseReportDto>;

public sealed class GetByIdHandler : IQueryHandler<GetExpenseReportByIdQuery, ExpenseReportDto>
{
    private readonly IExpenseReportRepository _repository;

    public GetByIdHandler(IExpenseReportRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<ExpenseReportDto>> HandleAsync(
        GetExpenseReportByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Id))
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.Validation("MissingReportId", "Report ID is required."));
        }

        ExpenseReport? report = await _repository.GetByIdAsync(query.Id.Trim(), cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.ReportNotFound(query.Id));
        }

        return Result.Success(CreateHandler.MapToDto(report));
    }
}

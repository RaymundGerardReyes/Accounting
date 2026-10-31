using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExpenseReports.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.ExpenseReports.Patch;

public sealed record PatchExpenseReportCommand(
    string ReportId,
    string? BusinessPurposeSummary = null,
    string? Remarks = null,
    string? ConsolidatedReceiptDocId = null,
    string? MfarDocId = null) : ICommand<ExpenseReportDto>;

public sealed class PatchHandler : ICommandHandler<PatchExpenseReportCommand, ExpenseReportDto>
{
    private readonly IExpenseReportRepository _repository;
    private readonly IClock _clock;

    public PatchHandler(IExpenseReportRepository repository, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<ExpenseReportDto>> HandleAsync(
        PatchExpenseReportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ReportId))
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.Validation("MissingReportId", "Report ID is required."));
        }

        ExpenseReport? report = await _repository.GetByIdAsync(command.ReportId.Trim(), cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.ReportNotFound(command.ReportId));
        }

        Result updateResult = report.UpdateDraft(
            command.BusinessPurposeSummary,
            command.Remarks,
            command.ConsolidatedReceiptDocId,
            command.MfarDocId,
            _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<ExpenseReportDto>(updateResult.Error);
        }

        await _repository.SaveAsync(report, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateHandler.MapToDto(report));
    }
}

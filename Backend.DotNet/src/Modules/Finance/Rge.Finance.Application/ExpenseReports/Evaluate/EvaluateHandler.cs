using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Liquidation;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.ExpenseReports.Evaluate;

public sealed record EvaluateExpenseReportQuery(string ReportId) : IQuery<EvaluationPreviewDto>;

public sealed record EvaluationPreviewDto(
    string ReportId,
    bool IsAllowed,
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> Warnings);

public sealed class EvaluateHandler : IQueryHandler<EvaluateExpenseReportQuery, EvaluationPreviewDto>
{
    private readonly IExpenseReportRepository _reportRepository;
    private readonly IPolicyRepository _policyRepository;
    private readonly IClock _clock;

    public EvaluateHandler(
        IExpenseReportRepository reportRepository,
        IPolicyRepository policyRepository,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(reportRepository);
        ArgumentNullException.ThrowIfNull(policyRepository);
        ArgumentNullException.ThrowIfNull(clock);

        _reportRepository = reportRepository;
        _policyRepository = policyRepository;
        _clock = clock;
    }

    public async Task<Result<EvaluationPreviewDto>> HandleAsync(
        EvaluateExpenseReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        ExpenseReport? report = await _reportRepository.GetByIdAsync(query.ReportId, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<EvaluationPreviewDto>(FinanceErrors.ReportNotFound(query.ReportId));
        }

        PolicyVersion? activeVersion = await _policyRepository.GetActiveVersionAsync("EXPENSE_POLICY", cancellationToken).ConfigureAwait(false);
        PeriodCounter? counter = await _policyRepository.GetPeriodCounterAsync(report.EmployeeId, report.ReportingPeriod, "EXP_AGGREGATE", cancellationToken).ConfigureAwait(false);

        if (activeVersion is null)
        {
            return Result.Success(new EvaluationPreviewDto(
                ReportId: report.Id,
                IsAllowed: true,
                Violations: [],
                Warnings: ["No active financial policy configured; allowed by fallback."]));
        }

        PolicyEvaluationResult eval = PolicyEvaluator.EvaluateExpenseReport(report, activeVersion, counter, _clock.UtcNow);

        return Result.Success(new EvaluationPreviewDto(
            ReportId: report.Id,
            IsAllowed: eval.IsAllowed,
            Violations: eval.Violations,
            Warnings: eval.Warnings));
    }
}

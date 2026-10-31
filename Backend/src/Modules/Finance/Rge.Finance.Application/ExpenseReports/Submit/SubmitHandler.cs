using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExpenseReports.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Liquidation;
using Rge.Finance.Domain.Policy;

namespace Rge.Finance.Application.ExpenseReports.Submit;

public sealed record SubmitExpenseReportCommand(
    string ReportId,
    string ActorId) : ICommand<ExpenseReportDto>;

public sealed class SubmitHandler : ICommandHandler<SubmitExpenseReportCommand, ExpenseReportDto>
{
    private readonly IExpenseReportRepository _reportRepository;
    private readonly IPolicyRepository _policyRepository;
    private readonly IClock _clock;

    public SubmitHandler(
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

    public async Task<Result<ExpenseReportDto>> HandleAsync(
        SubmitExpenseReportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ExpenseReport? report = await _reportRepository.GetByIdAsync(command.ReportId, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.ReportNotFound(command.ReportId));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;

        // Policy evaluation
        PolicyVersion? activeVersion = await _policyRepository.GetActiveVersionAsync("EXPENSE_POLICY", cancellationToken).ConfigureAwait(false);
        if (activeVersion is not null)
        {
            PeriodCounter? counter = await _policyRepository.GetPeriodCounterAsync(report.EmployeeId, report.ReportingPeriod, "EXP_AGGREGATE", cancellationToken).ConfigureAwait(false);
            PolicyEvaluationResult eval = PolicyEvaluator.EvaluateExpenseReport(report, activeVersion, counter, nowUtc);
            if (!eval.IsAllowed)
            {
                return Result.Failure<ExpenseReportDto>(Error.Validation(
                    "Finance.Policy.EvaluationFailed",
                    $"Policy violations: {string.Join("; ", eval.Violations)}"));
            }
        }

        Result submitResult = report.Submit(nowUtc);
        if (submitResult.IsFailure)
        {
            return Result.Failure<ExpenseReportDto>(submitResult.Error);
        }

        await _reportRepository.SaveAsync(report, cancellationToken).ConfigureAwait(false);

        // Update period counter
        var totalClaimed = report.CalculateTotalClaimed();
        PeriodCounter periodCounter = await _policyRepository.GetPeriodCounterAsync(report.EmployeeId, report.ReportingPeriod, "EXP_AGGREGATE", cancellationToken).ConfigureAwait(false)
            ?? PeriodCounter.Create(report.EmployeeId, report.ReportingPeriod, "EXP_AGGREGATE", totalClaimed.CurrencyCode, nowUtc);

        periodCounter.Increment(totalClaimed, report.Id, nowUtc);
        await _policyRepository.SavePeriodCounterAsync(periodCounter, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateHandler.MapToDto(report));
    }
}

using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Domain.Policy;

public sealed record PolicyEvaluationResult(
    bool IsAllowed,
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Domain service that evaluates expense reports and financial requests against a policy version.
/// </summary>
public static class PolicyEvaluator
{
    public static PolicyEvaluationResult EvaluateExpenseReport(
        ExpenseReport report,
        PolicyVersion policy,
        PeriodCounter? currentPeriodCounter,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(policy);

        var violations = new List<string>();
        var warnings = new List<string>();

        // 1. Max Reports Per Period
        if (currentPeriodCounter is not null && currentPeriodCounter.SubmissionCount >= policy.MaxReportsPerPeriod)
        {
            violations.Add($"POL-REP-LIMIT: Maximum reports limit ({policy.MaxReportsPerPeriod}) for period '{report.ReportingPeriod}' reached.");
        }

        // 2. Aggregate Monthly Monetary Limit
        Money totalClaimed = report.CalculateTotalClaimed();
        long currentTotalMinor = currentPeriodCounter?.AggregateAmount.AmountMinor ?? 0;
        if (currentTotalMinor + totalClaimed.AmountMinor > policy.AggregateMonthlyLimit.AmountMinor)
        {
            violations.Add($"POL-AMT-LIMIT: Aggregate claims ({currentTotalMinor + totalClaimed.AmountMinor}) would exceed monthly limit ({policy.AggregateMonthlyLimit.AmountMinor}).");
        }

        // 3. Consolidated Receipt Document Requirement
        if (policy.RequireConsolidatedReceiptPdf && string.IsNullOrWhiteSpace(report.ConsolidatedReceiptDocId))
        {
            violations.Add("POL-DOC-RECEIPT: Consolidated receipt PDF is mandatory under policy.");
        }

        // 4. Out-of-Base MFAR Requirement
        if (policy.RequireMfarForOutOfBase && report.OutOfBaseActivity && string.IsNullOrWhiteSpace(report.MfarDocId))
        {
            violations.Add("POL-DOC-MFAR: Out-of-base activity requires an approved MFAR document.");
        }

        // 5. Employee Declaration
        if (!report.EmployeeDeclarationConfirmed)
        {
            violations.Add("POL-DECL-CONFIRM: Employee declaration of truthfulness must be confirmed.");
        }

        return new PolicyEvaluationResult(
            IsAllowed: violations.Count == 0,
            Violations: violations,
            Warnings: warnings);
    }
}

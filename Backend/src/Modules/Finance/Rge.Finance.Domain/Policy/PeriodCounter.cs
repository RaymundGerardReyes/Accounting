using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Policy;

/// <summary>
/// Tracks aggregate submissions and monetary claims for an employee within a calendar period (YYYY-MM).
/// </summary>
public sealed class PeriodCounter : Entity<string>
{
    public string EmployeeId { get; private set; }
    public string PeriodKey { get; private set; }
    public string RuleCode { get; private set; }
    public long SubmissionCount { get; private set; }
    public Money AggregateAmount { get; private set; }
    public string? ActiveReportId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public PeriodCounter(
        string id,
        string employeeId,
        string periodKey,
        string ruleCode,
        long submissionCount,
        Money aggregateAmount,
        string? activeReportId,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        EmployeeId = Guard.AgainstNullOrWhiteSpace(employeeId);
        PeriodKey = Guard.AgainstNullOrWhiteSpace(periodKey);
        RuleCode = Guard.AgainstNullOrWhiteSpace(ruleCode);
        SubmissionCount = submissionCount;
        AggregateAmount = aggregateAmount;
        ActiveReportId = activeReportId;
        UpdatedAtUtc = nowUtc;
    }

    public static PeriodCounter Create(string employeeId, string periodKey, string ruleCode, string currencyCode, DateTimeOffset nowUtc)
    {
        return new PeriodCounter(
            id: Guid.NewGuid().ToString("D"),
            employeeId: employeeId,
            periodKey: periodKey,
            ruleCode: ruleCode,
            submissionCount: 0,
            aggregateAmount: Money.Zero(currencyCode),
            activeReportId: null,
            nowUtc: nowUtc);
    }

    public void Increment(Money amount, string reportId, DateTimeOffset nowUtc)
    {
        SubmissionCount++;
        AggregateAmount += amount;
        ActiveReportId = reportId;
        UpdatedAtUtc = nowUtc;
    }

    public void Reset(DateTimeOffset nowUtc)
    {
        SubmissionCount = 0;
        AggregateAmount = Money.Zero(AggregateAmount.CurrencyCode);
        ActiveReportId = null;
        UpdatedAtUtc = nowUtc;
    }
}

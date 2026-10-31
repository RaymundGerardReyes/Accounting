using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Domain.Policy;

public enum PolicyVersionStatus
{
    Draft,
    PendingApproval,
    Active,
    Retired
}

/// <summary>
/// Specific versioned instance of a financial policy containing operational thresholds and compliance flags.
/// </summary>
public sealed class PolicyVersion : Entity<string>
{
    public string PolicyId { get; private set; }
    public long VersionNumber { get; private set; }
    public PolicyVersionStatus Status { get; private set; }
    public string EffectiveFrom { get; private set; }
    public string? EffectiveUntil { get; private set; }
    public string Currency { get; private set; }
    public Money AggregateMonthlyLimit { get; private set; }
    public int MaxReportsPerPeriod { get; private set; }
    public string PeriodType { get; private set; }
    public int SubmissionDeadlineDayOfNextMonth { get; private set; }
    public string DeadlineInterpretation { get; private set; }
    public int FinanceSlaDays { get; private set; }
    public string SlaDayType { get; private set; }
    public bool RequireConsolidatedReceiptPdf { get; private set; }
    public bool RequireMfarForOutOfBase { get; private set; }
    public bool AllowPartialLineApproval { get; private set; }
    public bool BlockNewAdvanceWhenOverdue { get; private set; }
    public Money? MaxAdvanceAmount { get; private set; }
    public string EligibleCategoriesJson { get; private set; }
    public string? PreviousValuesJson { get; private set; }
    public string ChangeJustification { get; private set; }
    public string RatificationStatus { get; private set; }
    public string AuthorId { get; private set; }
    public string? ApproverId { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? RetiredAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public PolicyVersion(
        string id,
        string policyId,
        long versionNumber,
        string effectiveFrom,
        string? effectiveUntil,
        Money aggregateMonthlyLimit,
        int maxReportsPerPeriod,
        string periodType,
        int submissionDeadlineDay,
        string deadlineInterpretation,
        int financeSlaDays,
        string slaDayType,
        bool requireConsolidatedReceiptPdf,
        bool requireMfarForOutOfBase,
        bool allowPartialLineApproval,
        bool blockNewAdvanceWhenOverdue,
        Money? maxAdvanceAmount,
        string eligibleCategoriesJson,
        string? previousValuesJson,
        string changeJustification,
        string authorId,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        PolicyId = Guard.AgainstNullOrWhiteSpace(policyId);
        VersionNumber = versionNumber;
        Status = PolicyVersionStatus.Draft;
        EffectiveFrom = Guard.AgainstNullOrWhiteSpace(effectiveFrom);
        EffectiveUntil = effectiveUntil;
        Currency = aggregateMonthlyLimit.CurrencyCode;
        AggregateMonthlyLimit = aggregateMonthlyLimit;
        MaxReportsPerPeriod = maxReportsPerPeriod;
        PeriodType = Guard.AgainstNullOrWhiteSpace(periodType);
        SubmissionDeadlineDayOfNextMonth = submissionDeadlineDay;
        DeadlineInterpretation = Guard.AgainstNullOrWhiteSpace(deadlineInterpretation);
        FinanceSlaDays = financeSlaDays;
        SlaDayType = Guard.AgainstNullOrWhiteSpace(slaDayType);
        RequireConsolidatedReceiptPdf = requireConsolidatedReceiptPdf;
        RequireMfarForOutOfBase = requireMfarForOutOfBase;
        AllowPartialLineApproval = allowPartialLineApproval;
        BlockNewAdvanceWhenOverdue = blockNewAdvanceWhenOverdue;
        MaxAdvanceAmount = maxAdvanceAmount;
        EligibleCategoriesJson = Guard.AgainstNullOrWhiteSpace(eligibleCategoriesJson);
        PreviousValuesJson = previousValuesJson;
        ChangeJustification = Guard.AgainstNullOrWhiteSpace(changeJustification);
        RatificationStatus = "pending_finance_ratification";
        AuthorId = Guard.AgainstNullOrWhiteSpace(authorId);
        CreatedAtUtc = nowUtc;
    }

    public Result Submit(DateTimeOffset nowUtc)
    {
        if (Status != PolicyVersionStatus.Draft)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("PolicyVersion", Status.ToString(), nameof(PolicyVersionStatus.PendingApproval)));
        }

        Status = PolicyVersionStatus.PendingApproval;
        SubmittedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Approve(string approverId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(approverId);
        if (string.Equals(approverId, AuthorId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FinanceErrors.SodViolation("INV-SOD-001", "Author cannot approve their own policy version."));
        }

        if (Status != PolicyVersionStatus.PendingApproval)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("PolicyVersion", Status.ToString(), nameof(PolicyVersionStatus.Active)));
        }

        ApproverId = approverId;
        Status = PolicyVersionStatus.Active;
        RatificationStatus = "cpa_ratified";
        ApprovedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Retire(DateTimeOffset nowUtc)
    {
        if (Status != PolicyVersionStatus.Active)
        {
            return Result.Failure(FinanceErrors.InvalidTransition("PolicyVersion", Status.ToString(), nameof(PolicyVersionStatus.Retired)));
        }

        Status = PolicyVersionStatus.Retired;
        RetiredAtUtc = nowUtc;
        return Result.Success();
    }
}

using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Finance.Application.Reporting.KpiDefinitions;

public sealed record GetKpiDefinitionsQuery : IQuery<IReadOnlyList<KpiDefinitionDto>>;

public sealed record KpiDefinitionDto(
    string KpiCode,
    string Label,
    string MeasureType,
    string Numerator,
    string Denominator,
    string DateBasis,
    IReadOnlyList<string> EligibleStates,
    string Exclusions);

public sealed class KpiDefinitionsHandler : IQueryHandler<GetKpiDefinitionsQuery, IReadOnlyList<KpiDefinitionDto>>
{
    private static readonly IReadOnlyList<KpiDefinitionDto> Definitions =
    [
        new(
            KpiCode: "advances_released_minor",
            Label: "Total Advances Released (Period Flow)",
            MeasureType: "period_flow",
            Numerator: "Sum of Disbursement.executed_amount_minor where status = 'evidence_verified'",
            Denominator: "N/A (Monetary sum in centavos)",
            DateBasis: "Disbursement.evidence_verified_at",
            EligibleStates: ["evidence_verified"],
            Exclusions: "Excludes draft, submitted, prepared, unverified, failed, or cancelled."),
        new(
            KpiCode: "outstanding_advance_balance_minor",
            Label: "Outstanding Advance Balance (As-Of Point-in-Time)",
            MeasureType: "point_in_time_balance",
            Numerator: "Sum of positive derived balances from AdvanceLedgerEntry rows",
            Denominator: "N/A (Monetary sum in centavos)",
            DateBasis: "AdvanceLedgerEntry.occurred_at_utc <= as_of_date",
            EligibleStates: ["evidence_verified"],
            Exclusions: "Excludes unreleased advances and closed advances with zero balance."),
        new(
            KpiCode: "overdue_advances_count",
            Label: "Overdue Advances Count",
            MeasureType: "point_in_time_balance",
            Numerator: "Count of released CashAdvances with balance > 0 and liquidation_due_date < as_of_date",
            Denominator: "N/A (Record count)",
            DateBasis: "CashAdvance.liquidation_due_date < as_of_date",
            EligibleStates: ["open", "settlement_pending"],
            Exclusions: "Excludes unreleased or fully reconciled/closed advances."),
        new(
            KpiCode: "on_time_liquidation_rate_pct",
            Label: "On-Time Liquidation Rate (%)",
            MeasureType: "period_cohort_ratio",
            Numerator: "Count of released advances liquidated on or before deadline",
            Denominator: "Count of all released advances due in period",
            DateBasis: "CashAdvance.liquidation_due_date",
            EligibleStates: ["evidence_verified"],
            Exclusions: "Excludes cancelled or unreleased advances.")
    ];

    public Task<Result<IReadOnlyList<KpiDefinitionDto>>> HandleAsync(
        GetKpiDefinitionsQuery query,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(Definitions));
    }
}

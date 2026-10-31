using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Finance.Application.Reporting.BusinessConfirmations;

public sealed record GetBusinessConfirmationsQuery : IQuery<IReadOnlyList<BusinessConfirmationDto>>;

public sealed record BusinessConfirmationDto(
    string Code,
    string Topic,
    string Status,
    string CurrentSetting,
    string Note);

public sealed class BusinessConfirmationsHandler : IQueryHandler<GetBusinessConfirmationsQuery, IReadOnlyList<BusinessConfirmationDto>>
{
    private static readonly IReadOnlyList<BusinessConfirmationDto> Confirmations =
    [
        new("C1", "Field-Staff Monthly Expense Cap & Frequency Ratification", "configurable_pending_ratification", "PHP 15,000.00 aggregate monthly limit & 1 report/month in Policy v1", "Seeded as versioned configuration; never hardcoded as business constants."),
        new("C2", "Interpretation of 'By 2nd' Monthly Submission Deadline", "configurable_pending_ratification", "submission_deadline_day_of_next_month = 2 (flagged day_of_following_month_unconfirmed)", "Configurable per policy version without code changes."),
        new("C3", "Finance Processing SLA ('Five Days')", "configurable_pending_ratification", "finance_sla_days = 5 using PH holiday_calendar table (flagged business_days_unconfirmed)", "Configurable between business days and calendar days."),
        new("C4", "Cash Advance Maximum Limit vs Expense Report Cap", "configurable_pending_ratification", "max_advance_amount_minor = null (bounded by ApprovalMatrix role limits)", "Separate policy parameter from monthly expense report cap."),
        new("C5", "Four-Eyes Separation of Duties on Financial Approvals", "active_enforced", "Strict distinct actor enforcement (Maker != Verifier != Bank Approver != Evidence Verifier)", "Enforced across domain entities and API endpoints."),
        new("C6", "Oldest-First Advance Settlement Ordering", "active_enforced", "Automatic FIFO settlement of expense reports against unliquidated advances", "Deterministic reconciliation behavior."),
        new("C7", "Formula Injection Neutralization on CSV Exports", "active_enforced", "Prefixes =, +, -, @, Tab, CR sanitized on cell output", "Protects against spreadsheet execution attacks.")
    ];

    public Task<Result<IReadOnlyList<BusinessConfirmationDto>>> HandleAsync(
        GetBusinessConfirmationsQuery query,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(Confirmations));
    }
}

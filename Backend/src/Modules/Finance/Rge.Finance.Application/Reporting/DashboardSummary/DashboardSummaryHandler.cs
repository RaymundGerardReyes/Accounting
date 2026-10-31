using System.Globalization;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Ledger;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.Reporting.DashboardSummary;

public sealed record GetDashboardSummaryQuery(
    string? StartDate = null,
    string? EndDate = null,
    string? AsOfDate = null,
    string? DepartmentId = null) : IQuery<DashboardSummaryDto>;

public sealed record DashboardSummaryDto(
    long TotalAdvancesReleasedMinor,
    long OutstandingAdvanceBalanceMinor,
    int OverdueAdvancesCount,
    int PendingApprovalsCount,
    int OpenExpenseReportsCount,
    long TotalLiquidatedExpenseMinor,
    decimal OnTimeLiquidationRatePct);

public sealed class DashboardSummaryHandler : IQueryHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private readonly ICashAdvanceRepository _advanceRepository;
    private readonly IDisbursementRepository _disbursementRepository;
    private readonly IExpenseReportRepository _reportRepository;
    private readonly IAdvanceLedgerStore _ledgerStore;

    public DashboardSummaryHandler(
        ICashAdvanceRepository advanceRepository,
        IDisbursementRepository disbursementRepository,
        IExpenseReportRepository reportRepository,
        IAdvanceLedgerStore ledgerStore)
    {
        ArgumentNullException.ThrowIfNull(advanceRepository);
        ArgumentNullException.ThrowIfNull(disbursementRepository);
        ArgumentNullException.ThrowIfNull(reportRepository);
        ArgumentNullException.ThrowIfNull(ledgerStore);

        _advanceRepository = advanceRepository;
        _disbursementRepository = disbursementRepository;
        _reportRepository = reportRepository;
        _ledgerStore = ledgerStore;
    }

    public async Task<Result<DashboardSummaryDto>> HandleAsync(
        GetDashboardSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<CashAdvance> advances = await _advanceRepository.ListAsync(query.DepartmentId, null, 200, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ExpenseReport> reports = await _reportRepository.ListAsync(null, null, 200, cancellationToken).ConfigureAwait(false);

        long totalReleasedMinor = 0;
        long totalOutstandingMinor = 0;
        int overdueCount = 0;
        int pendingApprovals = advances.Count(a => a.ApprovalStatus == ApprovalStatus.Submitted);

        string asOf = query.AsOfDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (CashAdvance adv in advances)
        {
            if (adv.PaymentStatus == PaymentStatus.EvidenceVerified)
            {
                totalReleasedMinor += adv.ApprovedAmount?.AmountMinor ?? adv.RequestedAmount.AmountMinor;

                IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(adv.Id, cancellationToken).ConfigureAwait(false);
                var bal = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, adv.RequestedAmount.CurrencyCode);
                if (bal.AmountMinor > 0)
                {
                    totalOutstandingMinor += bal.AmountMinor;
                    if (!string.IsNullOrWhiteSpace(adv.LiquidationDueDate) && string.CompareOrdinal(adv.LiquidationDueDate, asOf) < 0)
                    {
                        overdueCount++;
                    }
                }
            }
        }

        int openReportsCount = reports.Count(r => r.ReviewStatus is ExpenseReportStatus.Draft or ExpenseReportStatus.Submitted);
        long totalLiquidatedMinor = reports.Where(r => r.ReviewStatus == ExpenseReportStatus.Approved).Sum(r => r.CalculateTotalApproved().AmountMinor);

        int totalReleasedCount = advances.Count(a => a.PaymentStatus == PaymentStatus.EvidenceVerified);
        decimal onTimeRate = totalReleasedCount > 0
            ? Math.Round((decimal)(totalReleasedCount - overdueCount) / totalReleasedCount * 100m, 2)
            : 100.0m;

        return Result.Success(new DashboardSummaryDto(
            TotalAdvancesReleasedMinor: totalReleasedMinor,
            OutstandingAdvanceBalanceMinor: totalOutstandingMinor,
            OverdueAdvancesCount: overdueCount,
            PendingApprovalsCount: pendingApprovals,
            OpenExpenseReportsCount: openReportsCount,
            TotalLiquidatedExpenseMinor: totalLiquidatedMinor,
            OnTimeLiquidationRatePct: onTimeRate));
    }
}

using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Liquidation;
using Rge.Finance.Domain.Procurement;

namespace Rge.Finance.Application.Reporting.Tracker;

public sealed record GetCentralizedTrackerQuery(
    string? Workflow = null,
    string? DepartmentId = null,
    string? Status = null,
    string? Search = null,
    int Limit = 50) : IQuery<IReadOnlyList<TrackerItemDto>>;

public sealed record TrackerItemDto(
    string RecordType,
    string RecordNumber,
    string DepartmentId,
    string? EmployeeId,
    string? VendorName,
    long AmountMinor,
    string Currency,
    string Status,
    string Stage,
    DateTimeOffset CreatedAtUtc);

public sealed class TrackerHandler : IQueryHandler<GetCentralizedTrackerQuery, IReadOnlyList<TrackerItemDto>>
{
    private readonly ICashAdvanceRepository _advanceRepository;
    private readonly IPurchaseRequestRepository _prRepository;
    private readonly IExpenseReportRepository _reportRepository;
    private readonly IAccountPayableRepository _apRepository;

    public TrackerHandler(
        ICashAdvanceRepository advanceRepository,
        IPurchaseRequestRepository prRepository,
        IExpenseReportRepository reportRepository,
        IAccountPayableRepository apRepository)
    {
        ArgumentNullException.ThrowIfNull(advanceRepository);
        ArgumentNullException.ThrowIfNull(prRepository);
        ArgumentNullException.ThrowIfNull(reportRepository);
        ArgumentNullException.ThrowIfNull(apRepository);

        _advanceRepository = advanceRepository;
        _prRepository = prRepository;
        _reportRepository = reportRepository;
        _apRepository = apRepository;
    }

    public async Task<Result<IReadOnlyList<TrackerItemDto>>> HandleAsync(
        GetCentralizedTrackerQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var items = new List<TrackerItemDto>();

        if (string.IsNullOrWhiteSpace(query.Workflow) || string.Equals(query.Workflow, "cash_advance", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<CashAdvance> advances = await _advanceRepository.ListAsync(query.DepartmentId, query.Status, query.Limit, cancellationToken).ConfigureAwait(false);
            items.AddRange(advances.Select(a => new TrackerItemDto(
                RecordType: "cash_advance",
                RecordNumber: a.AdvanceNumber.Value,
                DepartmentId: a.DepartmentId,
                EmployeeId: a.EmployeeId,
                VendorName: null,
                AmountMinor: a.RequestedAmount.AmountMinor,
                Currency: a.RequestedAmount.CurrencyCode,
                Status: a.ApprovalStatus.ToString().ToLowerInvariant(),
                Stage: a.DeriveOverallStage(),
                CreatedAtUtc: a.CreatedAtUtc)));
        }

        if (string.IsNullOrWhiteSpace(query.Workflow) || string.Equals(query.Workflow, "purchase_request", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<PurchaseRequest> prs = await _prRepository.ListAsync(query.DepartmentId, query.Status, query.Limit, cancellationToken).ConfigureAwait(false);
            items.AddRange(prs.Select(p => new TrackerItemDto(
                RecordType: "purchase_request",
                RecordNumber: p.RequestNumber,
                DepartmentId: p.DepartmentId,
                EmployeeId: null,
                VendorName: p.VendorId,
                AmountMinor: p.RequestedAmount.AmountMinor,
                Currency: p.RequestedAmount.CurrencyCode,
                Status: p.ApprovalStatus.ToString().ToLowerInvariant(),
                Stage: p.WorkflowStatus.ToLowerInvariant(),
                CreatedAtUtc: p.CreatedAtUtc)));
        }

        if (string.IsNullOrWhiteSpace(query.Workflow) || string.Equals(query.Workflow, "expense_report", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<ExpenseReport> reports = await _reportRepository.ListAsync(null, query.Status, query.Limit, cancellationToken).ConfigureAwait(false);
            items.AddRange(reports.Select(r => new TrackerItemDto(
                RecordType: "expense_report",
                RecordNumber: r.ReportNumber,
                DepartmentId: r.DepartmentId,
                EmployeeId: r.EmployeeId,
                VendorName: null,
                AmountMinor: r.CalculateTotalClaimed().AmountMinor,
                Currency: r.Currency,
                Status: r.ReviewStatus.ToString().ToLowerInvariant(),
                Stage: r.SettlementStatus.ToString().ToLowerInvariant(),
                CreatedAtUtc: r.CreatedAtUtc)));
        }

        if (string.IsNullOrWhiteSpace(query.Workflow) || string.Equals(query.Workflow, "accounts_payable", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<AccountPayable> payables = await _apRepository.ListAsync(null, query.Status, query.Limit, cancellationToken).ConfigureAwait(false);
            items.AddRange(payables.Select(ap => new TrackerItemDto(
                RecordType: "accounts_payable",
                RecordNumber: ap.PayableNumber,
                DepartmentId: "*",
                EmployeeId: null,
                VendorName: ap.VendorId,
                AmountMinor: ap.NetPayable.AmountMinor,
                Currency: ap.NetPayable.CurrencyCode,
                Status: ap.Status.ToString().ToLowerInvariant(),
                Stage: ap.Status.ToString().ToLowerInvariant(),
                CreatedAtUtc: ap.CreatedAtUtc)));
        }

        return Result.Success<IReadOnlyList<TrackerItemDto>>(items.OrderByDescending(i => i.CreatedAtUtc).Take(query.Limit).ToList());
    }
}

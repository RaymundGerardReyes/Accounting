using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.ExpenseReports.Create;
using Rge.Finance.Application.Ports;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.CashAdvances.StateMachines;
using Rge.Finance.Domain.Errors;
using Rge.Finance.Domain.Ledger;
using Rge.Finance.Domain.Liquidation;

namespace Rge.Finance.Application.ExpenseReports.Review;

public sealed record ReviewExpenseReportCommand(
    string ReportId,
    string ReviewerId,
    string Decision,
    string? Notes = null,
    string? RejectionReason = null,
    IReadOnlyDictionary<string, long>? ApprovedLineAmountsMinor = null) : ICommand<ExpenseReportDto>;

public sealed class ReviewHandler : ICommandHandler<ReviewExpenseReportCommand, ExpenseReportDto>
{
    private readonly IExpenseReportRepository _reportRepository;
    private readonly ICashAdvanceRepository _advanceRepository;
    private readonly IAdvanceLedgerStore _ledgerStore;
    private readonly ISettlementApplicationStore _settlementStore;
    private readonly IClock _clock;

    public ReviewHandler(
        IExpenseReportRepository reportRepository,
        ICashAdvanceRepository advanceRepository,
        IAdvanceLedgerStore ledgerStore,
        ISettlementApplicationStore settlementStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(reportRepository);
        ArgumentNullException.ThrowIfNull(advanceRepository);
        ArgumentNullException.ThrowIfNull(ledgerStore);
        ArgumentNullException.ThrowIfNull(settlementStore);
        ArgumentNullException.ThrowIfNull(clock);

        _reportRepository = reportRepository;
        _advanceRepository = advanceRepository;
        _ledgerStore = ledgerStore;
        _settlementStore = settlementStore;
        _clock = clock;
    }

    public async Task<Result<ExpenseReportDto>> HandleAsync(
        ReviewExpenseReportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ExpenseReport? report = await _reportRepository.GetByIdAsync(command.ReportId, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return Result.Failure<ExpenseReportDto>(FinanceErrors.ReportNotFound(command.ReportId));
        }

        if (string.Equals(report.EmployeeId, command.ReviewerId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ExpenseReportDto>(Error.SodViolation(
                "Finance.SoD.EmployeeCannotReviewOwnReport",
                "Employee cannot review their own expense report."));
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        string decisionNorm = command.Decision.Trim().ToLowerInvariant();

        ExpenseReportStatus targetStatus = decisionNorm switch
        {
            "approved" => ExpenseReportStatus.Approved,
            "partially_approved" => ExpenseReportStatus.PartiallyApproved,
            "rejected" => ExpenseReportStatus.Rejected,
            "returned" => ExpenseReportStatus.ReturnedForCorrection,
            _ => ExpenseReportStatus.Draft
        };

        if (targetStatus == ExpenseReportStatus.Draft)
        {
            return Result.Failure<ExpenseReportDto>(Error.Validation(
                "Finance.Report.InvalidDecision",
                $"Decision '{command.Decision}' is invalid. Must be 'approved', 'partially_approved', 'rejected', or 'returned'."));
        }

        // Apply line-level decisions if provided
        foreach (ExpenseLine line in report.Lines)
        {
            if (targetStatus is ExpenseReportStatus.Approved)
            {
                line.Review(command.ReviewerId, ExpenseLineStatus.Approved, line.ClaimedAmount, command.Notes, null, nowUtc);
            }
            else if (targetStatus is ExpenseReportStatus.Rejected)
            {
                line.Review(command.ReviewerId, ExpenseLineStatus.Rejected, Money.Zero(line.ClaimedAmount.CurrencyCode), command.Notes, command.RejectionReason, nowUtc);
            }
            else if (targetStatus is ExpenseReportStatus.PartiallyApproved)
            {
                if (command.ApprovedLineAmountsMinor != null && command.ApprovedLineAmountsMinor.TryGetValue(line.Id, out long approvedMinor))
                {
                    var approvedMoney = new Money(approvedMinor, line.ClaimedAmount.CurrencyCode);
                    ExpenseLineStatus status = approvedMinor == line.ClaimedAmount.AmountMinor
                        ? ExpenseLineStatus.Approved
                        : (approvedMinor > 0 ? ExpenseLineStatus.PartiallyApproved : ExpenseLineStatus.Rejected);
                    line.Review(command.ReviewerId, status, approvedMoney, command.Notes, null, nowUtc);
                }
                else
                {
                    line.Review(command.ReviewerId, ExpenseLineStatus.Approved, line.ClaimedAmount, command.Notes, null, nowUtc);
                }
            }
        }

        Result reviewResult = report.Review(command.ReviewerId, targetStatus, command.Notes, command.RejectionReason, nowUtc);
        if (reviewResult.IsFailure)
        {
            return Result.Failure<ExpenseReportDto>(reviewResult.Error);
        }

        // If approved or partially approved, perform oldest-first settlement against unliquidated cash advances
        if (targetStatus is ExpenseReportStatus.Approved or ExpenseReportStatus.PartiallyApproved)
        {
            Money totalApproved = report.CalculateTotalApproved();
            long unallocatedExpenseMinor = totalApproved.AmountMinor;

            IReadOnlyList<CashAdvance> advances = await _advanceRepository.ListAsync(report.DepartmentId, null, 100, cancellationToken).ConfigureAwait(false);
            var employeeAdvances = advances
                .Where(a => string.Equals(a.EmployeeId, report.EmployeeId, StringComparison.OrdinalIgnoreCase) &&
                            a.PaymentStatus == PaymentStatus.EvidenceVerified &&
                            a.SettlementStatus != SettlementStatus.Closed)
                .OrderBy(a => a.CreatedAtUtc)
                .ToList();

            foreach (CashAdvance adv in employeeAdvances)
            {
                if (unallocatedExpenseMinor <= 0)
                {
                    break;
                }

                IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(adv.Id, cancellationToken).ConfigureAwait(false);
                var balance = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, adv.RequestedAmount.CurrencyCode);

                if (balance.AmountMinor > 0)
                {
                    long applyMinor = Math.Min(balance.AmountMinor, unallocatedExpenseMinor);
                    var applyMoney = new Money(applyMinor, adv.RequestedAmount.CurrencyCode);

                    var app = SettlementApplication.Create(
                        expenseReportId: report.Id,
                        expenseLineId: null,
                        advanceId: adv.Id,
                        employeeId: adv.EmployeeId,
                        applicationType: "expense_liquidation",
                        appliedAmount: applyMoney,
                        ledgerEntryId: null,
                        evidenceDocumentId: null,
                        bankReference: null,
                        appliedById: command.ReviewerId,
                        notes: command.Notes,
                        appliedAtUtc: nowUtc);

                    await _settlementStore.SaveApplicationAsync(app, cancellationToken).ConfigureAwait(false);

                    var ledgerEntry = new AdvanceLedgerEntry(
                        id: Guid.NewGuid().ToString("D"),
                        advanceId: adv.Id,
                        employeeId: adv.EmployeeId,
                        entryType: LedgerEntryType.ExpenseApplied,
                        amount: applyMoney,
                        signedAmountMinor: -applyMinor,
                        createdByActorId: command.ReviewerId,
                        occurredAtUtc: nowUtc);

                    await _ledgerStore.SaveEntryAsync(ledgerEntry, cancellationToken).ConfigureAwait(false);

                    unallocatedExpenseMinor -= applyMinor;

                    if (applyMinor == balance.AmountMinor)
                    {
                        adv.TransitionSettlementStatus(SettlementStatus.Reconciled, nowUtc);
                        await _advanceRepository.SaveAsync(adv, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // If excess remains after fully liquidating all advances, record reimbursement payable
            if (unallocatedExpenseMinor > 0 && employeeAdvances.Count > 0)
            {
                CashAdvance primaryAdvance = employeeAdvances[0];
                var excessMoney = new Money(unallocatedExpenseMinor, primaryAdvance.RequestedAmount.CurrencyCode);
                var reimbursementEntry = new AdvanceLedgerEntry(
                    id: Guid.NewGuid().ToString("D"),
                    advanceId: primaryAdvance.Id,
                    employeeId: primaryAdvance.EmployeeId,
                    entryType: LedgerEntryType.ReimbursementPayable,
                    amount: excessMoney,
                    signedAmountMinor: unallocatedExpenseMinor,
                    createdByActorId: command.ReviewerId,
                    occurredAtUtc: nowUtc);

                await _ledgerStore.SaveEntryAsync(reimbursementEntry, cancellationToken).ConfigureAwait(false);
            }
        }

        await _reportRepository.SaveAsync(report, cancellationToken).ConfigureAwait(false);

        return Result.Success(CreateHandler.MapToDto(report));
    }
}

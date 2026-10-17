import csv
import io
from datetime import datetime, timezone
from decimal import Decimal
from typing import Any, Optional

from sqlalchemy.orm import Session

from finance_models import (
    CashAdvance,
    Disbursement,
    AdvanceLedgerEntry,
    ExpenseReport,
    ExpenseLine,
    SettlementApplication,
    PurchaseRequest,
    AccountPayable,
    PaymentRecord,
    Vendor,
    ExceptionRegister,
    ExceptionRequest,
    AuditEvent,
    TaxCode,
    utc_now,
)
from services.finance_common import (
    ResolvedActor,
    FinanceNotFoundError,
    FinanceForbiddenError,
    FinanceValidationError,
    require_roles,
    record_audit_event,
    manila_today_str,
)
from services.finance_workflows import compute_advance_reconciliation


KPI_DEFINITIONS: dict[str, dict[str, Any]] = {
    "advances_released_minor": {
        "kpi_code": "advances_released_minor",
        "label": "Total Advances Released (Period Flow)",
        "measure_type": "period_flow",
        "numerator": "Sum of Disbursement.executed_amount_minor where status = 'evidence_verified' and evidence_verified_at within [start_date, end_date]",
        "denominator": "N/A (Monetary sum in centavos)",
        "date_basis": "Disbursement.evidence_verified_at",
        "eligible_states": ["evidence_verified"],
        "exclusions": "Excludes draft, submitted, approved-but-unreleased, prepared, verified, bank_approved, executed_unverified, failed, unknown, and cancelled records.",
    },
    "outstanding_advance_balance_minor": {
        "kpi_code": "outstanding_advance_balance_minor",
        "label": "Outstanding Advance Balance (As-Of Point-in-Time)",
        "measure_type": "point_in_time_balance",
        "numerator": "Sum of positive derived balances per CashAdvance from AdvanceLedgerEntry rows with effective_at <= as_of_date (Released - Approved Expenses Applied - Cash Returned)",
        "denominator": "N/A (Monetary sum in centavos)",
        "date_basis": "AdvanceLedgerEntry.effective_at <= as_of_date",
        "eligible_states": ["evidence_verified"],
        "exclusions": "Excludes unreleased advances and closed advances with zero balance.",
    },
    "overdue_advances_count": {
        "kpi_code": "overdue_advances_count",
        "label": "Overdue Advances Count",
        "measure_type": "point_in_time_balance",
        "numerator": "Count of released CashAdvances with derived outstanding_balance_minor > 0 and liquidation_due_date < as_of_date and settlement_status not in ('reconciled', 'closed')",
        "denominator": "N/A (Record count)",
        "date_basis": "CashAdvance.liquidation_due_date < as_of_date",
        "eligible_states": ["open", "settlement_pending", "shortfall_receivable"],
        "exclusions": "Excludes unreleased, reconciled, or closed advances.",
    },
    "on_time_liquidation_rate_pct": {
        "kpi_code": "on_time_liquidation_rate_pct",
        "label": "On-Time Liquidation Rate (%)",
        "measure_type": "period_cohort_ratio",
        "numerator": "Count of released advances due in period where liquidation was submitted on or before liquidation_due_date",
        "denominator": "Count of all released advances with liquidation_due_date within [start_date, end_date]",
        "date_basis": "CashAdvance.liquidation_due_date",
        "eligible_states": ["evidence_verified"],
        "exclusions": "Excludes cancelled or unreleased advances.",
    },
    "average_liquidation_turnaround_days": {
        "kpi_code": "average_liquidation_turnaround_days",
        "label": "Average Liquidation Turnaround (Days)",
        "measure_type": "period_average",
        "numerator": "Sum of elapsed days from Disbursement.evidence_verified_at to ExpenseReport.reviewed_at for settled advances",
        "denominator": "Count of advances with completed expense review in period",
        "date_basis": "Disbursement.evidence_verified_at to ExpenseReport.reviewed_at",
        "eligible_states": ["approved", "partially_approved"],
        "exclusions": "Excludes unreviewed or draft reports.",
    },
    "expense_rejection_rate_pct": {
        "kpi_code": "expense_rejection_rate_pct",
        "label": "Expense Line Rejection / Adjustment Rate (%)",
        "measure_type": "period_ratio",
        "numerator": "Count of reviewed ExpenseLines with line_status in ('rejected', 'returned', 'partially_approved')",
        "denominator": "Total count of reviewed ExpenseLines (approved, partially_approved, rejected, returned)",
        "date_basis": "ExpenseLine.reviewed_at",
        "eligible_states": ["approved", "partially_approved", "rejected", "returned"],
        "exclusions": "Excludes pending/unreviewed draft lines.",
    },
    "missing_document_rate_pct": {
        "kpi_code": "missing_document_rate_pct",
        "label": "Missing-Document Exception Rate (%)",
        "measure_type": "period_ratio",
        "numerator": "Count of ExpenseReports with missing_document exceptions or policy violations",
        "denominator": "Count of non-draft ExpenseReports evaluated",
        "date_basis": "ExpenseReport.created_at",
        "eligible_states": ["submitted", "finance_review", "returned_for_correction", "approved", "partially_approved", "rejected"],
        "exclusions": "Excludes cancelled reports.",
    },
    "purchase_requests_awaiting_approval_count": {
        "kpi_code": "purchase_requests_awaiting_approval_count",
        "label": "Purchase Requests Awaiting Approval",
        "measure_type": "point_in_time_balance",
        "numerator": "Count of PurchaseRequests with approval_status = 'submitted'",
        "denominator": "N/A",
        "date_basis": "Current persisted state",
        "eligible_states": ["submitted"],
        "exclusions": "Excludes draft, approved, rejected, or cancelled requests.",
    },
    "validated_payables_awaiting_payment_minor": {
        "kpi_code": "validated_payables_awaiting_payment_minor",
        "label": "Validated Payables Awaiting Payment",
        "measure_type": "point_in_time_balance",
        "numerator": "Sum of (net_payable_minor - confirmed_reconciled_payments_minor) for AccountPayables in ('open', 'partially_paid', 'paid_unverified')",
        "denominator": "N/A (Monetary sum in centavos)",
        "date_basis": "Current persisted state",
        "eligible_states": ["open", "partially_paid", "paid_unverified"],
        "exclusions": "Excludes reconciled, disputed, or cancelled payables.",
    },
    "unreconciled_payment_amount_minor": {
        "kpi_code": "unreconciled_payment_amount_minor",
        "label": "Unreconciled Bank Transactions Amount",
        "measure_type": "point_in_time_balance",
        "numerator": "Sum of executed_amount_minor on Disbursements ('executed_unverified', 'unknown') plus PaymentRecords ('paid_unverified', 'payment_unknown', or reconciliation_status = 'discrepancy')",
        "denominator": "N/A (Monetary sum in centavos)",
        "date_basis": "Current persisted state",
        "eligible_states": ["executed_unverified", "paid_unverified", "unknown", "payment_unknown", "discrepancy"],
        "exclusions": "Excludes prepared/unexecuted or fully matched/verified transactions.",
    },
    "duplicate_payment_alerts_count": {
        "kpi_code": "duplicate_payment_alerts_count",
        "label": "Active Duplicate Receipt / Invoice / Payment Alerts",
        "measure_type": "point_in_time_balance",
        "numerator": "Count of ExceptionRegister records with category in ('duplicate_receipt_alert', 'duplicate_invoice_alert', 'duplicate_payment_alert') and status in ('open', 'under_review', 'reopened')",
        "denominator": "N/A",
        "date_basis": "Current persisted state",
        "eligible_states": ["open", "under_review", "reopened"],
        "exclusions": "Excludes resolved alerts.",
    },
}

BUSINESS_CONFIRMATIONS_REGISTER: list[dict[str, str]] = [
    {
        "code": "C1",
        "topic": "Field-Staff Monthly Expense Cap & Frequency Ratification",
        "status": "configurable_pending_ratification",
        "current_setting": "PHP 15,000.00 aggregate monthly limit (1,500,000 centavos) & 1 report/month in Policy v1",
        "note": "Seeded as versioned configuration; never hardcoded as business constants.",
    },
    {
        "code": "C2",
        "topic": "Interpretation of 'By 2nd' Monthly Submission Deadline",
        "status": "configurable_pending_ratification",
        "current_setting": "submission_deadline_day_of_next_month = 2 (flagged day_of_following_month_unconfirmed)",
        "note": "Configurable per policy version without code changes.",
    },
    {
        "code": "C3",
        "topic": "Finance Processing SLA ('Five Days')",
        "status": "configurable_pending_ratification",
        "current_setting": "finance_sla_days = 5 using PH holiday_calendar table (flagged business_days_unconfirmed)",
        "note": "Configurable between business days and calendar days.",
    },
    {
        "code": "C4",
        "topic": "Cash Advance Maximum Limit vs Expense Report Cap",
        "status": "configurable_pending_ratification",
        "current_setting": "max_advance_amount_minor = null (bounded by ApprovalMatrix role limits; not conflated with PHP 15,000 report cap)",
        "note": "Separate policy parameter from monthly expense report cap.",
    },
    {
        "code": "C5",
        "topic": "Block New Cash Advance While Prior Advance is Overdue",
        "status": "configurable_active_control",
        "current_setting": "block_new_advance_when_overdue = true (overridable only via approved ExceptionRequest)",
        "note": "Enforced server-side in PolicyEngineService.evaluate_cash_advance.",
    },
    {
        "code": "C6",
        "topic": "Shortfall & Unapproved Expense Recovery Procedure",
        "status": "cpa_control_enforced",
        "current_setting": "Remains receivable ('shortfall_receivable') with ExceptionRegister follow-up; never auto-written-off.",
        "note": "Payroll deduction requires separate written employee authorization per labor rules.",
    },
    {
        "code": "C7",
        "topic": "Philippine VAT / EWT Tax Codes & BIR Form 2307",
        "status": "requires_cpa_configuration",
        "current_setting": "tax_codes table seeded empty; payables without CPA-approved code flagged 'tax_review_required'",
        "note": "Finance Controller (CPA) can add and approve tax codes in basis points.",
    },
    {
        "code": "C8",
        "topic": "Accounting Period Cutoff & Timezone",
        "status": "configured",
        "current_setting": "Asia/Manila timezone boundaries",
        "note": "Used across period counters and overdue calculations.",
    },
    {
        "code": "C9",
        "topic": "Partial Line Approval on Expense Reports",
        "status": "configurable_active_control",
        "current_setting": "allow_partial_line_approval = true (requires reviewer reason; unapproved portion stays receivable)",
        "note": "Controlled by active PolicyVersion.",
    },
    {
        "code": "C10",
        "topic": "Document Evidence Retention Policy",
        "status": "configured",
        "current_setting": "Append-only metadata + SHA-256 in document_references; no destructive delete route exposed",
        "note": "Retention status tracked per document.",
    },
    {
        "code": "C11",
        "topic": "Entity & Site Wording (Acme Mfg vs School / Destination)",
        "status": "configured",
        "current_setting": "Supports neutral school_or_destination / destination_or_site field while preserving Acme legacy config",
        "note": "Compatible with both field-visit and manufacturing cost center workflows.",
    },
]


def _sanitize_csv_cell(val: Any) -> Any:
    if val is None:
        return ""
    if isinstance(val, (int, float, Decimal)):
        return val
    s = str(val)
    if s and s[0] in ("=", "+", "-", "@", "\t", "\r"):
        return f"'{s}"
    return s


def _minor_to_php_decimal_str(minor: Optional[int]) -> str:
    if minor is None:
        return "0.00"
    d = Decimal(int(minor)) / Decimal(100)
    return f"{d:.2f}"


class FinanceReportingService:
    def get_kpi_definitions(self) -> list[dict[str, Any]]:
        return list(KPI_DEFINITIONS.values())

    def get_business_confirmations(self) -> list[dict[str, str]]:
        return BUSINESS_CONFIRMATIONS_REGISTER

    def _scope_advances_query(
        self,
        db: Session,
        actor: ResolvedActor,
        department_id: Optional[str] = None,
        cost_center_id: Optional[str] = None,
        employee_id: Optional[str] = None,
    ) -> list[CashAdvance]:
        q = db.query(CashAdvance)
        if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "maker", "verifier", "bank_approver", "evidence_verifier", "auditor"):
            q = q.filter(CashAdvance.employee_id == actor.id)
        if department_id and department_id != "all":
            q = q.filter(CashAdvance.department_id == department_id)
        if cost_center_id and cost_center_id != "all":
            q = q.filter(CashAdvance.cost_center_id == cost_center_id)
        if employee_id and employee_id != "all":
            q = q.filter(CashAdvance.employee_id == employee_id)
        return q.order_by(CashAdvance.created_at.desc()).all()

    def _scope_reports_query(
        self,
        db: Session,
        actor: ResolvedActor,
        department_id: Optional[str] = None,
        cost_center_id: Optional[str] = None,
        employee_id: Optional[str] = None,
    ) -> list[ExpenseReport]:
        q = db.query(ExpenseReport)
        if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "maker", "verifier", "bank_approver", "evidence_verifier", "auditor"):
            q = q.filter(ExpenseReport.employee_id == actor.id)
        if department_id and department_id != "all":
            q = q.filter(ExpenseReport.department_id == department_id)
        if cost_center_id and cost_center_id != "all":
            q = q.filter(ExpenseReport.cost_center_id == cost_center_id)
        if employee_id and employee_id != "all":
            q = q.filter(ExpenseReport.employee_id == employee_id)
        return q.order_by(ExpenseReport.created_at.desc()).all()

    def _scope_prs_query(
        self,
        db: Session,
        actor: ResolvedActor,
        department_id: Optional[str] = None,
        cost_center_id: Optional[str] = None,
    ) -> list[PurchaseRequest]:
        q = db.query(PurchaseRequest)
        if not actor.has_role("finance_reviewer", "finance_controller", "bank_approver", "auditor"):
            q = q.filter(PurchaseRequest.is_restricted_category == 0)
        if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "maker", "verifier", "bank_approver", "evidence_verifier", "auditor"):
            q = q.filter(PurchaseRequest.requestor_id == actor.id)
        if department_id and department_id != "all":
            q = q.filter(PurchaseRequest.department_id == department_id)
        if cost_center_id and cost_center_id != "all":
            q = q.filter(PurchaseRequest.cost_center_id == cost_center_id)
        return q.order_by(PurchaseRequest.created_at.desc()).all()

    def get_dashboard_summary(
        self,
        db: Session,
        actor: ResolvedActor,
        start_date: Optional[str] = None,
        end_date: Optional[str] = None,
        as_of_date: Optional[str] = None,
        department_id: Optional[str] = None,
        cost_center_id: Optional[str] = None,
        employee_id: Optional[str] = None,
    ) -> dict[str, Any]:
        as_of = (as_of_date or end_date or manila_today_str())[:10]
        p_start = (start_date or "2000-01-01")[:10]
        p_end = (end_date or "2099-12-31")[:10]

        advances = self._scope_advances_query(db, actor, department_id, cost_center_id, employee_id)
        adv_ids = [a.id for a in advances]
        adv_map = {a.id: a for a in advances}

        # 1. Period flow: Advances actually released (evidence_verified only) within [p_start, p_end]
        advances_released_minor = 0
        released_disbursements_count = 0
        if adv_ids:
            disbs = (
                db.query(Disbursement)
                .filter(
                    Disbursement.advance_id.in_(adv_ids),
                    Disbursement.status == "evidence_verified",
                )
                .all()
            )
            for d in disbs:
                ver_date = (d.evidence_verified_at or d.created_at).strftime("%Y-%m-%d")
                if p_start <= ver_date <= p_end:
                    advances_released_minor += int(d.executed_amount_minor or 0)
                    released_disbursements_count += 1

        # 2. Point-in-time balance: Outstanding advance balance as of as_of_date
        outstanding_advance_balance_minor = 0
        unused_advances_awaiting_return_minor = 0
        overdue_advances_count = 0
        overdue_advances_amount_minor = 0
        aging_buckets = {"current": 0, "1_30_days": 0, "31_60_days": 0, "over_60_days": 0}

        for adv in advances:
            if adv.payment_status != "evidence_verified":
                continue
            entries = (
                db.query(AdvanceLedgerEntry)
                .filter(AdvanceLedgerEntry.advance_id == adv.id)
                .all()
            )
            as_of_entries = [
                e for e in entries
                if e.effective_at.strftime("%Y-%m-%d") <= as_of
            ]
            rel = sum(int(e.amount_minor) for e in as_of_entries if e.entry_type == "disbursement_release")
            app = sum(int(e.amount_minor) for e in as_of_entries if e.entry_type == "expense_applied")
            ret = sum(int(e.amount_minor) for e in as_of_entries if e.entry_type == "cash_return")
            adj = sum(int(e.signed_amount_minor) for e in as_of_entries if e.entry_type in ("adjustment", "reversal"))
            bal = max(0, rel - app - ret + adj)

            if adv.settlement_status != "closed" and bal > 0:
                outstanding_advance_balance_minor += bal
                if adv.liquidation_review_status in ("approved", "partially_approved"):
                    unused_advances_awaiting_return_minor += bal

                if adv.liquidation_due_date and adv.liquidation_due_date < as_of:
                    overdue_advances_count += 1
                    overdue_advances_amount_minor += bal
                    try:
                        days_over = (
                            datetime.fromisoformat(as_of).date()
                            - datetime.fromisoformat(adv.liquidation_due_date[:10]).date()
                        ).days
                    except Exception:
                        days_over = 1
                    if days_over <= 30:
                        aging_buckets["1_30_days"] += bal
                    elif days_over <= 60:
                        aging_buckets["31_60_days"] += bal
                    else:
                        aging_buckets["over_60_days"] += bal
                else:
                    aging_buckets["current"] += bal

        # 3. On-time liquidation rate & turnaround
        due_cohort = [
            a for a in advances
            if a.payment_status == "evidence_verified"
            and a.liquidation_due_date
            and (p_start <= a.liquidation_due_date <= p_end)
        ]
        on_time_count = 0
        turnaround_days_list: list[float] = []
        for a in due_cohort:
            apps = db.query(SettlementApplication).filter(SettlementApplication.advance_id == a.id).all()
            rep_ids = [ap.expense_report_id for ap in apps if ap.expense_report_id]
            if rep_ids:
                reps = db.query(ExpenseReport).filter(ExpenseReport.id.in_(rep_ids)).all()
                earliest_sub = min(
                    (r.submitted_at.strftime("%Y-%m-%d") for r in reps if r.submitted_at),
                    default=None,
                )
                if earliest_sub and earliest_sub <= a.liquidation_due_date:
                    on_time_count += 1
            elif a.settlement_status in ("reconciled", "closed"):
                on_time_count += 1

        on_time_rate = round((on_time_count / len(due_cohort)) * 100.0, 2) if due_cohort else 100.0

        # Turnaround across all advances with verified release and reviewed report
        for a in advances:
            if a.payment_status != "evidence_verified":
                continue
            disb = (
                db.query(Disbursement)
                .filter(Disbursement.advance_id == a.id, Disbursement.status == "evidence_verified")
                .first()
            )
            apps = db.query(SettlementApplication).filter(SettlementApplication.advance_id == a.id).all()
            rep_ids = [ap.expense_report_id for ap in apps if ap.expense_report_id]
            if disb and disb.evidence_verified_at and rep_ids:
                reps = (
                    db.query(ExpenseReport)
                    .filter(ExpenseReport.id.in_(rep_ids), ExpenseReport.reviewed_at != None)  # noqa: E711
                    .all()
                )
                for r in reps:
                    if r.reviewed_at:
                        delta = (r.reviewed_at - disb.evidence_verified_at).total_seconds() / 86400.0
                        turnaround_days_list.append(max(0.0, round(delta, 2)))

        avg_turnaround_days = (
            round(sum(turnaround_days_list) / len(turnaround_days_list), 2)
            if turnaround_days_list
            else 0.0
        )

        # 4. Expense reports, rejection rate & missing document rate
        reports = self._scope_reports_query(db, actor, department_id, cost_center_id, employee_id)
        period_reports = [
            r for r in reports
            if p_start <= r.created_at.strftime("%Y-%m-%d") <= p_end
        ]
        rep_ids_all = [r.id for r in period_reports]
        reviewed_lines_count = 0
        rejected_or_adjusted_lines_count = 0
        if rep_ids_all:
            all_lines = db.query(ExpenseLine).filter(ExpenseLine.expense_report_id.in_(rep_ids_all)).all()
            for l in all_lines:
                if l.line_status in ("approved", "partially_approved", "rejected", "returned"):
                    reviewed_lines_count += 1
                    if l.line_status in ("rejected", "returned", "partially_approved"):
                        rejected_or_adjusted_lines_count += 1

        expense_rejection_rate_pct = (
            round((rejected_or_adjusted_lines_count / reviewed_lines_count) * 100.0, 2)
            if reviewed_lines_count > 0
            else 0.0
        )

        evaluated_reports = [r for r in period_reports if r.review_status != "cancelled" and r.policy_evaluation_json]
        missing_doc_reports = 0
        for r in evaluated_reports:
            ev = r.to_dict().get("policy_evaluation") or {}
            if any(
                res.get("rule_id") in ("CONSOLIDATED_RECEIPT_PDF", "OUT_OF_BASE_MFAR")
                and res.get("outcome") == "violated"
                for res in ev.get("results", [])
            ):
                missing_doc_reports += 1
        missing_document_rate_pct = (
            round((missing_doc_reports / len(evaluated_reports)) * 100.0, 2)
            if evaluated_reports
            else 0.0
        )

        # 5. Purchase-to-Pay KPIs
        prs = self._scope_prs_query(db, actor, department_id, cost_center_id)
        pr_ids = [p.id for p in prs]
        prs_awaiting_approval_count = sum(1 for p in prs if p.approval_status == "submitted")

        validated_payables_awaiting_payment_minor = 0
        confirmed_vendor_payments_minor = 0
        unreconciled_payment_amount_minor = 0

        if pr_ids:
            payables = db.query(AccountPayable).filter(AccountPayable.purchase_request_id.in_(pr_ids)).all()
            for ap in payables:
                pays = db.query(PaymentRecord).filter(PaymentRecord.payable_id == ap.id).all()
                confirmed_for_ap = 0
                for p in pays:
                    if p.status == "reconciled" and p.reconciliation_status == "matched":
                        confirmed_for_ap += int(p.executed_amount_minor or 0)
                        ver_d = (p.evidence_verified_at or p.created_at).strftime("%Y-%m-%d")
                        if p_start <= ver_d <= p_end:
                            confirmed_vendor_payments_minor += int(p.executed_amount_minor or 0)
                    elif p.status in ("paid_unverified", "payment_unknown") or p.reconciliation_status == "discrepancy":
                        unreconciled_payment_amount_minor += int(p.executed_amount_minor or p.payment_amount_minor)

                if ap.status in ("open", "partially_paid", "paid_unverified"):
                    validated_payables_awaiting_payment_minor += max(0, int(ap.net_payable_minor) - confirmed_for_ap)

        # Include unverified/unknown advance disbursements in unreconciled_payment_amount_minor
        if adv_ids:
            unrec_disbs = (
                db.query(Disbursement)
                .filter(
                    Disbursement.advance_id.in_(adv_ids),
                    Disbursement.status.in_(["executed_unverified", "unknown"]),
                )
                .all()
            )
            unreconciled_payment_amount_minor += sum(
                int(d.executed_amount_minor or d.payment_amount_minor) for d in unrec_disbs
            )

        # 6. Exception Register counts
        exc_q = db.query(ExceptionRegister).filter(
            ExceptionRegister.status.in_(["open", "under_review", "reopened"])
        )
        if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "maker", "verifier", "bank_approver", "evidence_verifier", "auditor"):
            exc_q = exc_q.filter(ExceptionRegister.owner_id == actor.id)
        elif department_id and department_id != "all":
            exc_q = exc_q.filter(ExceptionRegister.department_id == department_id)
        open_exceptions = exc_q.all()
        duplicate_alerts_count = sum(
            1
            for e in open_exceptions
            if e.exception_category in ("duplicate_receipt_alert", "duplicate_invoice_alert", "duplicate_payment_alert")
        )

        return {
            "filters_applied": {
                "start_date": p_start,
                "end_date": p_end,
                "as_of_date": as_of,
                "department_id": department_id or "all",
                "cost_center_id": cost_center_id or "all",
                "employee_id": employee_id or "all",
            },
            "kpis": {
                "advances_released_minor": advances_released_minor,
                "released_disbursements_count": released_disbursements_count,
                "outstanding_advance_balance_minor": outstanding_advance_balance_minor,
                "overdue_advances_count": overdue_advances_count,
                "overdue_advances_amount_minor": overdue_advances_amount_minor,
                "unused_advances_awaiting_return_minor": unused_advances_awaiting_return_minor,
                "on_time_liquidation_rate_pct": on_time_rate,
                "average_liquidation_turnaround_days": avg_turnaround_days,
                "expense_rejection_rate_pct": expense_rejection_rate_pct,
                "missing_document_rate_pct": missing_document_rate_pct,
                "purchase_requests_awaiting_approval_count": prs_awaiting_approval_count,
                "validated_payables_awaiting_payment_minor": validated_payables_awaiting_payment_minor,
                "confirmed_vendor_payments_minor": confirmed_vendor_payments_minor,
                "unreconciled_payment_amount_minor": unreconciled_payment_amount_minor,
                "duplicate_payment_alerts_count": duplicate_alerts_count,
                "open_exceptions_count": len(open_exceptions),
            },
            "aging_buckets_minor": aging_buckets,
            "workflow_counts": {
                "cash_advances_total": len(advances),
                "cash_advances_approved_unreleased": sum(
                    1 for a in advances if a.approval_status == "approved" and a.payment_status != "evidence_verified"
                ),
                "cash_advances_released": sum(1 for a in advances if a.payment_status == "evidence_verified"),
                "cash_advances_closed": sum(1 for a in advances if a.settlement_status == "closed"),
                "expense_reports_total": len(period_reports),
                "expense_reports_approved": sum(
                    1 for r in period_reports if r.review_status in ("approved", "partially_approved")
                ),
                "purchase_requests_total": len(prs),
            },
            "generated_at": utc_now().isoformat(),
        }

    def get_centralized_tracker(
        self,
        db: Session,
        actor: ResolvedActor,
        workflow: Optional[str] = None,
        department_id: Optional[str] = None,
        status: Optional[str] = None,
        search: Optional[str] = None,
    ) -> list[dict[str, Any]]:
        rows: list[dict[str, Any]] = []

        if not workflow or workflow in ("all", "cash_advance"):
            advances = self._scope_advances_query(db, actor, department_id=department_id)
            for adv in advances:
                recon = compute_advance_reconciliation(db, adv)
                row = {
                    "record_id": adv.id,
                    "reference_number": adv.advance_number,
                    "workflow_type": "cash_advance",
                    "employee_or_payee": adv.employee_id,
                    "department_id": adv.department_id,
                    "cost_center_id": adv.cost_center_id,
                    "purpose_or_category": adv.purpose_code,
                    "requested_amount_minor": int(adv.requested_amount_minor),
                    "approved_amount_minor": int(adv.approved_amount_minor or 0),
                    "released_or_paid_minor": int(recon["released_amount_minor"]),
                    "claimed_expenses_minor": int(recon["total_claimed_minor"]),
                    "approved_expenses_minor": int(recon["approved_expenses_applied_minor"]),
                    "returned_funds_minor": int(recon["cash_returned_minor"]),
                    "reimbursement_due_minor": int(recon["additional_reimbursement_unsettled_minor"]),
                    "outstanding_balance_minor": int(recon["outstanding_balance_minor"]),
                    "approval_status": adv.approval_status,
                    "payment_status": adv.payment_status,
                    "review_status": adv.liquidation_review_status,
                    "settlement_status": adv.settlement_status,
                    "posting_status": adv.posting_status,
                    "overall_stage": adv.derive_overall_stage(),
                    "due_date": adv.liquidation_due_date,
                    "is_overdue": bool(recon["is_overdue"]),
                    "linked_references": recon["applied_report_ids"],
                    "closed_at": adv.closed_at.isoformat() if adv.closed_at else None,
                    "created_at": adv.created_at.isoformat() if adv.created_at else None,
                }
                rows.append(row)

        if not workflow or workflow in ("all", "purchase_to_pay"):
            prs = self._scope_prs_query(db, actor, department_id=department_id)
            for pr in prs:
                vendor = db.query(Vendor).filter(Vendor.id == pr.vendor_id).first()
                aps = db.query(AccountPayable).filter(AccountPayable.purchase_request_id == pr.id).all()
                paid_minor = 0
                net_payable = 0
                ap_refs = []
                for ap in aps:
                    ap_refs.append(ap.payable_number)
                    net_payable += int(ap.net_payable_minor)
                    pays = db.query(PaymentRecord).filter(PaymentRecord.payable_id == ap.id).all()
                    paid_minor += sum(
                        int(p.executed_amount_minor or 0)
                        for p in pays
                        if p.status == "reconciled" and p.reconciliation_status == "matched"
                    )
                outstanding_ap = max(0, net_payable - paid_minor) if aps else 0
                row = {
                    "record_id": pr.id,
                    "reference_number": pr.request_number,
                    "workflow_type": "purchase_to_pay",
                    "employee_or_payee": vendor.registered_name if vendor else pr.vendor_id,
                    "department_id": pr.department_id,
                    "cost_center_id": pr.cost_center_id,
                    "purpose_or_category": pr.payment_category,
                    "requested_amount_minor": int(pr.requested_amount_minor),
                    "approved_amount_minor": int(pr.approved_amount_minor or 0),
                    "released_or_paid_minor": paid_minor,
                    "claimed_expenses_minor": net_payable,
                    "approved_expenses_minor": net_payable,
                    "returned_funds_minor": 0,
                    "reimbursement_due_minor": 0,
                    "outstanding_balance_minor": outstanding_ap,
                    "approval_status": pr.approval_status,
                    "payment_status": pr.workflow_status,
                    "review_status": "validated" if aps else "pending",
                    "settlement_status": "reconciled" if pr.workflow_status == "reconciled" else "open",
                    "posting_status": pr.posting_status,
                    "overall_stage": pr.workflow_status,
                    "due_date": pr.due_date,
                    "is_overdue": bool(pr.due_date and pr.due_date < manila_today_str() and outstanding_ap > 0),
                    "linked_references": ap_refs,
                    "closed_at": None,
                    "created_at": pr.created_at.isoformat() if pr.created_at else None,
                }
                rows.append(row)

        if status and status != "all":
            rows = [
                r for r in rows
                if status.lower() in (
                    str(r["approval_status"]).lower(),
                    str(r["payment_status"]).lower(),
                    str(r["settlement_status"]).lower(),
                    str(r["overall_stage"]).lower(),
                )
            ]
        if search and search.strip():
            q = search.strip().lower()
            rows = [
                r for r in rows
                if q in str(r["reference_number"]).lower()
                or q in str(r["employee_or_payee"]).lower()
                or q in str(r["purpose_or_category"]).lower()
            ]
        return rows

    def list_exception_register(
        self,
        db: Session,
        actor: ResolvedActor,
        status: Optional[str] = None,
    ) -> list[dict[str, Any]]:
        q = db.query(ExceptionRegister)
        if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "maker", "verifier", "bank_approver", "evidence_verifier", "auditor"):
            q = q.filter(ExceptionRegister.owner_id == actor.id)
        if status and status != "all":
            q = q.filter(ExceptionRegister.status == status)
        items = q.order_by(ExceptionRegister.detected_at.desc()).all()
        return [i.to_dict() for i in items]

    def resolve_exception_register_item(
        self,
        db: Session,
        actor: ResolvedActor,
        register_id: str,
        target_status: str,  # under_review | resolved | reopened
        resolution_notes: str,
        evidence_doc_id: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "finance_reviewer", "finance_controller", action_desc="update or resolve an exception register item")
        if target_status not in ("under_review", "resolved", "reopened"):
            raise FinanceValidationError("target_status must be 'under_review', 'resolved', or 'reopened'.")
        if not resolution_notes or len(resolution_notes.strip()) < 5:
            raise FinanceValidationError("Detailed resolution_notes are required.")

        reg = db.query(ExceptionRegister).filter(ExceptionRegister.id == register_id).first()
        if not reg:
            raise FinanceNotFoundError("Exception register item not found.")

        prev = reg.status
        reg.status = target_status
        reg.resolution_notes = resolution_notes.strip()
        if evidence_doc_id:
            reg.evidence_doc_id = evidence_doc_id
        reg.reviewer_id = actor.id
        if target_status == "resolved":
            reg.resolved_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"exception_register_{target_status}",
            entity_type="exception_register",
            entity_id=reg.id,
            previous_state={"status": prev},
            new_state={"status": target_status},
            reason=resolution_notes.strip(),
            evidence_ref=evidence_doc_id,
        )
        db.commit()
        db.refresh(reg)
        return reg.to_dict()

    def export_tracker_csv(
        self,
        db: Session,
        actor: ResolvedActor,
        workflow: Optional[str] = None,
        department_id: Optional[str] = None,
        status: Optional[str] = None,
    ) -> str:
        rows = self.get_centralized_tracker(
            db=db,
            actor=actor,
            workflow=workflow,
            department_id=department_id,
            status=status,
        )
        buf = io.StringIO()
        writer = csv.writer(buf, quoting=csv.QUOTE_MINIMAL)
        writer.writerow([
            "Reference Number",
            "Workflow",
            "Employee / Payee",
            "Department",
            "Cost Center",
            "Category / Purpose",
            "Requested (PHP)",
            "Approved (PHP)",
            "Released / Paid (PHP)",
            "Claimed / Payable (PHP)",
            "Approved Expenses (PHP)",
            "Returned Funds (PHP)",
            "Outstanding Balance (PHP)",
            "Approval Status",
            "Payment Status",
            "Settlement Status",
            "Overall Stage",
            "Due Date",
            "Overdue",
        ])
        for r in rows:
            writer.writerow([
                _sanitize_csv_cell(r["reference_number"]),
                _sanitize_csv_cell(r["workflow_type"]),
                _sanitize_csv_cell(r["employee_or_payee"]),
                _sanitize_csv_cell(r["department_id"]),
                _sanitize_csv_cell(r["cost_center_id"]),
                _sanitize_csv_cell(r["purpose_or_category"]),
                _minor_to_php_decimal_str(r["requested_amount_minor"]),
                _minor_to_php_decimal_str(r["approved_amount_minor"]),
                _minor_to_php_decimal_str(r["released_or_paid_minor"]),
                _minor_to_php_decimal_str(r["claimed_expenses_minor"]),
                _minor_to_php_decimal_str(r["approved_expenses_minor"]),
                _minor_to_php_decimal_str(r["returned_funds_minor"]),
                _minor_to_php_decimal_str(r["outstanding_balance_minor"]),
                _sanitize_csv_cell(r["approval_status"]),
                _sanitize_csv_cell(r["payment_status"]),
                _sanitize_csv_cell(r["settlement_status"]),
                _sanitize_csv_cell(r["overall_stage"]),
                _sanitize_csv_cell(r["due_date"] or ""),
                "YES" if r["is_overdue"] else "NO",
            ])

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="finance_tracker_csv_exported",
            entity_type="report_export",
            entity_id=workflow or "all",
            new_state={"row_count": len(rows), "department_id": department_id or "all"},
        )
        db.commit()
        # Prepend UTF-8 Byte Order Mark (\uFEFF) per accounting-invariants-and-csv-encoding.md
        return "\ufeff" + buf.getvalue()


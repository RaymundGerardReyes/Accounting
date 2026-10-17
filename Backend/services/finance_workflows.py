import json
import uuid
from typing import Any, Optional

from sqlalchemy.orm import Session
from sqlalchemy.exc import IntegrityError

from finance_models import (
    FinanceUser,
    CashAdvance,
    ApprovalHistory,
    Disbursement,
    AdvanceLedgerEntry,
    DocumentReference,
    ExpenseReport,
    ExpenseLine,
    SettlementApplication,
    ClosingSnapshot,
    Vendor,
    VendorBankChange,
    TaxCode,
    PurchaseRequest,
    PurchaseRequestLine,
    AccountPayable,
    PaymentRecord,
    ExceptionRegister,
    ExceptionRequest,
    utc_now,
)
from services.finance_common import (
    ResolvedActor,
    FinanceNotFoundError,
    FinanceForbiddenError,
    FinanceConflictError,
    FinanceValidationError,
    require_roles,
    check_approval_matrix,
    assert_distinct_actors,
    next_sequence_number,
    record_audit_event,
    manila_today_str,
    compute_liquidation_due_date,
)
from services.policy_engine import PolicyEngineService


# ==============================================================================
# Explicit State Transition Tables
# ==============================================================================

VALID_ADVANCE_APPROVAL_TRANSITIONS: dict[str, set[str]] = {
    "draft": {"submitted", "cancelled"},
    "submitted": {"approved", "rejected", "returned_for_correction", "cancelled"},
    "returned_for_correction": {"submitted", "cancelled"},
    "approved": {"cancelled"},
    "rejected": set(),
    "cancelled": set(),
}

VALID_DISBURSEMENT_TRANSITIONS: dict[str, set[str]] = {
    "prepared": {"verified", "cancelled"},
    "verified": {"bank_approved", "cancelled"},
    "bank_approved": {"executed_unverified", "failed", "unknown", "cancelled"},
    "executed_unverified": {"evidence_verified", "failed", "unknown"},
    "unknown": {"executed_unverified", "failed"},
    "evidence_verified": set(),
    "failed": set(),
    "cancelled": set(),
}

VALID_REPORT_REVIEW_TRANSITIONS: dict[str, set[str]] = {
    "draft": {"submitted", "cancelled"},
    "submitted": {"finance_review", "returned_for_correction", "approved", "partially_approved", "rejected", "cancelled"},
    "finance_review": {"returned_for_correction", "approved", "partially_approved", "rejected"},
    "returned_for_correction": {"submitted", "cancelled"},
    "approved": set(),
    "partially_approved": set(),
    "rejected": set(),
    "cancelled": set(),
}

VALID_PR_APPROVAL_TRANSITIONS: dict[str, set[str]] = {
    "draft": {"submitted", "cancelled"},
    "submitted": {"approved", "rejected", "returned_for_correction", "cancelled"},
    "returned_for_correction": {"submitted", "cancelled"},
    "approved": {"cancelled"},
    "rejected": set(),
    "cancelled": set(),
}

VALID_PAYMENT_RECORD_TRANSITIONS: dict[str, set[str]] = {
    "prepared": {"verified", "cancelled"},
    "verified": {"bank_approved", "cancelled"},
    "bank_approved": {"paid_unverified", "failed", "payment_unknown", "cancelled"},
    "paid_unverified": {"reconciled", "failed", "payment_unknown"},
    "payment_unknown": {"paid_unverified", "failed"},
    "reconciled": set(),
    "failed": set(),
    "cancelled": set(),
}

ALLOWLISTED_ADVANCE_PURPOSES = {
    "school_visit",
    "transportation_field_activity",
    "accommodation",
    "representation",
    "other_approved_activity",
}

ALLOWLISTED_PR_CATEGORIES = {
    "supplier_invoice": {"restricted": False, "requires_tin": False, "tax_code_required": False},
    "office_supplies_utilities": {"restricted": False, "requires_tin": False, "tax_code_required": False},
    "school_support": {"restricted": False, "requires_tin": False, "tax_code_required": False},
    "royalties_honoraria": {"restricted": False, "requires_tin": True, "tax_code_required": True},
    "referral_fees": {"restricted": False, "requires_tin": True, "tax_code_required": True},
    "payroll_statutory": {"restricted": True, "requires_tin": False, "tax_code_required": False},
}


def _create_exception_register_entry(
    db: Session,
    entity_type: str,
    entity_id: str,
    exception_category: str,
    severity: str,
    reason: str,
    owner_id: Optional[str] = None,
    department_id: Optional[str] = None,
    amount_minor: int = 0,
    evidence_doc_id: Optional[str] = None,
) -> ExceptionRegister:
    existing = (
        db.query(ExceptionRegister)
        .filter(
            ExceptionRegister.entity_type == entity_type,
            ExceptionRegister.entity_id == entity_id,
            ExceptionRegister.exception_category == exception_category,
            ExceptionRegister.status.in_(["open", "under_review", "reopened"]),
        )
        .first()
    )
    if existing:
        existing.amount_minor = int(amount_minor)
        existing.reason = reason
        db.flush()
        return existing

    reg = ExceptionRegister(
        id=str(uuid.uuid4()),
        register_number=next_sequence_number(db, "EXQ"),
        entity_type=entity_type,
        entity_id=entity_id,
        exception_category=exception_category,
        severity=severity,
        owner_id=owner_id,
        department_id=department_id,
        amount_minor=int(amount_minor),
        reason=reason,
        evidence_doc_id=evidence_doc_id,
        status="open",
        detected_at=utc_now(),
    )
    db.add(reg)
    db.flush()
    return reg


# ==============================================================================
# Derived Advance Reconciliation & Accounting Preview
# ==============================================================================

def compute_advance_reconciliation(db: Session, advance: CashAdvance) -> dict[str, Any]:
    ledger_entries = (
        db.query(AdvanceLedgerEntry)
        .filter(AdvanceLedgerEntry.advance_id == advance.id)
        .order_by(AdvanceLedgerEntry.effective_at.asc())
        .all()
    )
    released_minor = sum(
        int(e.amount_minor) for e in ledger_entries if e.entry_type == "disbursement_release"
    )
    approved_applied_minor = sum(
        int(e.amount_minor) for e in ledger_entries if e.entry_type == "expense_applied"
    )
    cash_returned_minor = sum(
        int(e.amount_minor) for e in ledger_entries if e.entry_type == "cash_return"
    )
    reimbursement_payable_minor = sum(
        int(e.amount_minor) for e in ledger_entries if e.entry_type == "reimbursement_payable"
    )
    reimbursement_settled_minor = sum(
        int(e.amount_minor) for e in ledger_entries if e.entry_type == "reimbursement_settled"
    )
    adjustments_signed_minor = sum(
        int(e.signed_amount_minor) for e in ledger_entries if e.entry_type in ("adjustment", "reversal")
    )

    # Derived outstanding employee receivable balance:
    # Released - Approved Applied - Cash Returned + Signed Adjustments
    raw_outstanding_minor = (
        released_minor - approved_applied_minor - cash_returned_minor + adjustments_signed_minor
    )
    outstanding_balance_minor = max(0, raw_outstanding_minor)

    # Also compute linked report/line stats for visibility
    apps = (
        db.query(SettlementApplication)
        .filter(SettlementApplication.advance_id == advance.id)
        .all()
    )
    applied_report_ids = sorted({a.expense_report_id for a in apps if a.expense_report_id})
    claimed_minor = 0
    rejected_minor = 0
    pending_review_minor = 0
    if applied_report_ids:
        rep_lines = (
            db.query(ExpenseLine)
            .filter(ExpenseLine.expense_report_id.in_(applied_report_ids))
            .all()
        )
        for rl in rep_lines:
            claimed_minor += int(rl.claimed_amount_minor)
            if rl.line_status == "rejected":
                rejected_minor += int(rl.claimed_amount_minor)
            elif rl.line_status == "partially_approved":
                rejected_minor += max(0, int(rl.claimed_amount_minor) - int(rl.approved_amount_minor or 0))
            elif rl.line_status in ("pending", "returned"):
                pending_review_minor += int(rl.claimed_amount_minor)

    unused_to_return_minor = max(0, released_minor - approved_applied_minor)
    reimbursement_unsettled_minor = max(0, reimbursement_payable_minor - reimbursement_settled_minor)
    today = manila_today_str()
    is_overdue = (
        advance.payment_status == "evidence_verified"
        and advance.settlement_status not in ("reconciled", "closed")
        and bool(advance.liquidation_due_date)
        and advance.liquidation_due_date < today
    )

    # Accounting preview (never claims official ledger posting without integration)
    preview_entries = []
    if released_minor > 0:
        preview_entries.append({
            "event": "disbursement_release",
            "debit_account": "1140 - Advances to Employees (Receivable)",
            "credit_account": "1010 - Cash in Bank",
            "amount_minor": released_minor,
            "posting_status": advance.posting_status,
        })
    if approved_applied_minor > 0:
        preview_entries.append({
            "event": "expense_settlement",
            "debit_account": "6100 - Field & Operating Expenses",
            "credit_account": "1140 - Advances to Employees (Receivable)",
            "amount_minor": approved_applied_minor,
            "posting_status": advance.posting_status,
        })
    if cash_returned_minor > 0:
        preview_entries.append({
            "event": "unused_cash_return",
            "debit_account": "1010 - Cash in Bank",
            "credit_account": "1140 - Advances to Employees (Receivable)",
            "amount_minor": cash_returned_minor,
            "posting_status": advance.posting_status,
        })

    return {
        "advance_id": advance.id,
        "advance_number": advance.advance_number,
        "requested_amount_minor": int(advance.requested_amount_minor),
        "approved_amount_minor": int(advance.approved_amount_minor or 0),
        "released_amount_minor": released_minor,
        "total_claimed_minor": claimed_minor,
        "approved_expenses_applied_minor": approved_applied_minor,
        "rejected_expenses_minor": rejected_minor,
        "pending_review_expenses_minor": pending_review_minor,
        "unused_funds_expected_minor": unused_to_return_minor,
        "cash_returned_minor": cash_returned_minor,
        "additional_reimbursement_payable_minor": reimbursement_payable_minor,
        "additional_reimbursement_settled_minor": reimbursement_settled_minor,
        "additional_reimbursement_unsettled_minor": reimbursement_unsettled_minor,
        "outstanding_balance_minor": outstanding_balance_minor,
        "is_overdue": is_overdue,
        "liquidation_due_date": advance.liquidation_due_date,
        "applied_report_ids": applied_report_ids,
        "ledger_entry_count": len(ledger_entries),
        "accounting_preview": preview_entries,
    }


# ==============================================================================
# Cash Advance, Disbursement, Liquidation & P2P Orchestration Service
# ==============================================================================

class FinanceWorkflowService:
    def __init__(self):
        self.policy_engine = PolicyEngineService()

    # --------------------------------------------------------------------------
    # A. Cash Advance Lifecycle
    # --------------------------------------------------------------------------

    def create_cash_advance(
        self,
        db: Session,
        actor: ResolvedActor,
        purpose_code: str,
        business_justification: str,
        requested_amount_minor: int,
        date_needed: str,
        department_id: Optional[str] = None,
        cost_center_id: Optional[str] = None,
        destination_or_site: Optional[str] = None,
        activity_start_date: Optional[str] = None,
        activity_end_date: Optional[str] = None,
        remarks: Optional[str] = None,
        attachment_ids: Optional[list[str]] = None,
        idempotency_key: Optional[str] = None,
        submit_now: bool = False,
    ) -> dict[str, Any]:
        if idempotency_key:
            existing = db.query(CashAdvance).filter(CashAdvance.idempotency_key == idempotency_key).first()
            if existing:
                return self.get_cash_advance_detail(db, actor, existing.id)

        if purpose_code not in ALLOWLISTED_ADVANCE_PURPOSES:
            raise FinanceValidationError(
                f"Invalid purpose_code '{purpose_code}'. Allowed: {', '.join(sorted(ALLOWLISTED_ADVANCE_PURPOSES))}."
            )
        if not business_justification or len(business_justification.strip()) < 5:
            raise FinanceValidationError("Business justification must be at least 5 characters.")
        if int(requested_amount_minor) <= 0:
            raise FinanceValidationError("Requested amount must be greater than zero centavos.")
        if activity_start_date and activity_end_date and activity_end_date < activity_start_date:
            raise FinanceValidationError("activity_end_date cannot precede activity_start_date.")

        pv = self.policy_engine.get_applicable_policy_version(db, "expense_liquidation", date_needed[:10])
        due_date = compute_liquidation_due_date(
            db,
            activity_end_date or date_needed,
            pv.submission_deadline_day_of_next_month,
        )

        adv = CashAdvance(
            id=str(uuid.uuid4()),
            advance_number=next_sequence_number(db, "CA"),
            requestor_id=actor.id,
            employee_id=actor.id,
            department_id=department_id or actor.user.department_id,
            cost_center_id=cost_center_id or actor.user.cost_center_id,
            purpose_code=purpose_code,
            business_justification=business_justification.strip(),
            destination_or_site=destination_or_site,
            requested_amount_minor=int(requested_amount_minor),
            approved_amount_minor=None,
            currency="PHP",
            date_needed=date_needed[:10],
            activity_start_date=activity_start_date[:10] if activity_start_date else None,
            activity_end_date=activity_end_date[:10] if activity_end_date else None,
            liquidation_due_date=due_date,
            remarks=remarks,
            attachment_ids_json=json.dumps(attachment_ids or []),
            approval_status="draft",
            payment_status="not_started",
            liquidation_review_status="not_started",
            settlement_status="open",
            posting_status="not_applicable",
            idempotency_key=idempotency_key,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        db.add(adv)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="cash_advance_created",
            entity_type="cash_advance",
            entity_id=adv.id,
            new_state={"advance_number": adv.advance_number, "requested_amount_minor": adv.requested_amount_minor},
        )
        db.commit()
        if submit_now:
            return self.submit_cash_advance(db, actor, adv.id)
        return self.get_cash_advance_detail(db, actor, adv.id)

    def submit_cash_advance(self, db: Session, actor: ResolvedActor, advance_id: str) -> dict[str, Any]:
        adv = db.query(CashAdvance).filter(CashAdvance.id == advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Cash advance not found.")
        if adv.requestor_id != actor.id:
            raise FinanceForbiddenError("Only the requestor may submit their own cash advance request.")
        if "submitted" not in VALID_ADVANCE_APPROVAL_TRANSITIONS.get(adv.approval_status, set()):
            raise FinanceConflictError(
                f"Cannot submit cash advance from approval_status '{adv.approval_status}'."
            )

        eval_res = self.policy_engine.evaluate_cash_advance(db, adv)
        adv.policy_version_id = eval_res["policy_version_id"]
        adv.policy_evaluation_json = json.dumps(eval_res)
        if not eval_res["is_compliant"]:
            record_audit_event(
                db=db,
                actor_id=actor.id,
                actor_role=actor.primary_role,
                action="cash_advance_submission_blocked_by_policy",
                entity_type="cash_advance",
                entity_id=adv.id,
                new_state=eval_res,
            )
            db.commit()
            raise FinanceValidationError(
                "Cash advance submission blocked by policy evaluation.",
                details=eval_res,
            )

        prev = adv.approval_status
        adv.approval_status = "submitted"
        adv.submitted_at = utc_now()
        adv.updated_at = utc_now()
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="cash_advance_submitted",
            entity_type="cash_advance",
            entity_id=adv.id,
            previous_state={"approval_status": prev},
            new_state={"approval_status": "submitted", "policy_version_id": adv.policy_version_id},
        )
        db.commit()
        return self.get_cash_advance_detail(db, actor, adv.id)

    def decide_cash_advance(
        self,
        db: Session,
        actor: ResolvedActor,
        advance_id: str,
        decision: str,  # approved | rejected | returned_for_correction | cancelled
        comments: str,
        approved_amount_minor: Optional[int] = None,
    ) -> dict[str, Any]:
        adv = db.query(CashAdvance).filter(CashAdvance.id == advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Cash advance not found.")
        if decision not in VALID_ADVANCE_APPROVAL_TRANSITIONS.get(adv.approval_status, set()):
            raise FinanceConflictError(
                f"Invalid state transition from '{adv.approval_status}' to '{decision}'."
            )
        if not comments or len(comments.strip()) < 3:
            raise FinanceValidationError("Decision reason / comments are required.")

        if decision == "cancelled":
            if adv.payment_status not in ("not_started", "cancelled"):
                raise FinanceConflictError("Cannot cancel an advance that already has an active disbursement.")
            if actor.id != adv.requestor_id and not actor.has_role("supervisor", "finance_controller"):
                raise FinanceForbiddenError("Not authorized to cancel this cash advance.")
        else:
            assert_distinct_actors(
                db=db,
                actor=actor,
                prior_actors={"requestor": adv.requestor_id, "employee": adv.employee_id},
                current_duty_label="business_approver",
                entity_type="cash_advance",
                entity_id=adv.id,
            )
            target_amt = (
                int(approved_amount_minor)
                if approved_amount_minor is not None
                else int(adv.requested_amount_minor)
            )
            check_approval_matrix(
                db=db,
                actor=actor,
                workflow="cash_advance",
                stage="business_approval",
                amount_minor=target_amt,
                department_id=adv.department_id,
            )
            if decision == "approved":
                if target_amt <= 0 or target_amt > int(adv.requested_amount_minor):
                    raise FinanceValidationError(
                        f"Approved amount ({target_amt}) must be > 0 and <= requested amount ({adv.requested_amount_minor})."
                    )
                adv.approved_amount_minor = target_amt
                adv.approved_at = utc_now()

        prev = adv.approval_status
        adv.approval_status = decision
        adv.updated_at = utc_now()

        db.add(
            ApprovalHistory(
                entity_type="cash_advance",
                entity_id=adv.id,
                approver_id=actor.id,
                approver_role=actor.primary_role,
                approval_stage="business_approval",
                decision=decision,
                approved_amount_minor=adv.approved_amount_minor if decision == "approved" else None,
                comments=comments.strip(),
                previous_status=prev,
                resulting_status=decision,
                decided_at=utc_now(),
            )
        )
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"cash_advance_{decision}",
            entity_type="cash_advance",
            entity_id=adv.id,
            previous_state={"approval_status": prev},
            new_state={"approval_status": decision, "approved_amount_minor": adv.approved_amount_minor},
            reason=comments.strip(),
        )
        db.commit()
        return self.get_cash_advance_detail(db, actor, adv.id)

    # --------------------------------------------------------------------------
    # B. Bank Disbursement Controls (Maker -> Verifier -> Bank Approver -> Evidence)
    # --------------------------------------------------------------------------

    def _get_advance_approver_ids(self, db: Session, advance_id: str) -> dict[str, Optional[str]]:
        decisions = (
            db.query(ApprovalHistory)
            .filter(
                ApprovalHistory.entity_type == "cash_advance",
                ApprovalHistory.entity_id == advance_id,
                ApprovalHistory.decision == "approved",
            )
            .all()
        )
        return {f"business_approver_{idx}": d.approver_id for idx, d in enumerate(decisions, start=1)}

    def prepare_disbursement(
        self,
        db: Session,
        actor: ResolvedActor,
        advance_id: str,
        payment_amount_minor: int,
        payment_method: str = "bank_transfer",
        recipient_account_ref: Optional[str] = None,
        idempotency_key: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", action_desc="prepare a bank disbursement")
        if idempotency_key:
            existing = db.query(Disbursement).filter(Disbursement.idempotency_key == idempotency_key).first()
            if existing:
                return existing.to_dict()

        adv = db.query(CashAdvance).filter(CashAdvance.id == advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Cash advance not found.")
        if adv.approval_status != "approved" or not adv.approved_amount_minor:
            raise FinanceConflictError("Disbursement can only be prepared for an approved cash advance.")

        prior_actors = {
            "requestor": adv.requestor_id,
            "employee": adv.employee_id,
            **self._get_advance_approver_ids(db, adv.id),
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="maker",
            entity_type="disbursement",
            entity_id=adv.id,
        )

        existing_disb = (
            db.query(Disbursement)
            .filter(
                Disbursement.advance_id == adv.id,
                Disbursement.status.notin_(["failed", "cancelled"]),
            )
            .all()
        )
        if any(d.status == "unknown" for d in existing_disb):
            raise FinanceConflictError(
                "An existing disbursement is in 'unknown' investigation state; investigate before preparing another payment."
            )
        existing_total = sum(int(d.executed_amount_minor or d.payment_amount_minor) for d in existing_disb)
        if int(payment_amount_minor) <= 0 or (existing_total + int(payment_amount_minor)) > int(adv.approved_amount_minor):
            raise FinanceValidationError(
                f"Disbursement amount ({payment_amount_minor}) plus active disbursements ({existing_total}) "
                f"cannot exceed approved amount ({adv.approved_amount_minor})."
            )

        disb = Disbursement(
            id=str(uuid.uuid4()),
            disbursement_number=next_sequence_number(db, "DISB"),
            advance_id=adv.id,
            payment_amount_minor=int(payment_amount_minor),
            currency=adv.currency,
            payment_method=payment_method,
            recipient_account_ref=recipient_account_ref,
            status="prepared",
            maker_id=actor.id,
            prepared_at=utc_now(),
            idempotency_key=idempotency_key,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        adv.payment_status = "prepared"
        adv.updated_at = utc_now()
        db.add(disb)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="disbursement_prepared",
            entity_type="disbursement",
            entity_id=disb.id,
            new_state={"disbursement_number": disb.disbursement_number, "payment_amount_minor": disb.payment_amount_minor},
        )
        db.commit()
        db.refresh(disb)
        return disb.to_dict()

    def verify_disbursement(
        self,
        db: Session,
        actor: ResolvedActor,
        disbursement_id: str,
        notes: str,
    ) -> dict[str, Any]:
        disb = db.query(Disbursement).filter(Disbursement.id == disbursement_id).first()
        if not disb:
            raise FinanceNotFoundError("Disbursement not found.")
        if disb.maker_id == actor.id:
            raise FinanceForbiddenError(
                f"Separation of duties violation: Maker '{actor.id}' cannot verify their own disbursement."
            )
        require_roles(actor, "verifier", action_desc="verify a bank disbursement")
        if "verified" not in VALID_DISBURSEMENT_TRANSITIONS.get(disb.status, set()):
            raise FinanceConflictError(f"Cannot verify disbursement from status '{disb.status}'.")

        adv = db.query(CashAdvance).filter(CashAdvance.id == disb.advance_id).first()
        prior_actors = {
            "requestor": adv.requestor_id if adv else None,
            "employee": adv.employee_id if adv else None,
            **(self._get_advance_approver_ids(db, adv.id) if adv else {}),
            "maker": disb.maker_id,
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="verifier",
            entity_type="disbursement",
            entity_id=disb.id,
        )

        prev = disb.status
        disb.status = "verified"
        disb.verifier_id = actor.id
        disb.verified_at = utc_now()
        disb.verification_notes = notes
        disb.updated_at = utc_now()
        if adv:
            adv.payment_status = "verified"
            adv.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="disbursement_verified",
            entity_type="disbursement",
            entity_id=disb.id,
            previous_state={"status": prev},
            new_state={"status": "verified"},
            reason=notes,
        )
        db.commit()
        db.refresh(disb)
        return disb.to_dict()

    def approve_disbursement_bank(
        self,
        db: Session,
        actor: ResolvedActor,
        disbursement_id: str,
        notes: str,
    ) -> dict[str, Any]:
        disb = db.query(Disbursement).filter(Disbursement.id == disbursement_id).first()
        if not disb:
            raise FinanceNotFoundError("Disbursement not found.")
        if "bank_approved" not in VALID_DISBURSEMENT_TRANSITIONS.get(disb.status, set()):
            raise FinanceConflictError(f"Cannot bank-approve disbursement from status '{disb.status}'.")

        check_approval_matrix(
            db=db,
            actor=actor,
            workflow="disbursement_bank",
            stage="bank_approval",
            amount_minor=int(disb.payment_amount_minor),
        )
        adv = db.query(CashAdvance).filter(CashAdvance.id == disb.advance_id).first()
        prior_actors = {
            "requestor": adv.requestor_id if adv else None,
            "employee": adv.employee_id if adv else None,
            **(self._get_advance_approver_ids(db, adv.id) if adv else {}),
            "maker": disb.maker_id,
            "verifier": disb.verifier_id,
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="bank_approver",
            entity_type="disbursement",
            entity_id=disb.id,
        )

        prev = disb.status
        disb.status = "bank_approved"
        disb.bank_approver_id = actor.id
        disb.bank_approved_at = utc_now()
        disb.bank_approval_notes = notes
        disb.updated_at = utc_now()
        if adv:
            adv.payment_status = "bank_approved"
            adv.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="disbursement_bank_approved",
            entity_type="disbursement",
            entity_id=disb.id,
            previous_state={"status": prev},
            new_state={"status": "bank_approved"},
            reason=notes,
        )
        db.commit()
        db.refresh(disb)
        return disb.to_dict()

    def record_disbursement_execution(
        self,
        db: Session,
        actor: ResolvedActor,
        disbursement_id: str,
        bank_reference: str,
        executed_amount_minor: int,
        evidence_document_id: str,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", "bank_approver", action_desc="record manual bank execution details")
        disb = db.query(Disbursement).filter(Disbursement.id == disbursement_id).first()
        if not disb:
            raise FinanceNotFoundError("Disbursement not found.")
        if "executed_unverified" not in VALID_DISBURSEMENT_TRANSITIONS.get(disb.status, set()):
            raise FinanceConflictError(f"Cannot record execution from disbursement status '{disb.status}'.")
        if not bank_reference or len(bank_reference.strip()) < 4:
            raise FinanceValidationError("A valid bank transaction reference is required.")
        if int(executed_amount_minor) <= 0 or int(executed_amount_minor) > int(disb.payment_amount_minor):
            raise FinanceValidationError(
                f"Executed amount ({executed_amount_minor}) must be > 0 and <= prepared amount ({disb.payment_amount_minor})."
            )

        # Prevent duplicate bank reference reuse
        dup_disb = (
            db.query(Disbursement)
            .filter(
                Disbursement.bank_reference == bank_reference.strip(),
                Disbursement.id != disb.id,
                Disbursement.status.notin_(["failed", "cancelled"]),
            )
            .first()
        )
        if dup_disb:
            raise FinanceConflictError(f"Bank reference '{bank_reference}' is already used on disbursement {dup_disb.disbursement_number}.")

        doc = db.query(DocumentReference).filter(DocumentReference.id == evidence_document_id).first()
        if not doc:
            raise FinanceValidationError("Attached bank remittance evidence document not found.")

        prev = disb.status
        disb.status = "executed_unverified"
        disb.bank_reference = bank_reference.strip()
        disb.executed_amount_minor = int(executed_amount_minor)
        disb.evidence_document_id = doc.id
        disb.executed_by_id = actor.id
        disb.executed_at = utc_now()
        disb.updated_at = utc_now()

        adv = db.query(CashAdvance).filter(CashAdvance.id == disb.advance_id).first()
        if adv:
            adv.payment_status = "executed_unverified"
            adv.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="disbursement_executed_unverified",
            entity_type="disbursement",
            entity_id=disb.id,
            previous_state={"status": prev},
            new_state={
                "status": "executed_unverified",
                "bank_reference": disb.bank_reference,
                "executed_amount_minor": disb.executed_amount_minor,
            },
            evidence_ref=doc.document_number,
        )
        db.commit()
        db.refresh(disb)
        return disb.to_dict()

    def verify_disbursement_evidence(
        self,
        db: Session,
        actor: ResolvedActor,
        disbursement_id: str,
        notes: str,
    ) -> dict[str, Any]:
        require_roles(actor, "evidence_verifier", "finance_reviewer", "finance_controller", action_desc="verify bank execution evidence")
        disb = db.query(Disbursement).filter(Disbursement.id == disbursement_id).first()
        if not disb:
            raise FinanceNotFoundError("Disbursement not found.")
        if "evidence_verified" not in VALID_DISBURSEMENT_TRANSITIONS.get(disb.status, set()):
            raise FinanceConflictError(
                f"Disbursement must be in 'executed_unverified' status before evidence verification (current: '{disb.status}')."
            )
        if not disb.bank_reference or not disb.evidence_document_id or not disb.executed_amount_minor:
            raise FinanceValidationError("Bank reference, executed amount, and remittance evidence document are required.")

        adv = db.query(CashAdvance).filter(CashAdvance.id == disb.advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Related cash advance not found.")

        # Enforce 6-person Separation of Duties
        prior_actors = {
            "requestor": adv.requestor_id,
            "employee": adv.employee_id,
            **self._get_advance_approver_ids(db, adv.id),
            "maker": disb.maker_id,
            "verifier": disb.verifier_id,
            "bank_approver": disb.bank_approver_id,
            "execution_recorder": disb.executed_by_id,
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="evidence_verifier",
            entity_type="disbursement",
            entity_id=disb.id,
        )

        prev = disb.status
        disb.status = "evidence_verified"
        disb.evidence_verifier_id = actor.id
        disb.evidence_verified_at = utc_now()
        disb.evidence_verification_notes = notes
        disb.updated_at = utc_now()

        adv.payment_status = "evidence_verified"
        adv.settlement_status = "settlement_pending"
        adv.posting_status = "pending"
        adv.updated_at = utc_now()

        # Append release entry to AdvanceLedgerEntry
        ledger_entry = AdvanceLedgerEntry(
            id=str(uuid.uuid4()),
            advance_id=adv.id,
            employee_id=adv.employee_id,
            entry_type="disbursement_release",
            signed_amount_minor=int(disb.executed_amount_minor),
            amount_minor=int(disb.executed_amount_minor),
            currency=disb.currency,
            source_type="disbursement",
            source_id=disb.id,
            reference_code=disb.bank_reference,
            evidence_document_id=disb.evidence_document_id,
            actor_id=actor.id,
            notes=notes or "Verified bank disbursement release",
            effective_at=utc_now(),
            created_at=utc_now(),
        )
        db.add(ledger_entry)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="disbursement_evidence_verified_and_released",
            entity_type="disbursement",
            entity_id=disb.id,
            previous_state={"status": prev},
            new_state={
                "status": "evidence_verified",
                "released_amount_minor": disb.executed_amount_minor,
                "ledger_entry_id": ledger_entry.id,
            },
            reason=notes,
            evidence_ref=disb.evidence_document_id,
        )
        db.commit()
        db.refresh(disb)
        return disb.to_dict()

    def mark_disbursement_exception_state(
        self,
        db: Session,
        actor: ResolvedActor,
        disbursement_id: str,
        target_status: str,  # failed | unknown | cancelled
        reason: str,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", "verifier", "bank_approver", "finance_controller", action_desc="update disbursement exception state")
        if target_status not in ("failed", "unknown", "cancelled"):
            raise FinanceValidationError("target_status must be 'failed', 'unknown', or 'cancelled'.")
        disb = db.query(Disbursement).filter(Disbursement.id == disbursement_id).first()
        if not disb:
            raise FinanceNotFoundError("Disbursement not found.")
        if target_status not in VALID_DISBURSEMENT_TRANSITIONS.get(disb.status, set()):
            raise FinanceConflictError(f"Cannot transition disbursement from '{disb.status}' to '{target_status}'.")

        prev = disb.status
        disb.status = target_status
        disb.failure_reason = reason
        disb.updated_at = utc_now()

        adv = db.query(CashAdvance).filter(CashAdvance.id == disb.advance_id).first()
        if adv:
            adv.payment_status = target_status
            adv.updated_at = utc_now()

        if target_status == "unknown":
            _create_exception_register_entry(
                db=db,
                entity_type="disbursement",
                entity_id=disb.id,
                exception_category="payment_unknown_investigation",
                severity="high",
                reason=f"Disbursement {disb.disbursement_number} outcome is unknown ({reason}). Investigate bank status before retrying.",
                owner_id=actor.id,
                department_id=adv.department_id if adv else None,
                amount_minor=int(disb.payment_amount_minor),
            )

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"disbursement_{target_status}",
            entity_type="disbursement",
            entity_id=disb.id,
            previous_state={"status": prev},
            new_state={"status": target_status},
            reason=reason,
        )
        db.commit()
        db.refresh(disb)
        return disb.to_dict()

    def get_cash_advance_detail(self, db: Session, actor: ResolvedActor, advance_id: str) -> dict[str, Any]:
        adv = db.query(CashAdvance).filter(CashAdvance.id == advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Cash advance not found.")
        if (
            adv.employee_id != actor.id
            and adv.requestor_id != actor.id
            and not actor.can_access_department(adv.department_id)
        ):
            raise FinanceForbiddenError("Not authorized to view this cash advance.")

        reconciliation = compute_advance_reconciliation(db, adv)
        approvals = (
            db.query(ApprovalHistory)
            .filter(ApprovalHistory.entity_type == "cash_advance", ApprovalHistory.entity_id == adv.id)
            .order_by(ApprovalHistory.decided_at.asc())
            .all()
        )
        disbs = (
            db.query(Disbursement)
            .filter(Disbursement.advance_id == adv.id)
            .order_by(Disbursement.created_at.asc())
            .all()
        )
        return adv.to_dict(
            reconciliation=reconciliation,
            approvals=[a.to_dict() for a in approvals],
            disbursements=[d.to_dict() for d in disbs],
        )

    # --------------------------------------------------------------------------
    # C. Monthly Expense Report, Receipt Duplicate Check & Oldest-First Settlement
    # --------------------------------------------------------------------------

    def create_expense_report(
        self,
        db: Session,
        actor: ResolvedActor,
        reporting_period: str,
        business_purpose_summary: str,
        out_of_base_activity: bool = False,
        consolidated_receipt_doc_id: Optional[str] = None,
        mfar_doc_id: Optional[str] = None,
        employee_declaration_confirmed: bool = False,
        remarks: Optional[str] = None,
        idempotency_key: Optional[str] = None,
    ) -> dict[str, Any]:
        if idempotency_key:
            existing = db.query(ExpenseReport).filter(ExpenseReport.idempotency_key == idempotency_key).first()
            if existing:
                return self.get_expense_report_detail(db, actor, existing.id)

        if not reporting_period or len(reporting_period.strip()) < 7:
            raise FinanceValidationError("reporting_period must be in YYYY-MM format.")
        if not business_purpose_summary or len(business_purpose_summary.strip()) < 5:
            raise FinanceValidationError("business_purpose_summary is required.")

        rep = ExpenseReport(
            id=str(uuid.uuid4()),
            report_number=next_sequence_number(db, "EXP"),
            employee_id=actor.id,
            department_id=actor.user.department_id,
            cost_center_id=actor.user.cost_center_id,
            reporting_period=reporting_period.strip()[:7],
            business_purpose_summary=business_purpose_summary.strip(),
            out_of_base_activity=1 if out_of_base_activity else 0,
            consolidated_receipt_doc_id=consolidated_receipt_doc_id,
            mfar_doc_id=mfar_doc_id,
            employee_declaration_confirmed=1 if employee_declaration_confirmed else 0,
            remarks=remarks,
            currency="PHP",
            review_status="draft",
            settlement_status="unapplied",
            posting_status="not_applicable",
            slot_consumed=0,
            idempotency_key=idempotency_key,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        db.add(rep)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="expense_report_created",
            entity_type="expense_report",
            entity_id=rep.id,
            new_state={"report_number": rep.report_number, "reporting_period": rep.reporting_period},
        )
        db.commit()
        return self.get_expense_report_detail(db, actor, rep.id)

    def add_expense_line(
        self,
        db: Session,
        actor: ResolvedActor,
        report_id: str,
        transaction_date: str,
        expense_category: str,
        business_purpose: str,
        merchant_or_payee: str,
        claimed_amount_minor: int,
        receipt_or_invoice_no: Optional[str] = None,
        school_or_destination: Optional[str] = None,
        out_of_base: bool = False,
        receipt_document_id: Optional[str] = None,
        mfar_document_id: Optional[str] = None,
    ) -> dict[str, Any]:
        rep = db.query(ExpenseReport).filter(ExpenseReport.id == report_id).first()
        if not rep:
            raise FinanceNotFoundError("Expense report not found.")
        if rep.employee_id != actor.id:
            raise FinanceForbiddenError("Only the report owner may add or edit expense lines.")
        if rep.review_status not in ("draft", "returned_for_correction"):
            raise FinanceConflictError(f"Cannot add expense lines when report status is '{rep.review_status}'.")
        if int(claimed_amount_minor) <= 0:
            raise FinanceValidationError("claimed_amount_minor must be a positive integer.")

        norm_merchant = (merchant_or_payee or "").strip().lower()
        norm_or = (receipt_or_invoice_no or "").strip().lower()
        fingerprint = f"{norm_merchant}|{norm_or}|{transaction_date[:10]}|{int(claimed_amount_minor)}"

        # Check company-wide duplicate receipt fingerprint or identical file SHA-256
        dup_line = None
        if norm_or:
            dup_line = (
                db.query(ExpenseLine)
                .filter(
                    ExpenseLine.fingerprint_key == fingerprint,
                )
                .first()
            )
        if not dup_line and receipt_document_id:
            doc = db.query(DocumentReference).filter(DocumentReference.id == receipt_document_id).first()
            if doc:
                same_hash_docs = (
                    db.query(DocumentReference)
                    .filter(
                        DocumentReference.sha256_hash == doc.sha256_hash,
                        DocumentReference.id != doc.id,
                    )
                    .all()
                )
                same_hash_ids = [d.id for d in same_hash_docs] + [doc.id]
                dup_line = (
                    db.query(ExpenseLine)
                    .filter(
                        ExpenseLine.receipt_document_id.in_(same_hash_ids),
                        ExpenseLine.expense_report_id != rep.id,
                    )
                    .first()
                )

        line = ExpenseLine(
            id=str(uuid.uuid4()),
            expense_report_id=rep.id,
            employee_id=rep.employee_id,
            transaction_date=transaction_date[:10],
            school_or_destination=school_or_destination,
            expense_category=expense_category.strip().lower(),
            business_purpose=business_purpose.strip(),
            merchant_or_payee=merchant_or_payee.strip(),
            receipt_or_invoice_no=receipt_or_invoice_no.strip() if receipt_or_invoice_no else None,
            fingerprint_key=fingerprint,
            claimed_amount_minor=int(claimed_amount_minor),
            approved_amount_minor=None,
            currency="PHP",
            out_of_base=1 if out_of_base else 0,
            receipt_document_id=receipt_document_id,
            mfar_document_id=mfar_document_id,
            duplicate_warning=1 if dup_line else 0,
            duplicate_of_line_id=dup_line.id if dup_line else None,
            line_status="pending",
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        if out_of_base:
            rep.out_of_base_activity = 1
        rep.updated_at = utc_now()
        db.add(line)
        db.flush()

        if dup_line:
            _create_exception_register_entry(
                db=db,
                entity_type="expense_line",
                entity_id=line.id,
                exception_category="duplicate_receipt_alert",
                severity="medium",
                reason=f"Expense line receipt ({merchant_or_payee} / OR {receipt_or_invoice_no}) matches existing line {dup_line.id}.",
                owner_id=rep.employee_id,
                department_id=rep.department_id,
                amount_minor=int(claimed_amount_minor),
                evidence_doc_id=receipt_document_id,
            )

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="expense_line_added",
            entity_type="expense_line",
            entity_id=line.id,
            new_state={
                "expense_report_id": rep.id,
                "claimed_amount_minor": line.claimed_amount_minor,
                "duplicate_warning": bool(line.duplicate_warning),
            },
        )
        db.commit()
        return self.get_expense_report_detail(db, actor, rep.id)

    def update_expense_report_metadata(
        self,
        db: Session,
        actor: ResolvedActor,
        report_id: str,
        consolidated_receipt_doc_id: Optional[str] = None,
        mfar_doc_id: Optional[str] = None,
        employee_declaration_confirmed: Optional[bool] = None,
        out_of_base_activity: Optional[bool] = None,
        remarks: Optional[str] = None,
    ) -> dict[str, Any]:
        rep = db.query(ExpenseReport).filter(ExpenseReport.id == report_id).first()
        if not rep:
            raise FinanceNotFoundError("Expense report not found.")
        if rep.employee_id != actor.id:
            raise FinanceForbiddenError("Only the report owner may update this expense report.")
        if rep.review_status not in ("draft", "returned_for_correction"):
            raise FinanceConflictError(f"Cannot edit report in status '{rep.review_status}'.")

        if consolidated_receipt_doc_id is not None:
            rep.consolidated_receipt_doc_id = consolidated_receipt_doc_id
        if mfar_doc_id is not None:
            rep.mfar_doc_id = mfar_doc_id
        if employee_declaration_confirmed is not None:
            rep.employee_declaration_confirmed = 1 if employee_declaration_confirmed else 0
        if out_of_base_activity is not None:
            rep.out_of_base_activity = 1 if out_of_base_activity else 0
        if remarks is not None:
            rep.remarks = remarks
        rep.updated_at = utc_now()
        db.commit()
        return self.get_expense_report_detail(db, actor, rep.id)

    def submit_expense_report(
        self,
        db: Session,
        actor: ResolvedActor,
        report_id: str,
        employee_declaration_confirmed: Optional[bool] = None,
    ) -> dict[str, Any]:
        rep = db.query(ExpenseReport).filter(ExpenseReport.id == report_id).first()
        if not rep:
            raise FinanceNotFoundError("Expense report not found.")
        if rep.employee_id != actor.id:
            raise FinanceForbiddenError("Only the report owner may submit their own expense report.")
        if "submitted" not in VALID_REPORT_REVIEW_TRANSITIONS.get(rep.review_status, set()):
            raise FinanceConflictError(f"Cannot submit expense report from status '{rep.review_status}'.")

        if employee_declaration_confirmed is not None:
            rep.employee_declaration_confirmed = 1 if employee_declaration_confirmed else 0
        if not rep.employee_declaration_confirmed:
            raise FinanceValidationError("Employee declaration confirming business purpose must be checked before submission.")

        lines = db.query(ExpenseLine).filter(ExpenseLine.expense_report_id == rep.id).all()
        if not lines:
            raise FinanceValidationError("Cannot submit an empty expense report without expense lines.")

        # Reset returned lines to pending on resubmission
        for l in lines:
            if l.line_status == "returned":
                l.line_status = "pending"

        eval_res = self.policy_engine.evaluate_expense_report(
            db=db,
            report=rep,
            lines=lines,
            consume_slot_and_exceptions=True,
        )
        rep.policy_version_id = eval_res["policy_version_id"]
        rep.policy_evaluation_json = json.dumps(eval_res)
        if eval_res.get("consumed_exception_ids"):
            rep.exception_request_id = eval_res["consumed_exception_ids"][0]

        if not eval_res["is_compliant"]:
            # Log missing document exceptions if applicable
            for r in eval_res["results"]:
                if r["outcome"] == "violated" and r["rule_id"] in ("CONSOLIDATED_RECEIPT_PDF", "OUT_OF_BASE_MFAR"):
                    _create_exception_register_entry(
                        db=db,
                        entity_type="expense_report",
                        entity_id=rep.id,
                        exception_category="missing_document",
                        severity="medium",
                        reason=r["message"],
                        owner_id=rep.employee_id,
                        department_id=rep.department_id,
                        amount_minor=eval_res["current_report_claimed_minor"],
                    )
            record_audit_event(
                db=db,
                actor_id=actor.id,
                actor_role=actor.primary_role,
                action="expense_report_submission_blocked_by_policy",
                entity_type="expense_report",
                entity_id=rep.id,
                new_state=eval_res,
            )
            db.commit()
            raise FinanceValidationError(
                "Expense report submission blocked by financial policy validation.",
                details=eval_res,
            )

        prev = rep.review_status
        rep.review_status = "submitted"
        rep.submitted_at = utc_now()
        rep.updated_at = utc_now()

        # Update any open released advances for this employee to show liquidation submitted
        open_advances = (
            db.query(CashAdvance)
            .filter(
                CashAdvance.employee_id == rep.employee_id,
                CashAdvance.payment_status == "evidence_verified",
                CashAdvance.settlement_status.notin_(["reconciled", "closed"]),
            )
            .all()
        )
        for oa in open_advances:
            if oa.liquidation_review_status in ("not_started", "returned_for_correction"):
                oa.liquidation_review_status = "submitted"
                oa.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="expense_report_submitted",
            entity_type="expense_report",
            entity_id=rep.id,
            previous_state={"review_status": prev},
            new_state={
                "review_status": "submitted",
                "policy_version_id": rep.policy_version_id,
                "overall_outcome": eval_res["overall_outcome"],
            },
        )
        db.commit()
        return self.get_expense_report_detail(db, actor, rep.id)

    def review_expense_report(
        self,
        db: Session,
        actor: ResolvedActor,
        report_id: str,
        line_decisions: list[dict[str, Any]],
        report_decision: str,  # approved | partially_approved | returned_for_correction | rejected
        review_notes: str,
    ) -> dict[str, Any]:
        require_roles(actor, "finance_reviewer", "finance_controller", action_desc="review an expense report")
        rep = db.query(ExpenseReport).filter(ExpenseReport.id == report_id).first()
        if not rep:
            raise FinanceNotFoundError("Expense report not found.")
        if report_decision not in VALID_REPORT_REVIEW_TRANSITIONS.get(rep.review_status, set()):
            raise FinanceConflictError(
                f"Cannot transition expense report from '{rep.review_status}' to '{report_decision}'."
            )

        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors={"employee": rep.employee_id},
            current_duty_label="finance_reviewer",
            entity_type="expense_report",
            entity_id=rep.id,
        )

        lines = (
            db.query(ExpenseLine)
            .filter(ExpenseLine.expense_report_id == rep.id)
            .order_by(ExpenseLine.created_at.asc())
            .all()
        )
        line_map = {l.id: l for l in lines}

        for ld in line_decisions:
            lid = ld.get("line_id")
            if lid not in line_map:
                raise FinanceValidationError(f"Expense line '{lid}' does not belong to report '{rep.id}'.")
            line = line_map[lid]
            l_status = ld.get("line_status")
            if l_status not in ("approved", "partially_approved", "rejected", "returned"):
                raise FinanceValidationError(f"Invalid line_status '{l_status}'.")
            app_amt = ld.get("approved_amount_minor")
            reason = (ld.get("rejection_reason") or ld.get("review_notes") or "").strip()

            if l_status == "approved":
                final_amt = int(app_amt) if app_amt is not None else int(line.claimed_amount_minor)
                if final_amt <= 0 or final_amt > int(line.claimed_amount_minor):
                    raise FinanceValidationError("Approved line amount cannot exceed claimed amount.")
                if final_amt < int(line.claimed_amount_minor):
                    l_status = "partially_approved"
                line.approved_amount_minor = final_amt
            elif l_status == "partially_approved":
                if app_amt is None or int(app_amt) <= 0 or int(app_amt) >= int(line.claimed_amount_minor):
                    raise FinanceValidationError(
                        "Partially approved line must have 0 < approved_amount_minor < claimed_amount_minor."
                    )
                if not reason:
                    raise FinanceValidationError("Partial approval requires a reason for the unapproved portion.")
                line.approved_amount_minor = int(app_amt)
            else:
                if not reason:
                    raise FinanceValidationError(f"Line status '{l_status}' requires a reason.")
                line.approved_amount_minor = 0
                line.rejection_reason = reason

            line.line_status = l_status
            line.reviewer_id = actor.id
            line.reviewed_at = utc_now()
            line.review_notes = ld.get("review_notes") or reason
            line.updated_at = utc_now()

        # If report_decision is approved or partially_approved, ensure all lines were reviewed
        if report_decision in ("approved", "partially_approved"):
            if any(l.line_status in ("pending", "returned") for l in lines):
                raise FinanceValidationError(
                    "All expense lines must be reviewed before approving the expense report."
                )

        prev_status = rep.review_status
        rep.review_status = report_decision
        rep.reviewer_id = actor.id
        rep.reviewed_at = utc_now()
        rep.review_notes = review_notes
        if report_decision in ("rejected", "returned_for_correction"):
            if not review_notes or len(review_notes.strip()) < 3:
                raise FinanceValidationError("Rejection or return-for-correction requires review_notes.")
            rep.rejection_reason = review_notes.strip()
        rep.updated_at = utc_now()

        # If final rejected, release the monthly period slot so employee can submit a corrected replacement if needed
        # Note: returned_for_correction KEEPS the slot (same report edited, per L10)
        if report_decision == "rejected":
            total_claimed = sum(int(l.claimed_amount_minor) for l in lines)
            self.policy_engine.release_period_slot(db, rep, total_claimed)

        # Apply approved lines oldest-first against open released CashAdvances
        if report_decision in ("approved", "partially_approved"):
            self._apply_approved_report_oldest_first(db, actor, rep, lines)

        db.add(
            ApprovalHistory(
                entity_type="expense_report",
                entity_id=rep.id,
                approver_id=actor.id,
                approver_role=actor.primary_role,
                approval_stage="finance_review",
                decision=report_decision,
                approved_amount_minor=sum(
                    int(l.approved_amount_minor or 0)
                    for l in lines
                    if l.line_status in ("approved", "partially_approved")
                ),
                comments=review_notes or f"Report {report_decision}",
                previous_status=prev_status,
                resulting_status=report_decision,
                decided_at=utc_now(),
            )
        )
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"expense_report_{report_decision}",
            entity_type="expense_report",
            entity_id=rep.id,
            previous_state={"review_status": prev_status},
            new_state={"review_status": report_decision, "settlement_status": rep.settlement_status},
            reason=review_notes,
        )
        db.commit()
        return self.get_expense_report_detail(db, actor, rep.id)

    def _apply_approved_report_oldest_first(
        self,
        db: Session,
        actor: ResolvedActor,
        report: ExpenseReport,
        lines: list[ExpenseLine],
    ) -> None:
        approved_lines = [
            l for l in lines
            if l.line_status in ("approved", "partially_approved") and int(l.approved_amount_minor or 0) > 0
        ]
        rejected_minor = sum(
            int(l.claimed_amount_minor) - int(l.approved_amount_minor or 0)
            for l in lines
            if l.line_status in ("rejected", "partially_approved")
        )

        # Fetch employee's released open advances oldest-first
        open_advances = (
            db.query(CashAdvance)
            .filter(
                CashAdvance.employee_id == report.employee_id,
                CashAdvance.payment_status == "evidence_verified",
                CashAdvance.settlement_status.notin_(["closed"]),
            )
            .order_by(CashAdvance.created_at.asc())
            .all()
        )

        for line in approved_lines:
            remaining_line_amt = int(line.approved_amount_minor or 0)
            for adv in open_advances:
                if remaining_line_amt <= 0:
                    break
                recon = compute_advance_reconciliation(db, adv)
                avail_balance = int(recon["outstanding_balance_minor"])
                if avail_balance <= 0:
                    continue

                apply_amt = min(remaining_line_amt, avail_balance)
                ledger_entry = AdvanceLedgerEntry(
                    id=str(uuid.uuid4()),
                    advance_id=adv.id,
                    employee_id=report.employee_id,
                    entry_type="expense_applied",
                    signed_amount_minor=-apply_amt,
                    amount_minor=apply_amt,
                    currency=report.currency,
                    source_type="settlement_application",
                    source_id=report.id,
                    reference_code=report.report_number,
                    evidence_document_id=line.receipt_document_id or report.consolidated_receipt_doc_id,
                    actor_id=actor.id,
                    notes=f"Applied approved expense line {line.id} from {report.report_number}",
                    effective_at=utc_now(),
                    created_at=utc_now(),
                )
                db.add(ledger_entry)
                db.flush()

                app_rec = SettlementApplication(
                    id=str(uuid.uuid4()),
                    expense_report_id=report.id,
                    expense_line_id=line.id,
                    advance_id=adv.id,
                    employee_id=report.employee_id,
                    application_type="advance_offset",
                    applied_amount_minor=apply_amt,
                    currency=report.currency,
                    ledger_entry_id=ledger_entry.id,
                    evidence_document_id=line.receipt_document_id or report.consolidated_receipt_doc_id,
                    applied_by_id=actor.id,
                    notes=f"Oldest-first settlement offset against {adv.advance_number}",
                    applied_at=utc_now(),
                )
                db.add(app_rec)
                db.flush()
                remaining_line_amt -= apply_amt

            # If approved expense exceeds all open advances, record excess reimbursement payable
            if remaining_line_amt > 0:
                target_adv = open_advances[-1] if open_advances else None
                ledger_entry_id = None
                if target_adv:
                    le = AdvanceLedgerEntry(
                        id=str(uuid.uuid4()),
                        advance_id=target_adv.id,
                        employee_id=report.employee_id,
                        entry_type="reimbursement_payable",
                        signed_amount_minor=0,
                        amount_minor=remaining_line_amt,
                        currency=report.currency,
                        source_type="settlement_application",
                        source_id=report.id,
                        reference_code=report.report_number,
                        evidence_document_id=line.receipt_document_id or report.consolidated_receipt_doc_id,
                        actor_id=actor.id,
                        notes=f"Excess approved expense reimbursement payable to employee ({report.report_number})",
                        effective_at=utc_now(),
                        created_at=utc_now(),
                    )
                    db.add(le)
                    db.flush()
                    ledger_entry_id = le.id

                db.add(
                    SettlementApplication(
                        id=str(uuid.uuid4()),
                        expense_report_id=report.id,
                        expense_line_id=line.id,
                        advance_id=target_adv.id if target_adv else None,
                        employee_id=report.employee_id,
                        application_type="excess_reimbursement_payable",
                        applied_amount_minor=remaining_line_amt,
                        currency=report.currency,
                        ledger_entry_id=ledger_entry_id,
                        evidence_document_id=line.receipt_document_id or report.consolidated_receipt_doc_id,
                        applied_by_id=actor.id,
                        notes="Approved expense exceeds open cash advances; eligible for reimbursement.",
                        applied_at=utc_now(),
                    )
                )
                report.settlement_status = "reimbursement_pending"

        if report.settlement_status == "unapplied":
            report.settlement_status = "applied"
        report.posting_status = "pending"

        # Update status of affected open advances & handle rejected expense shortfall (L7)
        for adv in open_advances:
            adv.liquidation_review_status = report.review_status
            recon = compute_advance_reconciliation(db, adv)
            if int(recon["outstanding_balance_minor"]) == 0 and int(recon["additional_reimbursement_unsettled_minor"]) == 0:
                adv.settlement_status = "reconciled"
            elif rejected_minor > 0 and int(recon["outstanding_balance_minor"]) > 0:
                adv.settlement_status = "shortfall_receivable"
                _create_exception_register_entry(
                    db=db,
                    entity_type="cash_advance",
                    entity_id=adv.id,
                    exception_category="rejected_expense_shortfall",
                    severity="high",
                    reason=(
                        f"Rejected/unsupported expense amount ({rejected_minor} centavos) leaves "
                        f"{recon['outstanding_balance_minor']} centavos receivable due from employee {adv.employee_id}. "
                        f"Never auto-written-off; requires cash return or written payroll deduction authorization."
                    ),
                    owner_id=adv.employee_id,
                    department_id=adv.department_id,
                    amount_minor=int(recon["outstanding_balance_minor"]),
                )
            else:
                adv.settlement_status = "settlement_pending"
            adv.updated_at = utc_now()

    def record_cash_return(
        self,
        db: Session,
        actor: ResolvedActor,
        advance_id: str,
        returned_amount_minor: int,
        bank_reference: str,
        evidence_document_id: str,
        notes: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "finance_reviewer", "evidence_verifier", "finance_controller", action_desc="record verified employee cash return")
        adv = db.query(CashAdvance).filter(CashAdvance.id == advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Cash advance not found.")
        if adv.payment_status != "evidence_verified":
            raise FinanceConflictError("Cannot record cash return on an advance that has not been released.")
        if adv.settlement_status == "closed":
            raise FinanceConflictError("Cannot record cash return on a closed cash advance.")
        if not bank_reference or len(bank_reference.strip()) < 3:
            raise FinanceValidationError("Bank deposit / official receipt reference is required for cash return.")

        doc = db.query(DocumentReference).filter(DocumentReference.id == evidence_document_id).first()
        if not doc:
            raise FinanceValidationError("Cash return deposit / receipt evidence document is required.")

        recon = compute_advance_reconciliation(db, adv)
        outstanding = int(recon["outstanding_balance_minor"])
        if int(returned_amount_minor) <= 0 or int(returned_amount_minor) > outstanding:
            raise FinanceValidationError(
                f"Returned amount ({returned_amount_minor}) must be > 0 and <= current outstanding balance ({outstanding})."
            )

        le = AdvanceLedgerEntry(
            id=str(uuid.uuid4()),
            advance_id=adv.id,
            employee_id=adv.employee_id,
            entry_type="cash_return",
            signed_amount_minor=-int(returned_amount_minor),
            amount_minor=int(returned_amount_minor),
            currency=adv.currency,
            source_type="cash_return_receipt",
            source_id=doc.id,
            reference_code=bank_reference.strip(),
            evidence_document_id=doc.id,
            actor_id=actor.id,
            notes=notes or "Verified unused cash advance return",
            effective_at=utc_now(),
            created_at=utc_now(),
        )
        db.add(le)
        db.flush()

        db.add(
            SettlementApplication(
                id=str(uuid.uuid4()),
                expense_report_id=None,
                expense_line_id=None,
                advance_id=adv.id,
                employee_id=adv.employee_id,
                application_type="cash_return",
                applied_amount_minor=int(returned_amount_minor),
                currency=adv.currency,
                ledger_entry_id=le.id,
                evidence_document_id=doc.id,
                bank_reference=bank_reference.strip(),
                applied_by_id=actor.id,
                notes=notes or "Unused advance funds returned by employee",
                applied_at=utc_now(),
            )
        )
        db.flush()

        updated_recon = compute_advance_reconciliation(db, adv)
        if int(updated_recon["outstanding_balance_minor"]) == 0 and int(updated_recon["additional_reimbursement_unsettled_minor"]) == 0:
            adv.settlement_status = "reconciled"
        adv.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="advance_cash_return_recorded",
            entity_type="cash_advance",
            entity_id=adv.id,
            new_state={
                "returned_amount_minor": returned_amount_minor,
                "remaining_outstanding_minor": updated_recon["outstanding_balance_minor"],
                "settlement_status": adv.settlement_status,
            },
            reason=notes,
            evidence_ref=doc.document_number,
        )
        db.commit()
        return self.get_cash_advance_detail(db, actor, adv.id)

    def record_reimbursement_settlement(
        self,
        db: Session,
        actor: ResolvedActor,
        advance_id: str,
        settled_amount_minor: int,
        bank_reference: str,
        evidence_document_id: str,
        notes: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "evidence_verifier", "finance_reviewer", "finance_controller", action_desc="record employee reimbursement payment evidence")
        adv = db.query(CashAdvance).filter(CashAdvance.id == advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Cash advance not found.")
        doc = db.query(DocumentReference).filter(DocumentReference.id == evidence_document_id).first()
        if not doc:
            raise FinanceValidationError("Reimbursement bank evidence document not found.")

        recon = compute_advance_reconciliation(db, adv)
        unsettled = int(recon["additional_reimbursement_unsettled_minor"])
        if int(settled_amount_minor) <= 0 or int(settled_amount_minor) > unsettled:
            raise FinanceValidationError(
                f"Reimbursement settlement ({settled_amount_minor}) must be > 0 and <= unsettled reimbursement ({unsettled})."
            )

        le = AdvanceLedgerEntry(
            id=str(uuid.uuid4()),
            advance_id=adv.id,
            employee_id=adv.employee_id,
            entry_type="reimbursement_settled",
            signed_amount_minor=0,
            amount_minor=int(settled_amount_minor),
            currency=adv.currency,
            source_type="reimbursement_payment",
            source_id=doc.id,
            reference_code=bank_reference.strip(),
            evidence_document_id=doc.id,
            actor_id=actor.id,
            notes=notes or "Employee excess expense reimbursement settled",
            effective_at=utc_now(),
            created_at=utc_now(),
        )
        db.add(le)
        db.flush()

        updated_recon = compute_advance_reconciliation(db, adv)
        if int(updated_recon["outstanding_balance_minor"]) == 0 and int(updated_recon["additional_reimbursement_unsettled_minor"]) == 0:
            adv.settlement_status = "reconciled"
        adv.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="advance_reimbursement_settled",
            entity_type="cash_advance",
            entity_id=adv.id,
            new_state={"settled_amount_minor": settled_amount_minor, "settlement_status": adv.settlement_status},
            evidence_ref=doc.document_number,
        )
        db.commit()
        return self.get_cash_advance_detail(db, actor, adv.id)

    def close_cash_advance(
        self,
        db: Session,
        actor: ResolvedActor,
        advance_id: str,
        closure_reason: str,
        authorized_exception_id: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "finance_controller", action_desc="authorize final closure of a cash advance")
        if not closure_reason or len(closure_reason.strip()) < 5:
            raise FinanceValidationError("A detailed Controller closure_reason is required.")

        adv = db.query(CashAdvance).filter(CashAdvance.id == advance_id).first()
        if not adv:
            raise FinanceNotFoundError("Cash advance not found.")
        if adv.settlement_status == "closed":
            raise FinanceConflictError("Cash advance is already closed.")
        if adv.payment_status != "evidence_verified":
            raise FinanceConflictError("Cannot close an advance that has not been released with verified bank evidence.")

        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors={"requestor": adv.requestor_id, "employee": adv.employee_id},
            current_duty_label="closure_controller",
            entity_type="cash_advance",
            entity_id=adv.id,
        )

        recon = compute_advance_reconciliation(db, adv)
        if int(recon["pending_review_expenses_minor"]) > 0:
            raise FinanceConflictError("Cannot close cash advance while linked expense lines are still pending review.")
        if int(recon["additional_reimbursement_unsettled_minor"]) > 0:
            raise FinanceConflictError(
                f"Cannot close cash advance while {recon['additional_reimbursement_unsettled_minor']} centavos of employee reimbursement remains unsettled."
            )

        outstanding = int(recon["outstanding_balance_minor"])
        if outstanding > 0:
            if not authorized_exception_id:
                raise FinanceConflictError(
                    f"Cannot close cash advance with remaining outstanding balance of {outstanding} centavos without an approved Controller exception."
                )
            exc = db.query(ExceptionRequest).filter(ExceptionRequest.id == authorized_exception_id).first()
            if not exc or exc.status != "approved" or int(exc.approved_max_amount_minor or 0) < outstanding:
                raise FinanceConflictError("Provided exception is not approved or insufficient to cover the outstanding balance.")
            exc.status = "consumed"
            exc.consumed_at = utc_now()
            exc.consumed_by_entity_id = adv.id

        # Verify that at least one settlement application or cash return evidence exists
        apps = db.query(SettlementApplication).filter(SettlementApplication.advance_id == adv.id).all()
        if not apps and not authorized_exception_id:
            raise FinanceConflictError("Cannot close cash advance without evidenced expense settlement or cash return.")

        for a in apps:
            if a.application_type == "cash_return" and (not a.evidence_document_id or not a.bank_reference):
                raise FinanceConflictError("Cash return application is missing required bank reference or document evidence.")

        snapshot = ClosingSnapshot(
            id=str(uuid.uuid4()),
            advance_id=adv.id,
            released_amount_minor=int(recon["released_amount_minor"]),
            claimed_expense_minor=int(recon["total_claimed_minor"]),
            approved_expense_minor=int(recon["approved_expenses_applied_minor"]),
            rejected_expense_minor=int(recon["rejected_expenses_minor"]),
            cash_returned_minor=int(recon["cash_returned_minor"]),
            reimbursement_minor=int(recon["additional_reimbursement_settled_minor"]),
            final_outstanding_minor=outstanding,
            authorized_exception_id=authorized_exception_id,
            controller_id=actor.id,
            closure_reason=closure_reason.strip(),
            snapshot_json=json.dumps(recon, default=str),
            closed_at=utc_now(),
        )
        db.add(snapshot)

        prev_settlement = adv.settlement_status
        adv.settlement_status = "closed"
        adv.closed_at = utc_now()
        adv.closed_by_id = actor.id
        adv.closure_reason = closure_reason.strip()
        adv.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="cash_advance_closed",
            entity_type="cash_advance",
            entity_id=adv.id,
            previous_state={"settlement_status": prev_settlement},
            new_state={"settlement_status": "closed", "closing_snapshot_id": snapshot.id},
            reason=closure_reason.strip(),
        )
        db.commit()
        return self.get_cash_advance_detail(db, actor, adv.id)

    def get_expense_report_detail(self, db: Session, actor: ResolvedActor, report_id: str) -> dict[str, Any]:
        rep = db.query(ExpenseReport).filter(ExpenseReport.id == report_id).first()
        if not rep:
            raise FinanceNotFoundError("Expense report not found.")
        if rep.employee_id != actor.id and not actor.can_access_department(rep.department_id):
            raise FinanceForbiddenError("Not authorized to view this expense report.")

        lines = (
            db.query(ExpenseLine)
            .filter(ExpenseLine.expense_report_id == rep.id)
            .order_by(ExpenseLine.created_at.asc())
            .all()
        )
        apps = (
            db.query(SettlementApplication)
            .filter(SettlementApplication.expense_report_id == rep.id)
            .order_by(SettlementApplication.applied_at.asc())
            .all()
        )
        return rep.to_dict(
            lines=[l.to_dict() for l in lines],
            applications=[a.to_dict() for a in apps],
        )

    # --------------------------------------------------------------------------
    # D. Purchase-to-Pay (Vendors, Tax Codes, Purchase Requests, AP, Payments)
    # --------------------------------------------------------------------------

    def create_vendor(
        self,
        db: Session,
        actor: ResolvedActor,
        registered_name: str,
        payee_type: str,
        tin: Optional[str] = None,
        address: Optional[str] = None,
        contact_person: Optional[str] = None,
        contact_email: Optional[str] = None,
        payment_method: str = "bank_transfer",
        bank_name: Optional[str] = None,
        bank_account_ref: Optional[str] = None,
        kyc_document_id: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", "finance_reviewer", "finance_controller", action_desc="create a vendor master record")
        if not registered_name or len(registered_name.strip()) < 3:
            raise FinanceValidationError("Vendor registered_name is required.")

        masked = None
        if bank_account_ref:
            clean_ref = bank_account_ref.strip()
            masked = f"****{clean_ref[-4:]}" if len(clean_ref) >= 4 else "****"

        vendor = Vendor(
            id=str(uuid.uuid4()),
            vendor_code=next_sequence_number(db, "VND"),
            registered_name=registered_name.strip(),
            payee_type=payee_type,
            tin=tin,
            address=address,
            contact_person=contact_person,
            contact_email=contact_email,
            payment_method=payment_method,
            bank_name=bank_name,
            bank_account_masked=masked,
            bank_account_ref=bank_account_ref.strip() if bank_account_ref else None,
            verification_status="pending_verification",
            kyc_document_id=kyc_document_id,
            created_by_id=actor.id,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        db.add(vendor)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="vendor_created",
            entity_type="vendor",
            entity_id=vendor.id,
            new_state={"vendor_code": vendor.vendor_code, "verification_status": "pending_verification"},
        )
        db.commit()
        db.refresh(vendor)
        return vendor.to_dict(include_protected_bank=True)

    def verify_vendor(
        self,
        db: Session,
        actor: ResolvedActor,
        vendor_id: str,
        decision: str = "verified",  # verified | rejected | suspended
        notes: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "verifier", "finance_reviewer", "finance_controller", action_desc="verify a vendor master record")
        vendor = db.query(Vendor).filter(Vendor.id == vendor_id).first()
        if not vendor:
            raise FinanceNotFoundError("Vendor not found.")

        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors={"vendor_creator": vendor.created_by_id},
            current_duty_label="vendor_verifier",
            entity_type="vendor",
            entity_id=vendor.id,
        )

        prev = vendor.verification_status
        vendor.verification_status = decision
        vendor.verified_by_id = actor.id
        vendor.verified_at = utc_now()
        vendor.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"vendor_{decision}",
            entity_type="vendor",
            entity_id=vendor.id,
            previous_state={"verification_status": prev},
            new_state={"verification_status": decision},
            reason=notes,
        )
        db.commit()
        db.refresh(vendor)
        return vendor.to_dict(include_protected_bank=True)

    def request_vendor_bank_change(
        self,
        db: Session,
        actor: ResolvedActor,
        vendor_id: str,
        new_bank_name: str,
        new_bank_account_ref: str,
        reason: str,
        supporting_document_id: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", "finance_reviewer", action_desc="request vendor bank account change")
        vendor = db.query(Vendor).filter(Vendor.id == vendor_id).first()
        if not vendor:
            raise FinanceNotFoundError("Vendor not found.")
        if not new_bank_name or not new_bank_account_ref or not reason:
            raise FinanceValidationError("new_bank_name, new_bank_account_ref, and reason are required.")

        clean_ref = new_bank_account_ref.strip()
        masked = f"****{clean_ref[-4:]}" if len(clean_ref) >= 4 else "****"

        change = VendorBankChange(
            id=str(uuid.uuid4()),
            vendor_id=vendor.id,
            previous_bank_ref=vendor.bank_account_ref,
            new_bank_name=new_bank_name.strip(),
            new_bank_account_masked=masked,
            new_bank_account_ref=clean_ref,
            supporting_document_id=supporting_document_id,
            reason=reason.strip(),
            status="pending_approval",
            requested_by_id=actor.id,
            requested_at=utc_now(),
        )
        db.add(change)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="vendor_bank_change_requested",
            entity_type="vendor",
            entity_id=vendor.id,
            new_state={"change_id": change.id, "new_bank_name": change.new_bank_name, "new_bank_account_masked": masked},
            reason=reason.strip(),
        )
        db.commit()
        db.refresh(change)
        return change.to_dict(include_protected_bank=True)

    def decide_vendor_bank_change(
        self,
        db: Session,
        actor: ResolvedActor,
        change_id: str,
        decision: str,  # approved | rejected
        notes: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "verifier", "bank_approver", "finance_controller", action_desc="approve vendor bank account change")
        change = db.query(VendorBankChange).filter(VendorBankChange.id == change_id).first()
        if not change:
            raise FinanceNotFoundError("Vendor bank change request not found.")
        if change.status != "pending_approval":
            raise FinanceConflictError(f"Bank change request is already '{change.status}'.")

        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors={"bank_change_requester": change.requested_by_id},
            current_duty_label="bank_change_approver",
            entity_type="vendor_bank_change",
            entity_id=change.id,
        )

        vendor = db.query(Vendor).filter(Vendor.id == change.vendor_id).first()
        if not vendor:
            raise FinanceNotFoundError("Vendor not found.")

        change.status = decision
        change.approved_by_id = actor.id
        change.decided_at = utc_now()
        if decision == "approved":
            vendor.bank_name = change.new_bank_name
            vendor.bank_account_masked = change.new_bank_account_masked
            vendor.bank_account_ref = change.new_bank_account_ref
            vendor.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"vendor_bank_change_{decision}",
            entity_type="vendor",
            entity_id=vendor.id,
            new_state={"change_id": change.id, "status": decision, "bank_account_masked": vendor.bank_account_masked},
            reason=notes or change.reason,
        )
        db.commit()
        db.refresh(change)
        return change.to_dict(include_protected_bank=True)

    def create_or_approve_tax_code(
        self,
        db: Session,
        actor: ResolvedActor,
        code: str,
        description: str,
        tax_type: str,
        rate_bps: int,
        requires_bir_2307: bool = False,
        approve_as_cpa: bool = False,
    ) -> dict[str, Any]:
        require_roles(actor, "finance_reviewer", "finance_controller", action_desc="configure tax codes")
        clean_code = code.strip().upper()
        tc = db.query(TaxCode).filter(TaxCode.code == clean_code).first()
        if not tc:
            tc = TaxCode(
                code=clean_code,
                description=description.strip(),
                tax_type=tax_type.strip().upper(),
                rate_bps=int(rate_bps),
                requires_bir_2307=1 if requires_bir_2307 else 0,
                cpa_approved=0,
                created_at=utc_now(),
            )
            db.add(tc)
        if approve_as_cpa:
            require_roles(actor, "finance_controller", action_desc="CPA-approve a tax code")
            tc.cpa_approved = 1
            tc.approved_by_id = actor.id

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="tax_code_configured",
            entity_type="tax_code",
            entity_id=clean_code,
            new_state={"rate_bps": rate_bps, "cpa_approved": bool(tc.cpa_approved)},
        )
        db.commit()
        db.refresh(tc)
        return tc.to_dict()

    def create_purchase_request(
        self,
        db: Session,
        actor: ResolvedActor,
        vendor_id: str,
        payment_category: str,
        business_purpose: str,
        requested_amount_minor: int,
        requested_payment_date: str,
        department_id: Optional[str] = None,
        cost_center_id: Optional[str] = None,
        budget_reference: Optional[str] = None,
        due_date: Optional[str] = None,
        supporting_doc_ids: Optional[list[str]] = None,
        invoice_doc_id: Optional[str] = None,
        remarks: Optional[str] = None,
        lines: Optional[list[dict[str, Any]]] = None,
        idempotency_key: Optional[str] = None,
        submit_now: bool = False,
    ) -> dict[str, Any]:
        if idempotency_key:
            existing = db.query(PurchaseRequest).filter(PurchaseRequest.idempotency_key == idempotency_key).first()
            if existing:
                return self.get_purchase_request_detail(db, actor, existing.id)

        if payment_category not in ALLOWLISTED_PR_CATEGORIES:
            raise FinanceValidationError(
                f"Invalid payment_category '{payment_category}'. Allowed: {', '.join(sorted(ALLOWLISTED_PR_CATEGORIES))}."
            )
        cat_rules = ALLOWLISTED_PR_CATEGORIES[payment_category]
        vendor = db.query(Vendor).filter(Vendor.id == vendor_id).first()
        if not vendor:
            raise FinanceNotFoundError("Vendor not found.")
        if cat_rules["requires_tin"] and not (vendor.tin or "").strip():
            raise FinanceValidationError(
                f"Payment category '{payment_category}' requires the payee/vendor to have a registered TIN on file."
            )
        if int(requested_amount_minor) <= 0:
            raise FinanceValidationError("requested_amount_minor must be positive.")

        pr = PurchaseRequest(
            id=str(uuid.uuid4()),
            request_number=next_sequence_number(db, "PR"),
            requestor_id=actor.id,
            vendor_id=vendor.id,
            payment_category=payment_category,
            is_restricted_category=1 if cat_rules["restricted"] else 0,
            business_purpose=business_purpose.strip(),
            department_id=department_id or actor.user.department_id,
            cost_center_id=cost_center_id or actor.user.cost_center_id,
            budget_reference=budget_reference,
            requested_amount_minor=int(requested_amount_minor),
            currency="PHP",
            requested_payment_date=requested_payment_date[:10],
            due_date=due_date[:10] if due_date else requested_payment_date[:10],
            supporting_doc_ids_json=json.dumps(supporting_doc_ids or []),
            invoice_doc_id=invoice_doc_id,
            remarks=remarks,
            approval_status="draft",
            workflow_status="draft",
            posting_status="not_applicable",
            idempotency_key=idempotency_key,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        db.add(pr)
        db.flush()

        for l_item in (lines or []):
            qty = max(1, int(l_item.get("quantity", 1)))
            unit_minor = int(l_item.get("unit_amount_minor", requested_amount_minor))
            db.add(
                PurchaseRequestLine(
                    id=str(uuid.uuid4()),
                    purchase_request_id=pr.id,
                    description=str(l_item.get("description", business_purpose))[:255],
                    quantity=qty,
                    unit_amount_minor=unit_minor,
                    total_amount_minor=qty * unit_minor,
                    account_code=l_item.get("account_code"),
                )
            )

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="purchase_request_created",
            entity_type="purchase_request",
            entity_id=pr.id,
            new_state={
                "request_number": pr.request_number,
                "payment_category": payment_category,
                "requested_amount_minor": pr.requested_amount_minor,
            },
        )
        db.commit()
        if submit_now:
            return self.submit_purchase_request(db, actor, pr.id)
        return self.get_purchase_request_detail(db, actor, pr.id)

    def submit_purchase_request(self, db: Session, actor: ResolvedActor, pr_id: str) -> dict[str, Any]:
        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pr_id).first()
        if not pr:
            raise FinanceNotFoundError("Purchase request not found.")
        if pr.requestor_id != actor.id:
            raise FinanceForbiddenError("Only the requestor may submit their purchase request.")
        if "submitted" not in VALID_PR_APPROVAL_TRANSITIONS.get(pr.approval_status, set()):
            raise FinanceConflictError(f"Cannot submit purchase request from '{pr.approval_status}'.")

        prev = pr.approval_status
        pr.approval_status = "submitted"
        pr.workflow_status = "submitted"
        pr.submitted_at = utc_now()
        pr.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="purchase_request_submitted",
            entity_type="purchase_request",
            entity_id=pr.id,
            previous_state={"approval_status": prev},
            new_state={"approval_status": "submitted"},
        )
        db.commit()
        return self.get_purchase_request_detail(db, actor, pr.id)

    def decide_purchase_request(
        self,
        db: Session,
        actor: ResolvedActor,
        pr_id: str,
        decision: str,  # approved | rejected | returned_for_correction | cancelled
        comments: str,
        approved_amount_minor: Optional[int] = None,
    ) -> dict[str, Any]:
        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pr_id).first()
        if not pr:
            raise FinanceNotFoundError("Purchase request not found.")
        if decision not in VALID_PR_APPROVAL_TRANSITIONS.get(pr.approval_status, set()):
            raise FinanceConflictError(f"Cannot transition purchase request from '{pr.approval_status}' to '{decision}'.")
        if not comments or len(comments.strip()) < 3:
            raise FinanceValidationError("Decision comments / reason are required.")

        if decision != "cancelled":
            assert_distinct_actors(
                db=db,
                actor=actor,
                prior_actors={"requestor": pr.requestor_id},
                current_duty_label="pr_approver",
                entity_type="purchase_request",
                entity_id=pr.id,
            )
            amt = int(approved_amount_minor) if approved_amount_minor is not None else int(pr.requested_amount_minor)
            check_approval_matrix(
                db=db,
                actor=actor,
                workflow="purchase_request",
                stage="business_approval",
                amount_minor=amt,
                department_id=pr.department_id,
            )
            if decision == "approved":
                if amt <= 0 or amt > int(pr.requested_amount_minor):
                    raise FinanceValidationError("Approved amount must be > 0 and <= requested amount.")
                pr.approved_amount_minor = amt
                pr.approved_at = utc_now()

        prev = pr.approval_status
        pr.approval_status = decision
        pr.workflow_status = decision
        pr.updated_at = utc_now()

        db.add(
            ApprovalHistory(
                entity_type="purchase_request",
                entity_id=pr.id,
                approver_id=actor.id,
                approver_role=actor.primary_role,
                approval_stage="business_approval",
                decision=decision,
                approved_amount_minor=pr.approved_amount_minor if decision == "approved" else None,
                comments=comments.strip(),
                previous_status=prev,
                resulting_status=decision,
                decided_at=utc_now(),
            )
        )
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"purchase_request_{decision}",
            entity_type="purchase_request",
            entity_id=pr.id,
            previous_state={"approval_status": prev},
            new_state={"approval_status": decision, "approved_amount_minor": pr.approved_amount_minor},
            reason=comments.strip(),
        )
        db.commit()
        return self.get_purchase_request_detail(db, actor, pr.id)

    def validate_account_payable(
        self,
        db: Session,
        actor: ResolvedActor,
        purchase_request_id: str,
        invoice_number: str,
        invoice_date: str,
        due_date: str,
        original_payable_minor: int,
        credit_adjustment_minor: int = 0,
        vat_code: Optional[str] = None,
        ewt_code: Optional[str] = None,
        payment_terms: Optional[str] = None,
        accounting_reference: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "finance_reviewer", "finance_controller", action_desc="validate an Account Payable")
        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == purchase_request_id).first()
        if not pr:
            raise FinanceNotFoundError("Purchase request not found.")
        if pr.approval_status != "approved" or not pr.approved_amount_minor:
            raise FinanceConflictError("Account Payable can only be recorded for an approved Purchase Request.")

        vendor = db.query(Vendor).filter(Vendor.id == pr.vendor_id).first()
        if not vendor or vendor.verification_status != "verified":
            raise FinanceConflictError(
                "Vendor must be verified ('verified' status) before validating an Account Payable."
            )

        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors={"pr_requestor": pr.requestor_id},
            current_duty_label="payable_validator",
            entity_type="account_payable",
            entity_id=pr.id,
        )

        if int(original_payable_minor) <= 0 or int(original_payable_minor) > int(pr.approved_amount_minor):
            raise FinanceValidationError(
                f"Payable invoice amount ({original_payable_minor}) must be > 0 and <= approved PR amount ({pr.approved_amount_minor})."
            )

        norm_inv = "".join(ch for ch in invoice_number.strip().upper() if ch.isalnum())
        if not norm_inv:
            raise FinanceValidationError("Valid invoice_number is required.")

        # Check exact duplicate (vendor_id, normalized_invoice_no, currency)
        exact_dup = (
            db.query(AccountPayable)
            .filter(
                AccountPayable.vendor_id == vendor.id,
                AccountPayable.normalized_invoice_no == norm_inv,
                AccountPayable.currency == pr.currency,
            )
            .first()
        )
        if exact_dup:
            _create_exception_register_entry(
                db=db,
                entity_type="purchase_request",
                entity_id=pr.id,
                exception_category="duplicate_invoice_alert",
                severity="high",
                reason=f"Duplicate invoice '{invoice_number}' for vendor {vendor.registered_name} (matches payable {exact_dup.payable_number}).",
                owner_id=pr.requestor_id,
                department_id=pr.department_id,
                amount_minor=int(original_payable_minor),
            )
            db.commit()
            raise FinanceConflictError(
                f"Duplicate invoice detected: vendor '{vendor.registered_name}' already has payable '{exact_dup.payable_number}' with invoice '{invoice_number}'."
            )

        # Check fuzzy duplicate (same vendor + same amount + same invoice date)
        fuzzy_dup = (
            db.query(AccountPayable)
            .filter(
                AccountPayable.vendor_id == vendor.id,
                AccountPayable.original_payable_minor == int(original_payable_minor),
                AccountPayable.invoice_date == invoice_date[:10],
            )
            .first()
        )

        # Tax calculation & CPA review flag
        vat_minor = 0
        ewt_minor = 0
        tax_review_status = "tax_review_required"
        cpa_ok = True

        if vat_code:
            vc = db.query(TaxCode).filter(TaxCode.code == vat_code.strip().upper()).first()
            if not vc or not vc.cpa_approved:
                cpa_ok = False
            else:
                vat_minor = (int(original_payable_minor) * int(vc.rate_bps)) // 10000
        if ewt_code:
            ec = db.query(TaxCode).filter(TaxCode.code == ewt_code.strip().upper()).first()
            if not ec or not ec.cpa_approved:
                cpa_ok = False
            else:
                ewt_minor = (int(original_payable_minor) * int(ec.rate_bps)) // 10000

        if (vat_code or ewt_code) and cpa_ok:
            tax_review_status = "cpa_approved"

        net_payable_minor = int(original_payable_minor) - int(credit_adjustment_minor) - ewt_minor
        if net_payable_minor <= 0:
            raise FinanceValidationError("Calculated net_payable_minor must be positive.")

        ap = AccountPayable(
            id=str(uuid.uuid4()),
            payable_number=next_sequence_number(db, "AP"),
            purchase_request_id=pr.id,
            vendor_id=vendor.id,
            invoice_number=invoice_number.strip(),
            normalized_invoice_no=norm_inv,
            invoice_date=invoice_date[:10],
            due_date=due_date[:10],
            currency=pr.currency,
            original_payable_minor=int(original_payable_minor),
            credit_adjustment_minor=int(credit_adjustment_minor),
            vat_code=vat_code.strip().upper() if vat_code else None,
            vat_amount_minor=vat_minor,
            ewt_code=ewt_code.strip().upper() if ewt_code else None,
            ewt_amount_minor=ewt_minor,
            net_payable_minor=net_payable_minor,
            tax_review_status=tax_review_status,
            duplicate_flag=1 if fuzzy_dup else 0,
            duplicate_reason=f"Matches amount and date of payable {fuzzy_dup.payable_number}" if fuzzy_dup else None,
            payment_terms=payment_terms,
            accounting_reference=accounting_reference,
            posting_status="pending",
            status="open",
            validated_by_id=actor.id,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        pr.workflow_status = "payable_validated"
        pr.updated_at = utc_now()
        db.add(ap)
        db.flush()

        if fuzzy_dup:
            _create_exception_register_entry(
                db=db,
                entity_type="account_payable",
                entity_id=ap.id,
                exception_category="duplicate_payment_alert",
                severity="medium",
                reason=f"Potential duplicate payable {ap.payable_number}: same vendor, amount, and invoice date as {fuzzy_dup.payable_number}.",
                owner_id=actor.id,
                department_id=pr.department_id,
                amount_minor=net_payable_minor,
            )

        if tax_review_status == "tax_review_required":
            _create_exception_register_entry(
                db=db,
                entity_type="account_payable",
                entity_id=ap.id,
                exception_category="tax_review_required",
                severity="low",
                reason=f"Payable {ap.payable_number} validated without CPA-approved tax/withholding code (C7 confirmation).",
                owner_id=actor.id,
                department_id=pr.department_id,
                amount_minor=net_payable_minor,
            )

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="account_payable_validated",
            entity_type="account_payable",
            entity_id=ap.id,
            new_state={
                "payable_number": ap.payable_number,
                "net_payable_minor": ap.net_payable_minor,
                "tax_review_status": ap.tax_review_status,
            },
        )
        db.commit()
        return self.get_payable_detail(db, actor, ap.id)

    def _get_pr_approver_ids(self, db: Session, pr_id: str) -> dict[str, Optional[str]]:
        decisions = (
            db.query(ApprovalHistory)
            .filter(
                ApprovalHistory.entity_type == "purchase_request",
                ApprovalHistory.entity_id == pr_id,
                ApprovalHistory.decision == "approved",
            )
            .all()
        )
        return {f"pr_approver_{idx}": d.approver_id for idx, d in enumerate(decisions, start=1)}

    def prepare_payable_payment(
        self,
        db: Session,
        actor: ResolvedActor,
        payable_id: str,
        payment_amount_minor: int,
        payment_method: str = "bank_transfer",
        idempotency_key: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", action_desc="prepare a vendor payment record")
        if idempotency_key:
            existing = db.query(PaymentRecord).filter(PaymentRecord.idempotency_key == idempotency_key).first()
            if existing:
                return existing.to_dict()

        ap = db.query(AccountPayable).filter(AccountPayable.id == payable_id).first()
        if not ap:
            raise FinanceNotFoundError("Account Payable not found.")
        if ap.status in ("reconciled", "paid", "cancelled", "disputed"):
            raise FinanceConflictError(f"Cannot prepare payment for payable in status '{ap.status}'.")

        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == ap.purchase_request_id).first()
        prior_actors = {
            "pr_requestor": pr.requestor_id if pr else None,
            **(self._get_pr_approver_ids(db, pr.id) if pr else {}),
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="maker",
            entity_type="payment_record",
            entity_id=ap.id,
        )

        active_payments = (
            db.query(PaymentRecord)
            .filter(
                PaymentRecord.payable_id == ap.id,
                PaymentRecord.status.notin_(["failed", "cancelled"]),
            )
            .all()
        )
        if any(p.status == "payment_unknown" for p in active_payments):
            raise FinanceConflictError(
                "An existing payment attempt is in 'payment_unknown' investigation state; resolve before creating another payment."
            )

        committed_minor = sum(int(p.executed_amount_minor or p.payment_amount_minor) for p in active_payments)
        if int(payment_amount_minor) <= 0 or (committed_minor + int(payment_amount_minor)) > int(ap.net_payable_minor):
            raise FinanceValidationError(
                f"Payment amount ({payment_amount_minor}) plus active payments ({committed_minor}) exceeds net payable ({ap.net_payable_minor})."
            )

        pay = PaymentRecord(
            id=str(uuid.uuid4()),
            payment_number=next_sequence_number(db, "PAY"),
            payable_id=ap.id,
            purchase_request_id=ap.purchase_request_id,
            vendor_id=ap.vendor_id,
            payment_amount_minor=int(payment_amount_minor),
            currency=ap.currency,
            payment_method=payment_method,
            status="prepared",
            maker_id=actor.id,
            prepared_at=utc_now(),
            reconciliation_status="unreconciled",
            idempotency_key=idempotency_key,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        if pr:
            pr.workflow_status = "payment_processing"
            pr.updated_at = utc_now()

        db.add(pay)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="payable_payment_prepared",
            entity_type="payment_record",
            entity_id=pay.id,
            new_state={"payment_number": pay.payment_number, "payment_amount_minor": pay.payment_amount_minor},
        )
        db.commit()
        db.refresh(pay)
        return pay.to_dict()

    def verify_payable_payment(self, db: Session, actor: ResolvedActor, payment_id: str, notes: str) -> dict[str, Any]:
        require_roles(actor, "verifier", action_desc="verify a payable payment record")
        pay = db.query(PaymentRecord).filter(PaymentRecord.id == payment_id).first()
        if not pay:
            raise FinanceNotFoundError("Payment record not found.")
        if "verified" not in VALID_PAYMENT_RECORD_TRANSITIONS.get(pay.status, set()):
            raise FinanceConflictError(f"Cannot verify payment record from status '{pay.status}'.")

        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pay.purchase_request_id).first()
        prior_actors = {
            "pr_requestor": pr.requestor_id if pr else None,
            **(self._get_pr_approver_ids(db, pr.id) if pr else {}),
            "maker": pay.maker_id,
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="verifier",
            entity_type="payment_record",
            entity_id=pay.id,
        )

        prev = pay.status
        pay.status = "verified"
        pay.verifier_id = actor.id
        pay.verified_at = utc_now()
        pay.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="payable_payment_verified",
            entity_type="payment_record",
            entity_id=pay.id,
            previous_state={"status": prev},
            new_state={"status": "verified"},
            reason=notes,
        )
        db.commit()
        db.refresh(pay)
        return pay.to_dict()

    def approve_payable_payment_bank(self, db: Session, actor: ResolvedActor, payment_id: str, notes: str) -> dict[str, Any]:
        pay = db.query(PaymentRecord).filter(PaymentRecord.id == payment_id).first()
        if not pay:
            raise FinanceNotFoundError("Payment record not found.")
        if "bank_approved" not in VALID_PAYMENT_RECORD_TRANSITIONS.get(pay.status, set()):
            raise FinanceConflictError(f"Cannot bank-approve payment record from status '{pay.status}'.")

        check_approval_matrix(
            db=db,
            actor=actor,
            workflow="payable_payment",
            stage="bank_approval",
            amount_minor=int(pay.payment_amount_minor),
        )
        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pay.purchase_request_id).first()
        prior_actors = {
            "pr_requestor": pr.requestor_id if pr else None,
            **(self._get_pr_approver_ids(db, pr.id) if pr else {}),
            "maker": pay.maker_id,
            "verifier": pay.verifier_id,
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="bank_approver",
            entity_type="payment_record",
            entity_id=pay.id,
        )

        prev = pay.status
        pay.status = "bank_approved"
        pay.bank_approver_id = actor.id
        pay.bank_approved_at = utc_now()
        pay.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="payable_payment_bank_approved",
            entity_type="payment_record",
            entity_id=pay.id,
            previous_state={"status": prev},
            new_state={"status": "bank_approved"},
            reason=notes,
        )
        db.commit()
        db.refresh(pay)
        return pay.to_dict()

    def record_payable_payment_execution(
        self,
        db: Session,
        actor: ResolvedActor,
        payment_id: str,
        bank_reference: str,
        executed_amount_minor: int,
        execution_date: str,
        remittance_doc_id: str,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", "bank_approver", action_desc="record manual bank execution for payable")
        pay = db.query(PaymentRecord).filter(PaymentRecord.id == payment_id).first()
        if not pay:
            raise FinanceNotFoundError("Payment record not found.")
        if "paid_unverified" not in VALID_PAYMENT_RECORD_TRANSITIONS.get(pay.status, set()):
            raise FinanceConflictError(f"Cannot record execution from status '{pay.status}'.")
        if not bank_reference or len(bank_reference.strip()) < 4:
            raise FinanceValidationError("Bank transaction reference is required.")
        if int(executed_amount_minor) <= 0 or int(executed_amount_minor) > int(pay.payment_amount_minor):
            raise FinanceValidationError("Executed amount must be > 0 and <= prepared payment amount.")

        dup_pay = (
            db.query(PaymentRecord)
            .filter(
                PaymentRecord.bank_reference == bank_reference.strip(),
                PaymentRecord.id != pay.id,
                PaymentRecord.status.notin_(["failed", "cancelled"]),
            )
            .first()
        )
        if dup_pay:
            raise FinanceConflictError(
                f"Duplicate bank reference '{bank_reference}' already used on payment {dup_pay.payment_number}."
            )

        doc = db.query(DocumentReference).filter(DocumentReference.id == remittance_doc_id).first()
        if not doc:
            raise FinanceValidationError("Remittance advice evidence document not found.")

        prev = pay.status
        pay.status = "paid_unverified"
        pay.bank_reference = bank_reference.strip()
        pay.executed_amount_minor = int(executed_amount_minor)
        pay.execution_date = execution_date[:10]
        pay.executed_by_id = actor.id
        pay.executed_at = utc_now()
        pay.remittance_doc_id = doc.id
        pay.updated_at = utc_now()

        ap = db.query(AccountPayable).filter(AccountPayable.id == pay.payable_id).first()
        if ap and ap.status == "open":
            ap.status = "paid_unverified"
            ap.updated_at = utc_now()
        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pay.purchase_request_id).first()
        if pr:
            pr.workflow_status = "paid_unverified"
            pr.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="payable_payment_paid_unverified",
            entity_type="payment_record",
            entity_id=pay.id,
            previous_state={"status": prev},
            new_state={
                "status": "paid_unverified",
                "bank_reference": pay.bank_reference,
                "executed_amount_minor": pay.executed_amount_minor,
            },
            evidence_ref=doc.document_number,
        )
        db.commit()
        db.refresh(pay)
        return pay.to_dict()

    def verify_and_reconcile_payable_payment(
        self,
        db: Session,
        actor: ResolvedActor,
        payment_id: str,
        reconciliation_outcome: str = "matched",  # matched | discrepancy
        notes: Optional[str] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "evidence_verifier", "finance_reviewer", "finance_controller", action_desc="verify payment evidence and reconcile")
        pay = db.query(PaymentRecord).filter(PaymentRecord.id == payment_id).first()
        if not pay:
            raise FinanceNotFoundError("Payment record not found.")
        if "reconciled" not in VALID_PAYMENT_RECORD_TRANSITIONS.get(pay.status, set()):
            raise FinanceConflictError(f"Payment must be in 'paid_unverified' state before reconciliation (current: '{pay.status}').")

        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pay.purchase_request_id).first()
        prior_actors = {
            "pr_requestor": pr.requestor_id if pr else None,
            **(self._get_pr_approver_ids(db, pr.id) if pr else {}),
            "maker": pay.maker_id,
            "verifier": pay.verifier_id,
            "bank_approver": pay.bank_approver_id,
            "execution_recorder": pay.executed_by_id,
        }
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors=prior_actors,
            current_duty_label="evidence_verifier",
            entity_type="payment_record",
            entity_id=pay.id,
        )

        ap = db.query(AccountPayable).filter(AccountPayable.id == pay.payable_id).first()
        if not ap:
            raise FinanceNotFoundError("Account Payable not found.")

        prev = pay.status
        pay.evidence_verifier_id = actor.id
        pay.evidence_verified_at = utc_now()
        pay.reconciliation_notes = notes
        pay.updated_at = utc_now()

        if reconciliation_outcome == "discrepancy":
            pay.reconciliation_status = "discrepancy"
            _create_exception_register_entry(
                db=db,
                entity_type="payment_record",
                entity_id=pay.id,
                exception_category="reconciliation_discrepancy",
                severity="high",
                reason=notes or f"Bank reconciliation discrepancy on payment {pay.payment_number}.",
                owner_id=actor.id,
                department_id=pr.department_id if pr else None,
                amount_minor=int(pay.executed_amount_minor or pay.payment_amount_minor),
                evidence_doc_id=pay.remittance_doc_id,
            )
        else:
            pay.status = "reconciled"
            pay.reconciliation_status = "matched"

        db.flush()

        # Recalculate total confirmed payments on the AccountPayable
        all_payments = db.query(PaymentRecord).filter(PaymentRecord.payable_id == ap.id).all()
        confirmed_paid = sum(
            int(p.executed_amount_minor or 0)
            for p in all_payments
            if p.status == "reconciled" and p.reconciliation_status == "matched"
        )
        if confirmed_paid >= int(ap.net_payable_minor):
            ap.status = "reconciled"
            if pr:
                pr.workflow_status = "reconciled"
                pr.updated_at = utc_now()
        elif confirmed_paid > 0:
            ap.status = "partially_paid"
            if pr:
                pr.workflow_status = "payment_processing"
                pr.updated_at = utc_now()
        ap.updated_at = utc_now()

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"payable_payment_reconciled_{reconciliation_outcome}",
            entity_type="payment_record",
            entity_id=pay.id,
            previous_state={"status": prev},
            new_state={
                "status": pay.status,
                "reconciliation_status": pay.reconciliation_status,
                "payable_status": ap.status,
                "confirmed_paid_minor": confirmed_paid,
            },
            reason=notes,
        )
        db.commit()
        db.refresh(pay)
        return pay.to_dict()

    def mark_payable_payment_exception(
        self,
        db: Session,
        actor: ResolvedActor,
        payment_id: str,
        target_status: str,  # failed | payment_unknown | cancelled
        reason: str,
    ) -> dict[str, Any]:
        require_roles(actor, "maker", "verifier", "bank_approver", "finance_controller", action_desc="update payment exception status")
        if target_status not in ("failed", "payment_unknown", "cancelled"):
            raise FinanceValidationError("target_status must be 'failed', 'payment_unknown', or 'cancelled'.")
        pay = db.query(PaymentRecord).filter(PaymentRecord.id == payment_id).first()
        if not pay:
            raise FinanceNotFoundError("Payment record not found.")
        if target_status not in VALID_PAYMENT_RECORD_TRANSITIONS.get(pay.status, set()):
            raise FinanceConflictError(f"Cannot transition payment from '{pay.status}' to '{target_status}'.")

        prev = pay.status
        pay.status = target_status
        pay.failure_reason = reason
        pay.updated_at = utc_now()

        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pay.purchase_request_id).first()
        if pr and target_status == "payment_unknown":
            pr.workflow_status = "payment_unknown"
            pr.updated_at = utc_now()
            _create_exception_register_entry(
                db=db,
                entity_type="payment_record",
                entity_id=pay.id,
                exception_category="payment_unknown_investigation",
                severity="high",
                reason=f"Payment {pay.payment_number} outcome is uncertain ({reason}). Do not auto-retry until reconciled.",
                owner_id=actor.id,
                department_id=pr.department_id,
                amount_minor=int(pay.payment_amount_minor),
            )

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"payable_payment_{target_status}",
            entity_type="payment_record",
            entity_id=pay.id,
            previous_state={"status": prev},
            new_state={"status": target_status},
            reason=reason,
        )
        db.commit()
        db.refresh(pay)
        return pay.to_dict()

    def get_purchase_request_detail(self, db: Session, actor: ResolvedActor, pr_id: str) -> dict[str, Any]:
        pr = db.query(PurchaseRequest).filter(PurchaseRequest.id == pr_id).first()
        if not pr:
            raise FinanceNotFoundError("Purchase request not found.")
        if pr.is_restricted_category and not actor.has_role("finance_reviewer", "finance_controller", "bank_approver", "auditor") and pr.requestor_id != actor.id:
            raise FinanceForbiddenError("Restricted payroll/statutory purchase request requires Finance or Controller authorization.")
        if pr.requestor_id != actor.id and not actor.can_access_department(pr.department_id):
            raise FinanceForbiddenError("Not authorized to view this purchase request.")

        lines = db.query(PurchaseRequestLine).filter(PurchaseRequestLine.purchase_request_id == pr.id).all()
        payables = db.query(AccountPayable).filter(AccountPayable.purchase_request_id == pr.id).all()
        payable_dicts = [self.get_payable_detail(db, actor, ap.id) for ap in payables]
        return pr.to_dict(
            lines=[l.to_dict() for l in lines],
            payables=payable_dicts,
        )

    def get_payable_detail(self, db: Session, actor: ResolvedActor, payable_id: str) -> dict[str, Any]:
        ap = db.query(AccountPayable).filter(AccountPayable.id == payable_id).first()
        if not ap:
            raise FinanceNotFoundError("Account Payable not found.")
        payments = (
            db.query(PaymentRecord)
            .filter(PaymentRecord.payable_id == ap.id)
            .order_by(PaymentRecord.created_at.asc())
            .all()
        )
        confirmed = sum(
            int(p.executed_amount_minor or 0)
            for p in payments
            if p.status == "reconciled" and p.reconciliation_status == "matched"
        )
        unverified = sum(
            int(p.executed_amount_minor or p.payment_amount_minor)
            for p in payments
            if p.status == "paid_unverified"
        )
        return ap.to_dict(
            paid_confirmed_minor=confirmed,
            paid_unverified_minor=unverified,
            payments=[p.to_dict() for p in payments],
        )


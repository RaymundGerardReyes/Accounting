import os
import uuid
import json
import hashlib
from datetime import datetime, timezone
from sqlalchemy import (
    Column,
    String,
    BigInteger,
    Integer,
    Text,
    DateTime,
    ForeignKey,
    UniqueConstraint,
    Index,
    event,
)
from database import Base


def utc_now():
    return datetime.now(timezone.utc)


def _loads_or(raw: str | None, fallback):
    if not raw:
        return fallback
    try:
        return json.loads(raw)
    except Exception:
        return fallback


# ==============================================================================
# 1. Identity, RBAC, Approval Matrix, Number Sequences & Holiday Calendar
# ==============================================================================

class FinanceUser(Base):
    __tablename__ = "users_finance"

    id = Column(String(64), primary_key=True)
    employee_code = Column(String(64), unique=True, nullable=False, index=True)
    full_name = Column(String(128), nullable=False)
    email = Column(String(128), nullable=False)
    department_id = Column(String(64), nullable=False, index=True)
    cost_center_id = Column(String(32), nullable=False, index=True)
    employee_group = Column(String(64), nullable=False, default="field_staff")
    status = Column(String(32), nullable=False, default="active")
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self, roles: list[str] | None = None):
        return {
            "id": self.id,
            "employee_code": self.employee_code,
            "full_name": self.full_name,
            "email": self.email,
            "department_id": self.department_id,
            "cost_center_id": self.cost_center_id,
            "employee_group": self.employee_group,
            "status": self.status,
            "roles": roles if roles is not None else [],
            "created_at": self.created_at.isoformat() if self.created_at else None,
        }


class RoleAssignment(Base):
    __tablename__ = "role_assignments"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    user_id = Column(String(64), ForeignKey("users_finance.id", ondelete="CASCADE"), nullable=False, index=True)
    role = Column(String(64), nullable=False, index=True)
    department_scope = Column(String(64), nullable=False, default="*")
    is_active = Column(Integer, nullable=False, default=1)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    __table_args__ = (
        UniqueConstraint("user_id", "role", "department_scope", name="uq_role_assignment_user_role_scope"),
    )

    def to_dict(self):
        return {
            "id": self.id,
            "user_id": self.user_id,
            "role": self.role,
            "department_scope": self.department_scope,
            "is_active": bool(self.is_active),
        }


class ApprovalMatrix(Base):
    __tablename__ = "approval_matrix"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    workflow = Column(String(64), nullable=False, index=True)
    stage = Column(String(64), nullable=False, index=True)
    role = Column(String(64), nullable=False, index=True)
    department_scope = Column(String(64), nullable=False, default="*")
    min_amount_minor = Column(BigInteger, nullable=False, default=0)
    max_amount_minor = Column(BigInteger, nullable=False)
    currency = Column(String(3), nullable=False, default="PHP")
    is_active = Column(Integer, nullable=False, default=1)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "workflow": self.workflow,
            "stage": self.stage,
            "role": self.role,
            "department_scope": self.department_scope,
            "min_amount_minor": self.min_amount_minor,
            "max_amount_minor": self.max_amount_minor,
            "currency": self.currency,
            "is_active": bool(self.is_active),
        }


class NumberSequence(Base):
    __tablename__ = "number_sequences"

    prefix = Column(String(32), primary_key=True)
    current_value = Column(BigInteger, nullable=False, default=0)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)


class HolidayCalendar(Base):
    __tablename__ = "holiday_calendar"

    holiday_date = Column(String(10), primary_key=True)  # YYYY-MM-DD
    name = Column(String(128), nullable=False)
    country_code = Column(String(8), nullable=False, default="PH")

    def to_dict(self):
        return {
            "holiday_date": self.holiday_date,
            "name": self.name,
            "country_code": self.country_code,
        }


# ==============================================================================
# 2. Append-Only Hash-Chained Audit Trail
# ==============================================================================

class AuditEvent(Base):
    __tablename__ = "audit_events"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    sequence_no = Column(BigInteger, nullable=False, unique=True, index=True)
    actor_id = Column(String(64), nullable=False, index=True)
    actor_role = Column(String(128), nullable=False)
    action = Column(String(128), nullable=False, index=True)
    entity_type = Column(String(64), nullable=False, index=True)
    entity_id = Column(String(64), nullable=False, index=True)
    previous_state = Column(Text, nullable=True)
    new_state = Column(Text, nullable=True)
    reason = Column(Text, nullable=True)
    correlation_id = Column(String(64), nullable=True, index=True)
    evidence_ref = Column(String(64), nullable=True)
    occurred_at = Column(DateTime(timezone=True), default=utc_now, nullable=False, index=True)
    prev_hash = Column(String(64), nullable=False)
    record_hash = Column(String(64), nullable=False, unique=True)

    def to_dict(self):
        return {
            "id": self.id,
            "sequence_no": self.sequence_no,
            "actor_id": self.actor_id,
            "actor_role": self.actor_role,
            "action": self.action,
            "entity_type": self.entity_type,
            "entity_id": self.entity_id,
            "previous_state": _loads_or(self.previous_state, self.previous_state),
            "new_state": _loads_or(self.new_state, self.new_state),
            "reason": self.reason,
            "correlation_id": self.correlation_id,
            "evidence_ref": self.evidence_ref,
            "occurred_at": self.occurred_at.isoformat() if self.occurred_at else None,
            "prev_hash": self.prev_hash,
            "record_hash": self.record_hash,
        }


# ==============================================================================
# 3. Configurable Financial Policy & Exception Management
# ==============================================================================

class FinancialPolicy(Base):
    __tablename__ = "financial_policies"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    policy_code = Column(String(64), unique=True, nullable=False, index=True)
    name = Column(String(255), nullable=False)
    description = Column(Text, nullable=True)
    scope_workflow = Column(String(64), nullable=False, index=True)
    applicable_employee_groups = Column(String(255), nullable=False, default="field_staff")
    applicable_departments = Column(String(255), nullable=False, default="*")
    current_version_id = Column(String(36), nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "policy_code": self.policy_code,
            "name": self.name,
            "description": self.description,
            "scope_workflow": self.scope_workflow,
            "applicable_employee_groups": _loads_or(self.applicable_employee_groups, [self.applicable_employee_groups]),
            "applicable_departments": _loads_or(self.applicable_departments, [self.applicable_departments]),
            "current_version_id": self.current_version_id,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }


class PolicyVersion(Base):
    __tablename__ = "policy_versions"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    policy_id = Column(String(36), ForeignKey("financial_policies.id", ondelete="CASCADE"), nullable=False, index=True)
    version_number = Column(BigInteger, nullable=False)
    status = Column(String(32), nullable=False, default="draft", index=True)
    # draft | pending_approval | active | retired
    effective_from = Column(String(10), nullable=False, index=True)  # YYYY-MM-DD
    effective_until = Column(String(10), nullable=True, index=True)  # YYYY-MM-DD
    currency = Column(String(3), nullable=False, default="PHP")
    aggregate_monthly_limit_minor = Column(BigInteger, nullable=False)
    max_reports_per_period = Column(BigInteger, nullable=False, default=1)
    period_type = Column(String(32), nullable=False, default="monthly")
    submission_deadline_day_of_next_month = Column(BigInteger, nullable=True, default=2)
    deadline_interpretation = Column(String(64), nullable=False, default="day_of_following_month_unconfirmed")
    finance_sla_days = Column(BigInteger, nullable=False, default=5)
    sla_day_type = Column(String(32), nullable=False, default="business_days_unconfirmed")
    require_consolidated_receipt_pdf = Column(Integer, nullable=False, default=1)
    require_mfar_for_out_of_base = Column(Integer, nullable=False, default=1)
    allow_partial_line_approval = Column(Integer, nullable=False, default=1)
    block_new_advance_when_overdue = Column(Integer, nullable=False, default=1)
    max_advance_amount_minor = Column(BigInteger, nullable=True)
    eligible_categories_json = Column(Text, nullable=False)
    previous_values_json = Column(Text, nullable=True)
    change_justification = Column(Text, nullable=False)
    ratification_status = Column(String(64), nullable=False, default="pending_finance_ratification")
    author_id = Column(String(64), nullable=False)
    approver_id = Column(String(64), nullable=True)
    submitted_at = Column(DateTime(timezone=True), nullable=True)
    approved_at = Column(DateTime(timezone=True), nullable=True)
    retired_at = Column(DateTime(timezone=True), nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    __table_args__ = (
        UniqueConstraint("policy_id", "version_number", name="uq_policy_version_number"),
    )

    def to_dict(self, rules: list[dict] | None = None):
        return {
            "id": self.id,
            "policy_id": self.policy_id,
            "version_number": self.version_number,
            "status": self.status,
            "effective_from": self.effective_from,
            "effective_until": self.effective_until,
            "currency": self.currency,
            "aggregate_monthly_limit_minor": self.aggregate_monthly_limit_minor,
            "max_reports_per_period": self.max_reports_per_period,
            "period_type": self.period_type,
            "submission_deadline_day_of_next_month": self.submission_deadline_day_of_next_month,
            "deadline_interpretation": self.deadline_interpretation,
            "finance_sla_days": self.finance_sla_days,
            "sla_day_type": self.sla_day_type,
            "require_consolidated_receipt_pdf": bool(self.require_consolidated_receipt_pdf),
            "require_mfar_for_out_of_base": bool(self.require_mfar_for_out_of_base),
            "allow_partial_line_approval": bool(self.allow_partial_line_approval),
            "block_new_advance_when_overdue": bool(self.block_new_advance_when_overdue),
            "max_advance_amount_minor": self.max_advance_amount_minor,
            "eligible_categories": _loads_or(self.eligible_categories_json, []),
            "previous_values": _loads_or(self.previous_values_json, None),
            "change_justification": self.change_justification,
            "ratification_status": self.ratification_status,
            "author_id": self.author_id,
            "approver_id": self.approver_id,
            "submitted_at": self.submitted_at.isoformat() if self.submitted_at else None,
            "approved_at": self.approved_at.isoformat() if self.approved_at else None,
            "retired_at": self.retired_at.isoformat() if self.retired_at else None,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "rules": rules if rules is not None else [],
        }


class PolicyRule(Base):
    __tablename__ = "policy_rules"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    policy_version_id = Column(String(36), ForeignKey("policy_versions.id", ondelete="CASCADE"), nullable=False, index=True)
    rule_code = Column(String(64), nullable=False, index=True)
    description = Column(String(255), nullable=False)
    severity = Column(String(32), nullable=False, default="block")  # block | warning | flag
    enforcement_mode = Column(String(32), nullable=False, default="exception_allowed")  # hard_block | exception_allowed | warn_only
    is_exception_allowed = Column(Integer, nullable=False, default=1)
    required_exception_role = Column(String(64), nullable=False, default="finance_controller")
    parameters_json = Column(Text, nullable=False, default="{}")

    def to_dict(self):
        return {
            "id": self.id,
            "policy_version_id": self.policy_version_id,
            "rule_code": self.rule_code,
            "description": self.description,
            "severity": self.severity,
            "enforcement_mode": self.enforcement_mode,
            "is_exception_allowed": bool(self.is_exception_allowed),
            "required_exception_role": self.required_exception_role,
            "parameters": _loads_or(self.parameters_json, {}),
        }


class PeriodCounter(Base):
    __tablename__ = "period_counters"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    employee_id = Column(String(64), nullable=False, index=True)
    period_key = Column(String(16), nullable=False, index=True)  # YYYY-MM
    rule_code = Column(String(64), nullable=False)
    submission_count = Column(BigInteger, nullable=False, default=0)
    aggregate_amount_minor = Column(BigInteger, nullable=False, default=0)
    active_report_id = Column(String(36), nullable=True)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    __table_args__ = (
        UniqueConstraint("employee_id", "period_key", "rule_code", name="uq_period_counter_emp_period_rule"),
    )


class ExceptionRequest(Base):
    __tablename__ = "exception_requests"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    exception_number = Column(String(64), unique=True, nullable=False, index=True)
    employee_id = Column(String(64), nullable=False, index=True)
    requester_id = Column(String(64), nullable=False, index=True)
    target_entity_type = Column(String(64), nullable=False, default="expense_report")
    target_entity_id = Column(String(36), nullable=True, index=True)
    reporting_period = Column(String(16), nullable=False, index=True)  # YYYY-MM
    policy_version_id = Column(String(36), ForeignKey("policy_versions.id"), nullable=False)
    violated_rule_code = Column(String(64), nullable=False, index=True)
    exception_type = Column(String(64), nullable=False)
    requested_amount_minor = Column(BigInteger, nullable=False, default=0)
    approved_max_amount_minor = Column(BigInteger, nullable=True)
    currency = Column(String(3), nullable=False, default="PHP")
    business_justification = Column(Text, nullable=False)
    supporting_document_ids_json = Column(Text, nullable=True)
    status = Column(String(32), nullable=False, default="draft", index=True)
    # draft | submitted | under_review | approved | rejected | expired | consumed
    assigned_reviewer_id = Column(String(64), nullable=True)
    approver_id = Column(String(64), nullable=True)
    decision_reason = Column(Text, nullable=True)
    submitted_at = Column(DateTime(timezone=True), nullable=True)
    decided_at = Column(DateTime(timezone=True), nullable=True)
    expires_at = Column(DateTime(timezone=True), nullable=True)
    consumed_at = Column(DateTime(timezone=True), nullable=True)
    consumed_by_entity_id = Column(String(36), nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "exception_number": self.exception_number,
            "employee_id": self.employee_id,
            "requester_id": self.requester_id,
            "target_entity_type": self.target_entity_type,
            "target_entity_id": self.target_entity_id,
            "reporting_period": self.reporting_period,
            "policy_version_id": self.policy_version_id,
            "violated_rule_code": self.violated_rule_code,
            "exception_type": self.exception_type,
            "requested_amount_minor": self.requested_amount_minor,
            "approved_max_amount_minor": self.approved_max_amount_minor,
            "currency": self.currency,
            "business_justification": self.business_justification,
            "supporting_document_ids": _loads_or(self.supporting_document_ids_json, []),
            "status": self.status,
            "assigned_reviewer_id": self.assigned_reviewer_id,
            "approver_id": self.approver_id,
            "decision_reason": self.decision_reason,
            "submitted_at": self.submitted_at.isoformat() if self.submitted_at else None,
            "decided_at": self.decided_at.isoformat() if self.decided_at else None,
            "expires_at": self.expires_at.isoformat() if self.expires_at else None,
            "consumed_at": self.consumed_at.isoformat() if self.consumed_at else None,
            "consumed_by_entity_id": self.consumed_by_entity_id,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }


# ==============================================================================
# 4. Cash Advances, Approval History, Disbursements & Append-Only Ledger
# ==============================================================================

class CashAdvance(Base):
    __tablename__ = "cash_advances"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    advance_number = Column(String(64), unique=True, nullable=False, index=True)
    requestor_id = Column(String(64), ForeignKey("users_finance.id"), nullable=False, index=True)
    employee_id = Column(String(64), ForeignKey("users_finance.id"), nullable=False, index=True)
    department_id = Column(String(64), nullable=False, index=True)
    cost_center_id = Column(String(32), nullable=False, index=True)
    purpose_code = Column(String(64), nullable=False)
    business_justification = Column(Text, nullable=False)
    destination_or_site = Column(String(255), nullable=True)
    requested_amount_minor = Column(BigInteger, nullable=False)
    approved_amount_minor = Column(BigInteger, nullable=True)
    currency = Column(String(3), nullable=False, default="PHP")
    date_needed = Column(String(10), nullable=False)
    activity_start_date = Column(String(10), nullable=True)
    activity_end_date = Column(String(10), nullable=True)
    liquidation_due_date = Column(String(10), nullable=True, index=True)
    remarks = Column(Text, nullable=True)
    attachment_ids_json = Column(Text, nullable=True)

    # Five independent state machines (no single linear status column)
    approval_status = Column(String(32), nullable=False, default="draft", index=True)
    payment_status = Column(String(32), nullable=False, default="not_started", index=True)
    liquidation_review_status = Column(String(32), nullable=False, default="not_started", index=True)
    settlement_status = Column(String(32), nullable=False, default="open", index=True)
    posting_status = Column(String(32), nullable=False, default="not_applicable", index=True)

    policy_version_id = Column(String(36), nullable=True)
    policy_evaluation_json = Column(Text, nullable=True)
    idempotency_key = Column(String(128), nullable=True, unique=True)
    submitted_at = Column(DateTime(timezone=True), nullable=True)
    approved_at = Column(DateTime(timezone=True), nullable=True)
    closed_at = Column(DateTime(timezone=True), nullable=True)
    closed_by_id = Column(String(64), nullable=True)
    closure_reason = Column(Text, nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def derive_overall_stage(self) -> str:
        if self.settlement_status == "closed":
            return "Closed"
        if self.settlement_status == "reconciled":
            return "Reconciled"
        if self.settlement_status == "shortfall_receivable":
            return "Shortfall Due from Employee"
        if self.liquidation_review_status == "approved":
            return "Liquidation Approved"
        if self.liquidation_review_status in ("submitted", "finance_review"):
            return "Liquidation Under Review"
        if self.payment_status == "evidence_verified":
            return "Released"
        if self.payment_status in ("prepared", "verified", "bank_approved", "executed_unverified"):
            return "Payment Processing"
        if self.approval_status == "approved":
            return "Approved (Unreleased)"
        if self.approval_status == "submitted":
            return "Submitted"
        if self.approval_status == "returned_for_correction":
            return "Returned for Correction"
        if self.approval_status == "rejected":
            return "Rejected"
        if self.approval_status == "cancelled":
            return "Cancelled"
        return "Draft"

    def to_dict(self, reconciliation: dict | None = None, approvals: list[dict] | None = None, disbursements: list[dict] | None = None):
        data = {
            "id": self.id,
            "advance_number": self.advance_number,
            "requestor_id": self.requestor_id,
            "employee_id": self.employee_id,
            "department_id": self.department_id,
            "cost_center_id": self.cost_center_id,
            "purpose_code": self.purpose_code,
            "business_justification": self.business_justification,
            "destination_or_site": self.destination_or_site,
            "requested_amount_minor": self.requested_amount_minor,
            "approved_amount_minor": self.approved_amount_minor,
            "currency": self.currency,
            "date_needed": self.date_needed,
            "activity_start_date": self.activity_start_date,
            "activity_end_date": self.activity_end_date,
            "liquidation_due_date": self.liquidation_due_date,
            "remarks": self.remarks,
            "attachment_ids": _loads_or(self.attachment_ids_json, []),
            "approval_status": self.approval_status,
            "payment_status": self.payment_status,
            "liquidation_review_status": self.liquidation_review_status,
            "settlement_status": self.settlement_status,
            "posting_status": self.posting_status,
            "overall_stage": self.derive_overall_stage(),
            "policy_version_id": self.policy_version_id,
            "policy_evaluation": _loads_or(self.policy_evaluation_json, None),
            "submitted_at": self.submitted_at.isoformat() if self.submitted_at else None,
            "approved_at": self.approved_at.isoformat() if self.approved_at else None,
            "closed_at": self.closed_at.isoformat() if self.closed_at else None,
            "closed_by_id": self.closed_by_id,
            "closure_reason": self.closure_reason,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }
        if reconciliation is not None:
            data["reconciliation"] = reconciliation
        if approvals is not None:
            data["approval_history"] = approvals
        if disbursements is not None:
            data["disbursements"] = disbursements
        return data


class ApprovalHistory(Base):
    __tablename__ = "approval_history"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    entity_type = Column(String(64), nullable=False, index=True)
    entity_id = Column(String(36), nullable=False, index=True)
    approver_id = Column(String(64), nullable=False, index=True)
    approver_role = Column(String(64), nullable=False)
    approval_stage = Column(String(64), nullable=False)
    decision = Column(String(32), nullable=False)
    approved_amount_minor = Column(BigInteger, nullable=True)
    comments = Column(Text, nullable=False)
    previous_status = Column(String(32), nullable=False)
    resulting_status = Column(String(32), nullable=False)
    decided_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "entity_type": self.entity_type,
            "entity_id": self.entity_id,
            "approver_id": self.approver_id,
            "approver_role": self.approver_role,
            "approval_stage": self.approval_stage,
            "decision": self.decision,
            "approved_amount_minor": self.approved_amount_minor,
            "comments": self.comments,
            "previous_status": self.previous_status,
            "resulting_status": self.resulting_status,
            "decided_at": self.decided_at.isoformat() if self.decided_at else None,
        }


class Disbursement(Base):
    __tablename__ = "disbursements"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    disbursement_number = Column(String(64), unique=True, nullable=False, index=True)
    advance_id = Column(String(36), ForeignKey("cash_advances.id", ondelete="RESTRICT"), nullable=False, index=True)
    payment_amount_minor = Column(BigInteger, nullable=False)
    executed_amount_minor = Column(BigInteger, nullable=True)
    currency = Column(String(3), nullable=False, default="PHP")
    payment_method = Column(String(64), nullable=False, default="bank_transfer")
    recipient_account_ref = Column(String(128), nullable=True)
    status = Column(String(32), nullable=False, default="prepared", index=True)
    # prepared | verified | bank_approved | executed_unverified | evidence_verified | failed | unknown | cancelled

    maker_id = Column(String(64), nullable=False)
    prepared_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    verifier_id = Column(String(64), nullable=True)
    verified_at = Column(DateTime(timezone=True), nullable=True)
    verification_notes = Column(Text, nullable=True)
    bank_approver_id = Column(String(64), nullable=True)
    bank_approved_at = Column(DateTime(timezone=True), nullable=True)
    bank_approval_notes = Column(Text, nullable=True)
    executed_by_id = Column(String(64), nullable=True)
    executed_at = Column(DateTime(timezone=True), nullable=True)
    bank_reference = Column(String(128), nullable=True, index=True)
    evidence_document_id = Column(String(36), nullable=True)
    evidence_verifier_id = Column(String(64), nullable=True)
    evidence_verified_at = Column(DateTime(timezone=True), nullable=True)
    evidence_verification_notes = Column(Text, nullable=True)
    failure_reason = Column(Text, nullable=True)
    idempotency_key = Column(String(128), nullable=True, unique=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "disbursement_number": self.disbursement_number,
            "advance_id": self.advance_id,
            "payment_amount_minor": self.payment_amount_minor,
            "executed_amount_minor": self.executed_amount_minor,
            "currency": self.currency,
            "payment_method": self.payment_method,
            "recipient_account_ref": self.recipient_account_ref,
            "status": self.status,
            "maker_id": self.maker_id,
            "prepared_at": self.prepared_at.isoformat() if self.prepared_at else None,
            "verifier_id": self.verifier_id,
            "verified_at": self.verified_at.isoformat() if self.verified_at else None,
            "verification_notes": self.verification_notes,
            "bank_approver_id": self.bank_approver_id,
            "bank_approved_at": self.bank_approved_at.isoformat() if self.bank_approved_at else None,
            "bank_approval_notes": self.bank_approval_notes,
            "executed_by_id": self.executed_by_id,
            "executed_at": self.executed_at.isoformat() if self.executed_at else None,
            "bank_reference": self.bank_reference,
            "evidence_document_id": self.evidence_document_id,
            "evidence_verifier_id": self.evidence_verifier_id,
            "evidence_verified_at": self.evidence_verified_at.isoformat() if self.evidence_verified_at else None,
            "evidence_verification_notes": self.evidence_verification_notes,
            "failure_reason": self.failure_reason,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }


class AdvanceLedgerEntry(Base):
    __tablename__ = "advance_ledger_entries"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    advance_id = Column(String(36), ForeignKey("cash_advances.id", ondelete="RESTRICT"), nullable=False, index=True)
    employee_id = Column(String(64), nullable=False, index=True)
    entry_type = Column(String(64), nullable=False, index=True)
    # disbursement_release | expense_applied | cash_return | reimbursement_payable | reimbursement_settled | adjustment | reversal
    signed_amount_minor = Column(BigInteger, nullable=False)
    amount_minor = Column(BigInteger, nullable=False)
    currency = Column(String(3), nullable=False, default="PHP")
    source_type = Column(String(64), nullable=False)
    source_id = Column(String(36), nullable=False, index=True)
    reference_code = Column(String(128), nullable=True)
    evidence_document_id = Column(String(36), nullable=True)
    actor_id = Column(String(64), nullable=False)
    notes = Column(Text, nullable=True)
    effective_at = Column(DateTime(timezone=True), default=utc_now, nullable=False, index=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "advance_id": self.advance_id,
            "employee_id": self.employee_id,
            "entry_type": self.entry_type,
            "signed_amount_minor": self.signed_amount_minor,
            "amount_minor": self.amount_minor,
            "currency": self.currency,
            "source_type": self.source_type,
            "source_id": self.source_id,
            "reference_code": self.reference_code,
            "evidence_document_id": self.evidence_document_id,
            "actor_id": self.actor_id,
            "notes": self.notes,
            "effective_at": self.effective_at.isoformat() if self.effective_at else None,
            "created_at": self.created_at.isoformat() if self.created_at else None,
        }


# ==============================================================================
# 5. Document Evidence, Expense Reports, Expense Lines, Settlement & Closure
# ==============================================================================

class DocumentReference(Base):
    __tablename__ = "document_references"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    document_number = Column(String(64), unique=True, nullable=False, index=True)
    uploader_id = Column(String(64), nullable=False, index=True)
    department_id = Column(String(64), nullable=False, index=True)
    document_type = Column(String(64), nullable=False, index=True)
    entity_type = Column(String(64), nullable=True, index=True)
    entity_id = Column(String(36), nullable=True, index=True)
    original_filename = Column(String(255), nullable=False)
    stored_filename = Column(String(255), unique=True, nullable=False)
    mime_type = Column(String(128), nullable=False)
    size_bytes = Column(BigInteger, nullable=False)
    sha256_hash = Column(String(64), nullable=False, index=True)
    is_restricted = Column(Integer, nullable=False, default=0)
    retention_status = Column(String(32), nullable=False, default="active")
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "document_number": self.document_number,
            "uploader_id": self.uploader_id,
            "department_id": self.department_id,
            "document_type": self.document_type,
            "entity_type": self.entity_type,
            "entity_id": self.entity_id,
            "original_filename": self.original_filename,
            "mime_type": self.mime_type,
            "size_bytes": self.size_bytes,
            "sha256_hash": self.sha256_hash,
            "is_restricted": bool(self.is_restricted),
            "retention_status": self.retention_status,
            "download_url": f"/api/v1/finance/documents/{self.id}/download",
            "created_at": self.created_at.isoformat() if self.created_at else None,
        }


class ExpenseReport(Base):
    __tablename__ = "expense_reports"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    report_number = Column(String(64), unique=True, nullable=False, index=True)
    employee_id = Column(String(64), ForeignKey("users_finance.id"), nullable=False, index=True)
    department_id = Column(String(64), nullable=False, index=True)
    cost_center_id = Column(String(32), nullable=False, index=True)
    reporting_period = Column(String(16), nullable=False, index=True)  # YYYY-MM
    business_purpose_summary = Column(Text, nullable=False)
    out_of_base_activity = Column(Integer, nullable=False, default=0)
    consolidated_receipt_doc_id = Column(String(36), nullable=True)
    mfar_doc_id = Column(String(36), nullable=True)
    employee_declaration_confirmed = Column(Integer, nullable=False, default=0)
    remarks = Column(Text, nullable=True)
    currency = Column(String(3), nullable=False, default="PHP")

    review_status = Column(String(32), nullable=False, default="draft", index=True)
    # draft | submitted | finance_review | returned_for_correction | approved | partially_approved | rejected | cancelled
    settlement_status = Column(String(32), nullable=False, default="unapplied", index=True)
    # unapplied | partially_applied | applied | reimbursement_pending | settled
    posting_status = Column(String(32), nullable=False, default="not_applicable")

    policy_version_id = Column(String(36), nullable=True)
    policy_evaluation_json = Column(Text, nullable=True)
    exception_request_id = Column(String(36), nullable=True)
    reviewer_id = Column(String(64), nullable=True)
    reviewed_at = Column(DateTime(timezone=True), nullable=True)
    review_notes = Column(Text, nullable=True)
    rejection_reason = Column(Text, nullable=True)
    submitted_at = Column(DateTime(timezone=True), nullable=True)
    slot_consumed = Column(Integer, nullable=False, default=0)
    idempotency_key = Column(String(128), nullable=True, unique=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self, lines: list[dict] | None = None, applications: list[dict] | None = None):
        line_items = lines if lines is not None else []
        total_claimed_minor = sum(int(l.get("claimed_amount_minor", 0)) for l in line_items)
        total_approved_minor = sum(
            int(l.get("approved_amount_minor") or 0)
            for l in line_items
            if l.get("line_status") in ("approved", "partially_approved")
        )
        total_rejected_minor = sum(
            int(l.get("claimed_amount_minor", 0)) - int(l.get("approved_amount_minor") or 0)
            for l in line_items
            if l.get("line_status") in ("rejected", "partially_approved")
        )
        data = {
            "id": self.id,
            "report_number": self.report_number,
            "employee_id": self.employee_id,
            "department_id": self.department_id,
            "cost_center_id": self.cost_center_id,
            "reporting_period": self.reporting_period,
            "business_purpose_summary": self.business_purpose_summary,
            "out_of_base_activity": bool(self.out_of_base_activity),
            "consolidated_receipt_doc_id": self.consolidated_receipt_doc_id,
            "mfar_doc_id": self.mfar_doc_id,
            "employee_declaration_confirmed": bool(self.employee_declaration_confirmed),
            "remarks": self.remarks,
            "currency": self.currency,
            "review_status": self.review_status,
            "settlement_status": self.settlement_status,
            "posting_status": self.posting_status,
            "total_claimed_minor": total_claimed_minor,
            "total_approved_minor": total_approved_minor,
            "total_rejected_minor": total_rejected_minor,
            "policy_version_id": self.policy_version_id,
            "policy_evaluation": _loads_or(self.policy_evaluation_json, None),
            "exception_request_id": self.exception_request_id,
            "reviewer_id": self.reviewer_id,
            "reviewed_at": self.reviewed_at.isoformat() if self.reviewed_at else None,
            "review_notes": self.review_notes,
            "rejection_reason": self.rejection_reason,
            "submitted_at": self.submitted_at.isoformat() if self.submitted_at else None,
            "slot_consumed": bool(self.slot_consumed),
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }
        if lines is not None:
            data["lines"] = lines
        if applications is not None:
            data["settlement_applications"] = applications
        return data


class ExpenseLine(Base):
    __tablename__ = "expense_lines"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    expense_report_id = Column(String(36), ForeignKey("expense_reports.id", ondelete="CASCADE"), nullable=False, index=True)
    employee_id = Column(String(64), nullable=False, index=True)
    transaction_date = Column(String(10), nullable=False)
    school_or_destination = Column(String(255), nullable=True)
    expense_category = Column(String(64), nullable=False, index=True)
    business_purpose = Column(Text, nullable=False)
    merchant_or_payee = Column(String(255), nullable=False)
    receipt_or_invoice_no = Column(String(128), nullable=True, index=True)
    fingerprint_key = Column(String(255), nullable=True, index=True)
    claimed_amount_minor = Column(BigInteger, nullable=False)
    approved_amount_minor = Column(BigInteger, nullable=True)
    currency = Column(String(3), nullable=False, default="PHP")
    out_of_base = Column(Integer, nullable=False, default=0)
    receipt_document_id = Column(String(36), nullable=True)
    mfar_document_id = Column(String(36), nullable=True)
    tax_code = Column(String(64), nullable=True)
    vat_amount_minor = Column(BigInteger, nullable=False, default=0)
    duplicate_warning = Column(Integer, nullable=False, default=0)
    duplicate_of_line_id = Column(String(36), nullable=True)
    line_status = Column(String(32), nullable=False, default="pending", index=True)
    # pending | approved | partially_approved | rejected | returned
    reviewer_id = Column(String(64), nullable=True)
    reviewed_at = Column(DateTime(timezone=True), nullable=True)
    review_notes = Column(Text, nullable=True)
    rejection_reason = Column(Text, nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "expense_report_id": self.expense_report_id,
            "employee_id": self.employee_id,
            "transaction_date": self.transaction_date,
            "school_or_destination": self.school_or_destination,
            "expense_category": self.expense_category,
            "business_purpose": self.business_purpose,
            "merchant_or_payee": self.merchant_or_payee,
            "receipt_or_invoice_no": self.receipt_or_invoice_no,
            "claimed_amount_minor": self.claimed_amount_minor,
            "approved_amount_minor": self.approved_amount_minor,
            "currency": self.currency,
            "out_of_base": bool(self.out_of_base),
            "receipt_document_id": self.receipt_document_id,
            "mfar_document_id": self.mfar_document_id,
            "tax_code": self.tax_code,
            "vat_amount_minor": self.vat_amount_minor,
            "duplicate_warning": bool(self.duplicate_warning),
            "duplicate_of_line_id": self.duplicate_of_line_id,
            "line_status": self.line_status,
            "reviewer_id": self.reviewer_id,
            "reviewed_at": self.reviewed_at.isoformat() if self.reviewed_at else None,
            "review_notes": self.review_notes,
            "rejection_reason": self.rejection_reason,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }


class SettlementApplication(Base):
    __tablename__ = "settlement_applications"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    expense_report_id = Column(String(36), ForeignKey("expense_reports.id"), nullable=True, index=True)
    expense_line_id = Column(String(36), ForeignKey("expense_lines.id"), nullable=True, index=True)
    advance_id = Column(String(36), ForeignKey("cash_advances.id"), nullable=True, index=True)
    employee_id = Column(String(64), nullable=False, index=True)
    application_type = Column(String(64), nullable=False, index=True)
    # advance_offset | cash_return | excess_reimbursement_payable | shortfall_receivable
    applied_amount_minor = Column(BigInteger, nullable=False)
    currency = Column(String(3), nullable=False, default="PHP")
    ledger_entry_id = Column(String(36), nullable=True)
    evidence_document_id = Column(String(36), nullable=True)
    bank_reference = Column(String(128), nullable=True)
    applied_by_id = Column(String(64), nullable=False)
    notes = Column(Text, nullable=True)
    applied_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "expense_report_id": self.expense_report_id,
            "expense_line_id": self.expense_line_id,
            "advance_id": self.advance_id,
            "employee_id": self.employee_id,
            "application_type": self.application_type,
            "applied_amount_minor": self.applied_amount_minor,
            "currency": self.currency,
            "ledger_entry_id": self.ledger_entry_id,
            "evidence_document_id": self.evidence_document_id,
            "bank_reference": self.bank_reference,
            "applied_by_id": self.applied_by_id,
            "notes": self.notes,
            "applied_at": self.applied_at.isoformat() if self.applied_at else None,
        }


class ClosingSnapshot(Base):
    __tablename__ = "closing_snapshots"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    advance_id = Column(String(36), ForeignKey("cash_advances.id", ondelete="RESTRICT"), unique=True, nullable=False, index=True)
    released_amount_minor = Column(BigInteger, nullable=False)
    claimed_expense_minor = Column(BigInteger, nullable=False)
    approved_expense_minor = Column(BigInteger, nullable=False)
    rejected_expense_minor = Column(BigInteger, nullable=False)
    cash_returned_minor = Column(BigInteger, nullable=False)
    reimbursement_minor = Column(BigInteger, nullable=False)
    final_outstanding_minor = Column(BigInteger, nullable=False)
    authorized_exception_id = Column(String(36), nullable=True)
    controller_id = Column(String(64), nullable=False)
    closure_reason = Column(Text, nullable=False)
    snapshot_json = Column(Text, nullable=False)
    closed_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "advance_id": self.advance_id,
            "released_amount_minor": self.released_amount_minor,
            "claimed_expense_minor": self.claimed_expense_minor,
            "approved_expense_minor": self.approved_expense_minor,
            "rejected_expense_minor": self.rejected_expense_minor,
            "cash_returned_minor": self.cash_returned_minor,
            "reimbursement_minor": self.reimbursement_minor,
            "final_outstanding_minor": self.final_outstanding_minor,
            "authorized_exception_id": self.authorized_exception_id,
            "controller_id": self.controller_id,
            "closure_reason": self.closure_reason,
            "snapshot": _loads_or(self.snapshot_json, {}),
            "closed_at": self.closed_at.isoformat() if self.closed_at else None,
        }


# ==============================================================================
# 6. Purchase-to-Pay (Vendors, Purchase Requests, Accounts Payable, Payments)
# ==============================================================================

class Vendor(Base):
    __tablename__ = "vendors"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    vendor_code = Column(String(64), unique=True, nullable=False, index=True)
    registered_name = Column(String(255), nullable=False)
    payee_type = Column(String(64), nullable=False, index=True)
    # supplier | utility | statutory_agency | individual_professional | school_partner | payroll_clearing
    tin = Column(String(64), nullable=True)
    address = Column(Text, nullable=True)
    contact_person = Column(String(128), nullable=True)
    contact_email = Column(String(128), nullable=True)
    payment_method = Column(String(64), nullable=False, default="bank_transfer")
    bank_name = Column(String(128), nullable=True)
    bank_account_masked = Column(String(64), nullable=True)
    bank_account_ref = Column(String(128), nullable=True)
    verification_status = Column(String(32), nullable=False, default="pending_verification", index=True)
    # pending_verification | verified | suspended | rejected
    kyc_document_id = Column(String(36), nullable=True)
    created_by_id = Column(String(64), nullable=False)
    verified_by_id = Column(String(64), nullable=True)
    verified_at = Column(DateTime(timezone=True), nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self, include_protected_bank: bool = False):
        data = {
            "id": self.id,
            "vendor_code": self.vendor_code,
            "registered_name": self.registered_name,
            "payee_type": self.payee_type,
            "tin": self.tin,
            "address": self.address,
            "contact_person": self.contact_person,
            "contact_email": self.contact_email,
            "payment_method": self.payment_method,
            "bank_name": self.bank_name,
            "bank_account_masked": self.bank_account_masked,
            "verification_status": self.verification_status,
            "kyc_document_id": self.kyc_document_id,
            "created_by_id": self.created_by_id,
            "verified_by_id": self.verified_by_id,
            "verified_at": self.verified_at.isoformat() if self.verified_at else None,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }
        if include_protected_bank:
            data["bank_account_ref"] = self.bank_account_ref
        return data


class VendorBankChange(Base):
    __tablename__ = "vendor_bank_changes"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    vendor_id = Column(String(36), ForeignKey("vendors.id", ondelete="CASCADE"), nullable=False, index=True)
    previous_bank_ref = Column(String(128), nullable=True)
    new_bank_name = Column(String(128), nullable=False)
    new_bank_account_masked = Column(String(64), nullable=False)
    new_bank_account_ref = Column(String(128), nullable=False)
    supporting_document_id = Column(String(36), nullable=True)
    reason = Column(Text, nullable=False)
    status = Column(String(32), nullable=False, default="pending_approval", index=True)
    requested_by_id = Column(String(64), nullable=False)
    approved_by_id = Column(String(64), nullable=True)
    requested_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    decided_at = Column(DateTime(timezone=True), nullable=True)

    def to_dict(self, include_protected_bank: bool = False):
        data = {
            "id": self.id,
            "vendor_id": self.vendor_id,
            "new_bank_name": self.new_bank_name,
            "new_bank_account_masked": self.new_bank_account_masked,
            "supporting_document_id": self.supporting_document_id,
            "reason": self.reason,
            "status": self.status,
            "requested_by_id": self.requested_by_id,
            "approved_by_id": self.approved_by_id,
            "requested_at": self.requested_at.isoformat() if self.requested_at else None,
            "decided_at": self.decided_at.isoformat() if self.decided_at else None,
        }
        if include_protected_bank:
            data["previous_bank_ref"] = self.previous_bank_ref
            data["new_bank_account_ref"] = self.new_bank_account_ref
        return data


class TaxCode(Base):
    __tablename__ = "tax_codes"

    code = Column(String(64), primary_key=True)
    description = Column(String(255), nullable=False)
    tax_type = Column(String(32), nullable=False)  # VAT | EWT | NON_VAT | EXEMPT
    rate_bps = Column(BigInteger, nullable=False)  # basis points: 1200 = 12.00%
    requires_bir_2307 = Column(Integer, nullable=False, default=0)
    cpa_approved = Column(Integer, nullable=False, default=0)
    approved_by_id = Column(String(64), nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)

    def to_dict(self):
        return {
            "code": self.code,
            "description": self.description,
            "tax_type": self.tax_type,
            "rate_bps": self.rate_bps,
            "requires_bir_2307": bool(self.requires_bir_2307),
            "cpa_approved": bool(self.cpa_approved),
            "approved_by_id": self.approved_by_id,
            "created_at": self.created_at.isoformat() if self.created_at else None,
        }


class PurchaseRequest(Base):
    __tablename__ = "purchase_requests"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    request_number = Column(String(64), unique=True, nullable=False, index=True)
    requestor_id = Column(String(64), ForeignKey("users_finance.id"), nullable=False, index=True)
    vendor_id = Column(String(36), ForeignKey("vendors.id"), nullable=False, index=True)
    payment_category = Column(String(64), nullable=False, index=True)
    # supplier_invoice | office_supplies_utilities | school_support | royalties_honoraria | referral_fees | payroll_statutory
    is_restricted_category = Column(Integer, nullable=False, default=0)
    business_purpose = Column(Text, nullable=False)
    department_id = Column(String(64), nullable=False, index=True)
    cost_center_id = Column(String(32), nullable=False, index=True)
    budget_reference = Column(String(64), nullable=True)
    requested_amount_minor = Column(BigInteger, nullable=False)
    approved_amount_minor = Column(BigInteger, nullable=True)
    currency = Column(String(3), nullable=False, default="PHP")
    requested_payment_date = Column(String(10), nullable=False)
    due_date = Column(String(10), nullable=True)
    supporting_doc_ids_json = Column(Text, nullable=True)
    invoice_doc_id = Column(String(36), nullable=True)
    remarks = Column(Text, nullable=True)

    approval_status = Column(String(32), nullable=False, default="draft", index=True)
    # draft | submitted | approved | rejected | returned_for_correction | cancelled
    workflow_status = Column(String(32), nullable=False, default="draft", index=True)
    # draft | submitted | approved | rejected | returned_for_correction | payable_validated | payment_processing | paid_unverified | reconciled | failed | cancelled | payment_unknown
    posting_status = Column(String(32), nullable=False, default="not_applicable")

    idempotency_key = Column(String(128), nullable=True, unique=True)
    submitted_at = Column(DateTime(timezone=True), nullable=True)
    approved_at = Column(DateTime(timezone=True), nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self, lines: list[dict] | None = None, payables: list[dict] | None = None):
        data = {
            "id": self.id,
            "request_number": self.request_number,
            "requestor_id": self.requestor_id,
            "vendor_id": self.vendor_id,
            "payment_category": self.payment_category,
            "is_restricted_category": bool(self.is_restricted_category),
            "business_purpose": self.business_purpose,
            "department_id": self.department_id,
            "cost_center_id": self.cost_center_id,
            "budget_reference": self.budget_reference,
            "requested_amount_minor": self.requested_amount_minor,
            "approved_amount_minor": self.approved_amount_minor,
            "currency": self.currency,
            "requested_payment_date": self.requested_payment_date,
            "due_date": self.due_date,
            "supporting_doc_ids": _loads_or(self.supporting_doc_ids_json, []),
            "invoice_doc_id": self.invoice_doc_id,
            "remarks": self.remarks,
            "approval_status": self.approval_status,
            "workflow_status": self.workflow_status,
            "posting_status": self.posting_status,
            "submitted_at": self.submitted_at.isoformat() if self.submitted_at else None,
            "approved_at": self.approved_at.isoformat() if self.approved_at else None,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }
        if lines is not None:
            data["lines"] = lines
        if payables is not None:
            data["payables"] = payables
        return data


class PurchaseRequestLine(Base):
    __tablename__ = "purchase_request_lines"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    purchase_request_id = Column(String(36), ForeignKey("purchase_requests.id", ondelete="CASCADE"), nullable=False, index=True)
    description = Column(String(255), nullable=False)
    quantity = Column(BigInteger, nullable=False, default=1)
    unit_amount_minor = Column(BigInteger, nullable=False)
    total_amount_minor = Column(BigInteger, nullable=False)
    account_code = Column(String(64), nullable=True)

    def to_dict(self):
        return {
            "id": self.id,
            "purchase_request_id": self.purchase_request_id,
            "description": self.description,
            "quantity": self.quantity,
            "unit_amount_minor": self.unit_amount_minor,
            "total_amount_minor": self.total_amount_minor,
            "account_code": self.account_code,
        }


class AccountPayable(Base):
    __tablename__ = "account_payables"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    payable_number = Column(String(64), unique=True, nullable=False, index=True)
    purchase_request_id = Column(String(36), ForeignKey("purchase_requests.id", ondelete="RESTRICT"), nullable=False, index=True)
    vendor_id = Column(String(36), ForeignKey("vendors.id", ondelete="RESTRICT"), nullable=False, index=True)
    invoice_number = Column(String(128), nullable=False)
    normalized_invoice_no = Column(String(128), nullable=False, index=True)
    invoice_date = Column(String(10), nullable=False)
    due_date = Column(String(10), nullable=False, index=True)
    currency = Column(String(3), nullable=False, default="PHP")
    original_payable_minor = Column(BigInteger, nullable=False)
    credit_adjustment_minor = Column(BigInteger, nullable=False, default=0)
    vat_code = Column(String(64), nullable=True)
    vat_amount_minor = Column(BigInteger, nullable=False, default=0)
    ewt_code = Column(String(64), nullable=True)
    ewt_amount_minor = Column(BigInteger, nullable=False, default=0)
    net_payable_minor = Column(BigInteger, nullable=False)
    tax_review_status = Column(String(32), nullable=False, default="tax_review_required")
    duplicate_flag = Column(Integer, nullable=False, default=0)
    duplicate_reason = Column(Text, nullable=True)
    payment_terms = Column(String(64), nullable=True)
    accounting_reference = Column(String(128), nullable=True)
    posting_status = Column(String(32), nullable=False, default="pending")
    status = Column(String(32), nullable=False, default="open", index=True)
    # open | partially_paid | paid_unverified | paid | reconciled | disputed | cancelled
    validated_by_id = Column(String(64), nullable=False)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    __table_args__ = (
        UniqueConstraint("vendor_id", "normalized_invoice_no", "currency", name="uq_payable_vendor_invoice_currency"),
    )

    def to_dict(self, paid_confirmed_minor: int = 0, paid_unverified_minor: int = 0, payments: list[dict] | None = None):
        remaining_minor = max(0, int(self.net_payable_minor) - int(paid_confirmed_minor))
        data = {
            "id": self.id,
            "payable_number": self.payable_number,
            "purchase_request_id": self.purchase_request_id,
            "vendor_id": self.vendor_id,
            "invoice_number": self.invoice_number,
            "normalized_invoice_no": self.normalized_invoice_no,
            "invoice_date": self.invoice_date,
            "due_date": self.due_date,
            "currency": self.currency,
            "original_payable_minor": self.original_payable_minor,
            "credit_adjustment_minor": self.credit_adjustment_minor,
            "vat_code": self.vat_code,
            "vat_amount_minor": self.vat_amount_minor,
            "ewt_code": self.ewt_code,
            "ewt_amount_minor": self.ewt_amount_minor,
            "net_payable_minor": self.net_payable_minor,
            "paid_confirmed_minor": paid_confirmed_minor,
            "paid_unverified_minor": paid_unverified_minor,
            "remaining_balance_minor": remaining_minor,
            "tax_review_status": self.tax_review_status,
            "duplicate_flag": bool(self.duplicate_flag),
            "duplicate_reason": self.duplicate_reason,
            "payment_terms": self.payment_terms,
            "accounting_reference": self.accounting_reference,
            "posting_status": self.posting_status,
            "status": self.status,
            "validated_by_id": self.validated_by_id,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }
        if payments is not None:
            data["payments"] = payments
        return data


class PaymentRecord(Base):
    __tablename__ = "payment_records"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    payment_number = Column(String(64), unique=True, nullable=False, index=True)
    payable_id = Column(String(36), ForeignKey("account_payables.id", ondelete="RESTRICT"), nullable=False, index=True)
    purchase_request_id = Column(String(36), ForeignKey("purchase_requests.id", ondelete="RESTRICT"), nullable=False, index=True)
    vendor_id = Column(String(36), ForeignKey("vendors.id", ondelete="RESTRICT"), nullable=False, index=True)
    payment_amount_minor = Column(BigInteger, nullable=False)
    executed_amount_minor = Column(BigInteger, nullable=True)
    currency = Column(String(3), nullable=False, default="PHP")
    payment_method = Column(String(64), nullable=False, default="bank_transfer")
    status = Column(String(32), nullable=False, default="prepared", index=True)
    # prepared | verified | bank_approved | paid_unverified | reconciled | failed | payment_unknown | cancelled

    maker_id = Column(String(64), nullable=False)
    prepared_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    verifier_id = Column(String(64), nullable=True)
    verified_at = Column(DateTime(timezone=True), nullable=True)
    bank_approver_id = Column(String(64), nullable=True)
    bank_approved_at = Column(DateTime(timezone=True), nullable=True)
    executed_by_id = Column(String(64), nullable=True)
    execution_date = Column(String(10), nullable=True)
    executed_at = Column(DateTime(timezone=True), nullable=True)
    bank_reference = Column(String(128), nullable=True, index=True)
    remittance_doc_id = Column(String(36), nullable=True)
    evidence_verifier_id = Column(String(64), nullable=True)
    evidence_verified_at = Column(DateTime(timezone=True), nullable=True)
    reconciliation_status = Column(String(32), nullable=False, default="unreconciled")
    # unreconciled | matched | discrepancy
    reconciliation_notes = Column(Text, nullable=True)
    failure_reason = Column(Text, nullable=True)
    idempotency_key = Column(String(128), nullable=True, unique=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    def to_dict(self):
        return {
            "id": self.id,
            "payment_number": self.payment_number,
            "payable_id": self.payable_id,
            "purchase_request_id": self.purchase_request_id,
            "vendor_id": self.vendor_id,
            "payment_amount_minor": self.payment_amount_minor,
            "executed_amount_minor": self.executed_amount_minor,
            "currency": self.currency,
            "payment_method": self.payment_method,
            "status": self.status,
            "maker_id": self.maker_id,
            "prepared_at": self.prepared_at.isoformat() if self.prepared_at else None,
            "verifier_id": self.verifier_id,
            "verified_at": self.verified_at.isoformat() if self.verified_at else None,
            "bank_approver_id": self.bank_approver_id,
            "bank_approved_at": self.bank_approved_at.isoformat() if self.bank_approved_at else None,
            "executed_by_id": self.executed_by_id,
            "execution_date": self.execution_date,
            "executed_at": self.executed_at.isoformat() if self.executed_at else None,
            "bank_reference": self.bank_reference,
            "remittance_doc_id": self.remittance_doc_id,
            "evidence_verifier_id": self.evidence_verifier_id,
            "evidence_verified_at": self.evidence_verified_at.isoformat() if self.evidence_verified_at else None,
            "reconciliation_status": self.reconciliation_status,
            "reconciliation_notes": self.reconciliation_notes,
            "failure_reason": self.failure_reason,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }


# ==============================================================================
# 7. Exception Register (Operational Control Queue)
# ==============================================================================

class ExceptionRegister(Base):
    __tablename__ = "exception_register"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    register_number = Column(String(64), unique=True, nullable=False, index=True)
    entity_type = Column(String(64), nullable=False, index=True)
    entity_id = Column(String(36), nullable=False, index=True)
    exception_category = Column(String(64), nullable=False, index=True)
    severity = Column(String(32), nullable=False, default="medium", index=True)
    owner_id = Column(String(64), nullable=True, index=True)
    department_id = Column(String(64), nullable=True, index=True)
    amount_minor = Column(BigInteger, nullable=False, default=0)
    reason = Column(Text, nullable=False)
    evidence_doc_id = Column(String(36), nullable=True)
    resolution_notes = Column(Text, nullable=True)
    status = Column(String(32), nullable=False, default="open", index=True)
    # open | under_review | resolved | reopened
    reviewer_id = Column(String(64), nullable=True)
    detected_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    resolved_at = Column(DateTime(timezone=True), nullable=True)

    def to_dict(self):
        return {
            "id": self.id,
            "register_number": self.register_number,
            "entity_type": self.entity_type,
            "entity_id": self.entity_id,
            "exception_category": self.exception_category,
            "severity": self.severity,
            "owner_id": self.owner_id,
            "department_id": self.department_id,
            "amount_minor": self.amount_minor,
            "reason": self.reason,
            "evidence_doc_id": self.evidence_doc_id,
            "resolution_notes": self.resolution_notes,
            "status": self.status,
            "reviewer_id": self.reviewer_id,
            "detected_at": self.detected_at.isoformat() if self.detected_at else None,
            "resolved_at": self.resolved_at.isoformat() if self.resolved_at else None,
        }


# ==============================================================================
# Append-Only Immutability Guards (ORM Event Listeners)
# ==============================================================================

def _prevent_modification(mapper, connection, target):
    raise PermissionError(
        f"Append-only financial record ({target.__class__.__name__} id={getattr(target, 'id', None)}) cannot be modified or deleted."
    )


for _append_only_cls in (AuditEvent, AdvanceLedgerEntry, ApprovalHistory, ClosingSnapshot):
    event.listen(_append_only_cls, "before_update", _prevent_modification)
    event.listen(_append_only_cls, "before_delete", _prevent_modification)


# ==============================================================================
# Seeding Logic (Gated by FINANCE_SEED=dev)
# ==============================================================================

def seed_finance_db(db):
    seed_mode = os.getenv("FINANCE_SEED", "dev").lower()
    if seed_mode not in ("dev", "test", "true", "1"):
        return

    if db.query(FinanceUser).count() == 0:
        users = [
            FinanceUser(
                id="emp_field_01",
                employee_code="EMP-2026-001",
                full_name="Juan Dela Cruz",
                email="juan.delacruz@acme.com",
                department_id="manufacturing",
                cost_center_id="CC-001",
                employee_group="field_staff",
                status="active",
            ),
            FinanceUser(
                id="emp_field_02",
                employee_code="EMP-2026-002",
                full_name="Maria Clara Santos",
                email="maria.santos@acme.com",
                department_id="logistics",
                cost_center_id="CC-004",
                employee_group="field_staff",
                status="active",
            ),
            FinanceUser(
                id="sup_approver_01",
                employee_code="EMP-2026-010",
                full_name="Roberto Reyes",
                email="roberto.reyes@acme.com",
                department_id="manufacturing",
                cost_center_id="CC-001",
                employee_group="management",
                status="active",
            ),
            FinanceUser(
                id="fin_reviewer_01",
                employee_code="EMP-2026-020",
                full_name="Elena Gomez",
                email="elena.gomez@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="finance",
                status="active",
            ),
            FinanceUser(
                id="fin_maker_01",
                employee_code="EMP-2026-021",
                full_name="Carlos Mendoza",
                email="carlos.mendoza@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="finance",
                status="active",
            ),
            FinanceUser(
                id="fin_verifier_01",
                employee_code="EMP-2026-022",
                full_name="Diana Lim",
                email="diana.lim@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="finance",
                status="active",
            ),
            FinanceUser(
                id="bank_approver_01",
                employee_code="EMP-2026-023",
                full_name="Arthur Tan",
                email="arthur.tan@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="finance",
                status="active",
            ),
            FinanceUser(
                id="evidence_verifier_01",
                employee_code="EMP-2026-024",
                full_name="Patricia Ong",
                email="patricia.ong@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="finance",
                status="active",
            ),
            FinanceUser(
                id="fin_controller_01",
                employee_code="EMP-2026-030",
                full_name="Victoria Sy (CPA)",
                email="victoria.sy@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="finance",
                status="active",
            ),
            FinanceUser(
                id="auditor_01",
                employee_code="EMP-2026-040",
                full_name="Gabriel Ramos (CIA)",
                email="gabriel.ramos@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="finance",
                status="active",
            ),
            FinanceUser(
                id="sys_admin_01",
                employee_code="EMP-2026-099",
                full_name="System Administrator",
                email="sysadmin@acme.com",
                department_id="finance",
                cost_center_id="CC-FIN",
                employee_group="corporate",
                status="active",
            ),
        ]
        db.add_all(users)
        db.flush()

        roles = [
            RoleAssignment(user_id="emp_field_01", role="employee", department_scope="manufacturing"),
            RoleAssignment(user_id="emp_field_02", role="employee", department_scope="logistics"),
            RoleAssignment(user_id="sup_approver_01", role="supervisor", department_scope="*"),
            RoleAssignment(user_id="fin_reviewer_01", role="finance_reviewer", department_scope="*"),
            RoleAssignment(user_id="fin_maker_01", role="maker", department_scope="*"),
            RoleAssignment(user_id="fin_verifier_01", role="verifier", department_scope="*"),
            RoleAssignment(user_id="bank_approver_01", role="bank_approver", department_scope="*"),
            RoleAssignment(user_id="evidence_verifier_01", role="evidence_verifier", department_scope="*"),
            RoleAssignment(user_id="fin_controller_01", role="finance_controller", department_scope="*"),
            RoleAssignment(user_id="auditor_01", role="auditor", department_scope="*"),
            RoleAssignment(user_id="sys_admin_01", role="system_admin", department_scope="*"),
        ]
        db.add_all(roles)

        matrix = [
            # Cash advance business approval: Supervisor up to PHP 50,000; Controller up to PHP 500,000
            ApprovalMatrix(
                workflow="cash_advance",
                stage="business_approval",
                role="supervisor",
                department_scope="*",
                min_amount_minor=1,
                max_amount_minor=5000000,
            ),
            ApprovalMatrix(
                workflow="cash_advance",
                stage="business_approval",
                role="finance_controller",
                department_scope="*",
                min_amount_minor=1,
                max_amount_minor=50000000,
            ),
            # Disbursement bank approval: Bank Approver up to PHP 200,000
            ApprovalMatrix(
                workflow="disbursement_bank",
                stage="bank_approval",
                role="bank_approver",
                department_scope="*",
                min_amount_minor=1,
                max_amount_minor=20000000,
            ),
            # Purchase Request business approval: Supervisor up to PHP 100,000; Controller up to PHP 1,000,000
            ApprovalMatrix(
                workflow="purchase_request",
                stage="business_approval",
                role="supervisor",
                department_scope="*",
                min_amount_minor=1,
                max_amount_minor=10000000,
            ),
            ApprovalMatrix(
                workflow="purchase_request",
                stage="business_approval",
                role="finance_controller",
                department_scope="*",
                min_amount_minor=1,
                max_amount_minor=100000000,
            ),
            # Payable payment bank approval
            ApprovalMatrix(
                workflow="payable_payment",
                stage="bank_approval",
                role="bank_approver",
                department_scope="*",
                min_amount_minor=1,
                max_amount_minor=100000000,
            ),
            # Exception approval: Finance Controller up to PHP 100,000
            ApprovalMatrix(
                workflow="exception_request",
                stage="controller_approval",
                role="finance_controller",
                department_scope="*",
                min_amount_minor=0,
                max_amount_minor=10000000,
            ),
            # Policy approval: Finance Controller
            ApprovalMatrix(
                workflow="policy_approval",
                stage="controller_approval",
                role="finance_controller",
                department_scope="*",
                min_amount_minor=0,
                max_amount_minor=99999999999,
            ),
        ]
        db.add_all(matrix)

    if db.query(HolidayCalendar).count() == 0:
        holidays = [
            HolidayCalendar(holiday_date="2026-01-01", name="New Year's Day", country_code="PH"),
            HolidayCalendar(holiday_date="2026-04-09", name="Araw ng Kagitingan", country_code="PH"),
            HolidayCalendar(holiday_date="2026-05-01", name="Labor Day", country_code="PH"),
            HolidayCalendar(holiday_date="2026-06-12", name="Independence Day", country_code="PH"),
            HolidayCalendar(holiday_date="2026-08-31", name="National Heroes Day", country_code="PH"),
            HolidayCalendar(holiday_date="2026-11-30", name="Bonifacio Day", country_code="PH"),
            HolidayCalendar(holiday_date="2026-12-25", name="Christmas Day", country_code="PH"),
            HolidayCalendar(holiday_date="2026-12-30", name="Rizal Day", country_code="PH"),
        ]
        db.add_all(holidays)

    if db.query(FinancialPolicy).count() == 0:
        policy_id = str(uuid.uuid4())
        version_id = str(uuid.uuid4())
        eligible_categories = [
            "transportation",
            "fuel",
            "accommodation",
            "meals",
            "school_visit_materials",
            "communication",
            "representation",
            "other",
        ]
        policy = FinancialPolicy(
            id=policy_id,
            policy_code="FIELD_LIQUIDATION_POLICY",
            name="Field-Staff Monthly Expense Liquidation Policy",
            description="Supplied baseline company policy for field-staff monthly expense reporting and cash advance liquidation (subject to Finance ratification C1-C5).",
            scope_workflow="expense_liquidation",
            applicable_employee_groups=json.dumps(["field_staff", "management", "corporate", "finance"]),
            applicable_departments=json.dumps(["*"]),
            current_version_id=version_id,
        )
        version = PolicyVersion(
            id=version_id,
            policy_id=policy_id,
            version_number=1,
            status="active",
            effective_from="2025-01-01",
            effective_until=None,
            currency="PHP",
            aggregate_monthly_limit_minor=1500000,  # PHP 15,000.00
            max_reports_per_period=1,
            period_type="monthly",
            submission_deadline_day_of_next_month=2,
            deadline_interpretation="day_of_following_month_unconfirmed",
            finance_sla_days=5,
            sla_day_type="business_days_unconfirmed",
            require_consolidated_receipt_pdf=1,
            require_mfar_for_out_of_base=1,
            allow_partial_line_approval=1,
            block_new_advance_when_overdue=1,
            max_advance_amount_minor=None,
            eligible_categories_json=json.dumps(eligible_categories),
            previous_values_json=None,
            change_justification="Initial seeded baseline policy from project instructions (PHP 15,000 aggregate monthly report cap, 1 report/month, consolidated OR PDF + MFAR when out-of-base). Pending Finance ratification.",
            ratification_status="pending_finance_ratification",
            author_id="fin_reviewer_01",
            approver_id="fin_controller_01",
            submitted_at=utc_now(),
            approved_at=utc_now(),
        )
        rules = [
            PolicyRule(
                policy_version_id=version_id,
                rule_code="MONTHLY_AGGREGATE_CAP",
                description="Aggregate monthly claimed expenses per employee must not exceed configured limit (initial: PHP 15,000.00).",
                severity="block",
                enforcement_mode="exception_allowed",
                is_exception_allowed=1,
                required_exception_role="finance_controller",
                parameters_json=json.dumps({"aggregate_monthly_limit_minor": 1500000, "currency": "PHP"}),
            ),
            PolicyRule(
                policy_version_id=version_id,
                rule_code="MONTHLY_REPORT_FREQUENCY",
                description="Maximum number of non-cancelled/non-rejected expense reports per employee per reporting period (initial: 1).",
                severity="block",
                enforcement_mode="exception_allowed",
                is_exception_allowed=1,
                required_exception_role="finance_controller",
                parameters_json=json.dumps({"max_reports_per_period": 1, "period_type": "monthly"}),
            ),
            PolicyRule(
                policy_version_id=version_id,
                rule_code="CONSOLIDATED_RECEIPT_PDF",
                description="Consolidated PDF of Official Receipts must be attached before submission.",
                severity="block",
                enforcement_mode="exception_allowed",
                is_exception_allowed=1,
                required_exception_role="finance_controller",
                parameters_json=json.dumps({"required_doc_type": "consolidated_receipt_pdf"}),
            ),
            PolicyRule(
                policy_version_id=version_id,
                rule_code="OUT_OF_BASE_MFAR",
                description="Out-of-base activities require an attached MFAR document and destination/site on lines.",
                severity="block",
                enforcement_mode="exception_allowed",
                is_exception_allowed=1,
                required_exception_role="finance_controller",
                parameters_json=json.dumps({"required_doc_type": "mfar"}),
            ),
            PolicyRule(
                policy_version_id=version_id,
                rule_code="ELIGIBLE_CATEGORY",
                description="Expense lines must use an eligible expense category.",
                severity="block",
                enforcement_mode="hard_block",
                is_exception_allowed=0,
                required_exception_role="finance_controller",
                parameters_json=json.dumps({"eligible_categories": eligible_categories}),
            ),
            PolicyRule(
                policy_version_id=version_id,
                rule_code="DUPLICATE_RECEIPT_CHECK",
                description="Flag potential duplicate receipts (merchant + OR number + date + amount or SHA-256) for Finance review.",
                severity="flag",
                enforcement_mode="warn_only",
                is_exception_allowed=1,
                required_exception_role="finance_reviewer",
                parameters_json=json.dumps({"check_sha256": True, "check_fingerprint": True}),
            ),
        ]
        db.add(policy)
        db.flush()
        db.add(version)
        db.flush()
        db.add_all(rules)
        db.flush()

    if db.query(Vendor).count() == 0:
        # Dev-profile only, pending_verification, NO bank account data (per V3 & Rule 8)
        dev_vendors = [
            Vendor(
                vendor_code="VND-2026-0001",
                registered_name="Metro Manila Industrial Supplies Corp.",
                payee_type="supplier",
                tin="008-412-900-000",
                address="Makati City, Metro Manila",
                contact_person="Liza Soberano",
                contact_email="billing@mmindustrial.ph",
                payment_method="bank_transfer",
                bank_name=None,
                bank_account_masked=None,
                bank_account_ref=None,
                verification_status="pending_verification",
                created_by_id="fin_maker_01",
            ),
            Vendor(
                vendor_code="VND-2026-0002",
                registered_name="Meralco Enterprise Utilities",
                payee_type="utility",
                tin="000-101-528-000",
                address="Ortigas Center, Pasig City",
                contact_person="Corporate Accounts",
                contact_email="enterprise@meralco.com.ph",
                payment_method="bank_transfer",
                bank_name=None,
                bank_account_masked=None,
                bank_account_ref=None,
                verification_status="pending_verification",
                created_by_id="fin_maker_01",
            ),
            Vendor(
                vendor_code="VND-2026-0003",
                registered_name="Social Security System (SSS) Statutory Remittance",
                payee_type="statutory_agency",
                tin="000-728-944-000",
                address="East Avenue, Quezon City",
                contact_person="Statutory Clearing Desk",
                contact_email="remittance@sss.gov.ph",
                payment_method="bank_transfer",
                bank_name=None,
                bank_account_masked=None,
                bank_account_ref=None,
                verification_status="pending_verification",
                created_by_id="fin_maker_01",
            ),
        ]
        db.add_all(dev_vendors)

    db.commit()


import os
import base64
from typing import Any, Optional, List
from fastapi import APIRouter, Depends, Header, HTTPException, Request, Response, status
from fastapi.responses import FileResponse
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session

from database import get_db
from finance_models import (
    FinanceUser,
    RoleAssignment,
    ApprovalMatrix,
    DocumentReference,
    CashAdvance,
    Disbursement,
    ExpenseReport,
    ExpenseLine,
    Vendor,
    VendorBankChange,
    TaxCode,
    PurchaseRequest,
    AccountPayable,
    PaymentRecord,
    ExceptionRequest,
    AuditEvent,
)
from services.finance_common import (
    FinanceDomainError,
    ResolvedActor,
    resolve_actor,
    record_audit_event,
    verify_audit_chain,
    store_document_evidence,
    get_document_for_download,
)
from services.policy_engine import PolicyEngineService
from services.finance_workflows import FinanceWorkflowService
from services.finance_reporting import FinanceReportingService

router = APIRouter(prefix="/api/v1/finance", tags=["Finance Operations"])

policy_service = PolicyEngineService()
workflow_service = FinanceWorkflowService()
reporting_service = FinanceReportingService()


def get_current_actor(
    x_user_id: Optional[str] = Header(None, alias="X-User-Id"),
    x_user_role: Optional[str] = Header(None, alias="X-User-Role"),
    x_act_as_user_id: Optional[str] = Header(None, alias="X-Act-As-User-Id"),
    db: Session = Depends(get_db),
) -> ResolvedActor:
    try:
        return resolve_actor(
            db=db,
            x_user_id=x_user_id,
            x_user_role=x_user_role,
            x_act_as_user_id=x_act_as_user_id,
        )
    except FinanceDomainError as e:
        raise HTTPException(
            status_code=e.status_code,
            detail={"message": e.message, **e.details} if e.details else e.message,
        )


def _handle_domain_call(fn, *args, **kwargs):
    try:
        return fn(*args, **kwargs)
    except FinanceDomainError as e:
        raise HTTPException(
            status_code=e.status_code,
            detail={"message": e.message, **e.details} if e.details else e.message,
        )


# ==============================================================================
# Pydantic Request Schemas
# ==============================================================================

class ActAsRequest(BaseModel):
    target_user_id: str
    reason: Optional[str] = "Interactive dev testing role switch"


class CreatePolicyVersionRequest(BaseModel):
    effective_from: str
    effective_until: Optional[str] = None
    aggregate_monthly_limit_minor: int = Field(..., gt=0)
    max_reports_per_period: int = Field(1, gt=0)
    submission_deadline_day_of_next_month: Optional[int] = 2
    finance_sla_days: int = 5
    require_consolidated_receipt_pdf: bool = True
    require_mfar_for_out_of_base: bool = True
    allow_partial_line_approval: bool = True
    block_new_advance_when_overdue: bool = True
    max_advance_amount_minor: Optional[int] = None
    eligible_categories: List[str] = Field(
        default_factory=lambda: [
            "transportation",
            "fuel",
            "accommodation",
            "meals",
            "school_visit_materials",
            "communication",
            "representation",
            "other",
        ]
    )
    change_justification: str
    rule_overrides: Optional[List[ dict[str, Any] ]] = None


class ApproveOrRetirePolicyRequest(BaseModel):
    comments: str


class CreateExceptionRequestSchema(BaseModel):
    employee_id: Optional[str] = None
    target_entity_type: str = "expense_report"
    target_entity_id: Optional[str] = None
    reporting_period: str
    violated_rule_code: str
    exception_type: str = "amount_cap_override"
    requested_amount_minor: int = 0
    business_justification: str
    supporting_document_ids: Optional[List[str]] = None
    policy_version_id: Optional[str] = None
    submit_immediately: bool = True


class DecideExceptionRequestSchema(BaseModel):
    decision: str  # approved | rejected
    decision_reason: str
    approved_max_amount_minor: Optional[int] = None
    expires_at: Optional[str] = None


class UploadDocumentJsonRequest(BaseModel):
    original_filename: str
    document_type: str
    content_base64: str
    department_id: Optional[str] = None
    entity_type: Optional[str] = None
    entity_id: Optional[str] = None
    is_restricted: bool = False


class CreateCashAdvanceSchema(BaseModel):
    purpose_code: str
    business_justification: str
    requested_amount_minor: int = Field(..., gt=0)
    date_needed: str
    department_id: Optional[str] = None
    cost_center_id: Optional[str] = None
    destination_or_site: Optional[str] = None
    activity_start_date: Optional[str] = None
    activity_end_date: Optional[str] = None
    remarks: Optional[str] = None
    attachment_ids: Optional[List[str]] = None
    idempotency_key: Optional[str] = None
    submit_now: bool = False


class DecideCashAdvanceSchema(BaseModel):
    decision: str  # approved | rejected | returned_for_correction | cancelled
    comments: str
    approved_amount_minor: Optional[int] = None


class PrepareDisbursementSchema(BaseModel):
    payment_amount_minor: int = Field(..., gt=0)
    payment_method: str = "bank_transfer"
    recipient_account_ref: Optional[str] = None
    idempotency_key: Optional[str] = None


class ActionNotesSchema(BaseModel):
    notes: str = "Reviewed and verified"


class ExecuteDisbursementSchema(BaseModel):
    bank_reference: str
    executed_amount_minor: int = Field(..., gt=0)
    evidence_document_id: str


class DisbursementExceptionStateSchema(BaseModel):
    target_status: str  # failed | unknown | cancelled
    reason: str


class CashReturnSchema(BaseModel):
    returned_amount_minor: int = Field(..., gt=0)
    bank_reference: str
    evidence_document_id: str
    notes: Optional[str] = None


class ReimbursementSettlementSchema(BaseModel):
    settled_amount_minor: int = Field(..., gt=0)
    bank_reference: str
    evidence_document_id: str
    notes: Optional[str] = None


class CloseCashAdvanceSchema(BaseModel):
    closure_reason: str
    authorized_exception_id: Optional[str] = None


class CreateExpenseReportSchema(BaseModel):
    reporting_period: str
    business_purpose_summary: str
    out_of_base_activity: bool = False
    consolidated_receipt_doc_id: Optional[str] = None
    mfar_doc_id: Optional[str] = None
    employee_declaration_confirmed: bool = False
    remarks: Optional[str] = None
    idempotency_key: Optional[str] = None


class UpdateExpenseReportSchema(BaseModel):
    consolidated_receipt_doc_id: Optional[str] = None
    mfar_doc_id: Optional[str] = None
    employee_declaration_confirmed: Optional[bool] = None
    out_of_base_activity: Optional[bool] = None
    remarks: Optional[str] = None


class AddExpenseLineSchema(BaseModel):
    transaction_date: str
    expense_category: str
    business_purpose: str
    merchant_or_payee: str
    claimed_amount_minor: int = Field(..., gt=0)
    receipt_or_invoice_no: Optional[str] = None
    school_or_destination: Optional[str] = None
    out_of_base: bool = False
    receipt_document_id: Optional[str] = None
    mfar_document_id: Optional[str] = None


class SubmitExpenseReportSchema(BaseModel):
    employee_declaration_confirmed: Optional[bool] = True


class ReviewExpenseReportSchema(BaseModel):
    report_decision: str  # approved | partially_approved | returned_for_correction | rejected
    review_notes: str
    line_decisions: List[dict[str, Any]]


class CreateVendorSchema(BaseModel):
    registered_name: str
    payee_type: str = "supplier"
    tin: Optional[str] = None
    address: Optional[str] = None
    contact_person: Optional[str] = None
    contact_email: Optional[str] = None
    payment_method: str = "bank_transfer"
    bank_name: Optional[str] = None
    bank_account_ref: Optional[str] = None
    kyc_document_id: Optional[str] = None


class VerifyVendorSchema(BaseModel):
    decision: str = "verified"
    notes: Optional[str] = None


class RequestVendorBankChangeSchema(BaseModel):
    new_bank_name: str
    new_bank_account_ref: str
    reason: str
    supporting_document_id: Optional[str] = None


class DecideVendorBankChangeSchema(BaseModel):
    decision: str  # approved | rejected
    notes: Optional[str] = None


class TaxCodeSchema(BaseModel):
    code: str
    description: str
    tax_type: str  # VAT | EWT | NON_VAT | EXEMPT
    rate_bps: int = Field(..., ge=0)
    requires_bir_2307: bool = False
    approve_as_cpa: bool = False


class CreatePurchaseRequestSchema(BaseModel):
    vendor_id: str
    payment_category: str
    business_purpose: str
    requested_amount_minor: int = Field(..., gt=0)
    requested_payment_date: str
    department_id: Optional[str] = None
    cost_center_id: Optional[str] = None
    budget_reference: Optional[str] = None
    due_date: Optional[str] = None
    supporting_doc_ids: Optional[List[str]] = None
    invoice_doc_id: Optional[str] = None
    remarks: Optional[str] = None
    lines: Optional[List[dict[str, Any]]] = None
    idempotency_key: Optional[str] = None
    submit_now: bool = False


class DecidePurchaseRequestSchema(BaseModel):
    decision: str  # approved | rejected | returned_for_correction | cancelled
    comments: str
    approved_amount_minor: Optional[int] = None


class ValidatePayableSchema(BaseModel):
    purchase_request_id: str
    invoice_number: str
    invoice_date: str
    due_date: str
    original_payable_minor: int = Field(..., gt=0)
    credit_adjustment_minor: int = Field(0, ge=0)
    vat_code: Optional[str] = None
    ewt_code: Optional[str] = None
    payment_terms: Optional[str] = None
    accounting_reference: Optional[str] = None


class PreparePayablePaymentSchema(BaseModel):
    payment_amount_minor: int = Field(..., gt=0)
    payment_method: str = "bank_transfer"
    idempotency_key: Optional[str] = None


class ExecutePayablePaymentSchema(BaseModel):
    bank_reference: str
    executed_amount_minor: int = Field(..., gt=0)
    execution_date: str
    remittance_doc_id: str


class ReconcilePayablePaymentSchema(BaseModel):
    reconciliation_outcome: str = "matched"  # matched | discrepancy
    notes: Optional[str] = None


class ResolveExceptionRegisterSchema(BaseModel):
    target_status: str = "resolved"  # under_review | resolved | reopened
    resolution_notes: str
    evidence_doc_id: Optional[str] = None


# ==============================================================================
# 1. Identity, RBAC & Dev Act-As Routes
# ==============================================================================

@router.get("/auth/me", summary="Get Current Server-Bound Actor & Roles")
def get_me(actor: ResolvedActor = Depends(get_current_actor)):
    return actor.to_dict()


@router.get("/users", summary="List Finance Users & Server-Bound Role Assignments")
def list_finance_users(
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    users = db.query(FinanceUser).order_by(FinanceUser.employee_code.asc()).all()
    all_roles = db.query(RoleAssignment).filter(RoleAssignment.is_active == 1).all()
    roles_by_user: dict[str, list[str]] = {}
    for r in all_roles:
        roles_by_user.setdefault(r.user_id, []).append(r.role)
    return [u.to_dict(roles=roles_by_user.get(u.id, [])) for u in users]


@router.get("/approval-matrix", summary="List Configured Approval Matrix Rules")
def list_approval_matrix(
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    rows = db.query(ApprovalMatrix).filter(ApprovalMatrix.is_active == 1).all()
    return [r.to_dict() for r in rows]


@router.post("/auth/act-as", summary="Dev-Only Act-As Actor Switch (Audit-Logged)")
def dev_act_as(
    req: ActAsRequest,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    auth_mode = os.getenv("FINANCE_AUTH_MODE", "dev").lower()
    if auth_mode not in ("dev", "test"):
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="act_as_blocked_in_prod",
            entity_type="auth",
            entity_id=req.target_user_id,
            reason="Attempted to call /auth/act-as in production mode",
        )
        db.commit()
        raise HTTPException(status_code=403, detail="Role switching is disabled in production mode.")

    target = _handle_domain_call(
        resolve_actor,
        db=db,
        x_user_id=actor.id,
        x_act_as_user_id=req.target_user_id,
    )
    db.commit()
    return target.to_dict()


# ==============================================================================
# 2. Configurable Financial Policy & Exception Management Routes
# ==============================================================================

@router.get("/policies", summary="List Financial Policies, Versions & Rules")
def list_policies(
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(policy_service.list_policies, db)


@router.post("/policies/{policy_code}/versions", status_code=status.HTTP_201_CREATED, summary="Create Draft Policy Version")
def create_policy_version(
    policy_code: str,
    req: CreatePolicyVersionRequest,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        policy_service.create_policy_version,
        db=db,
        actor=actor,
        policy_code=policy_code,
        effective_from=req.effective_from,
        effective_until=req.effective_until,
        aggregate_monthly_limit_minor=req.aggregate_monthly_limit_minor,
        max_reports_per_period=req.max_reports_per_period,
        submission_deadline_day_of_next_month=req.submission_deadline_day_of_next_month,
        finance_sla_days=req.finance_sla_days,
        require_consolidated_receipt_pdf=req.require_consolidated_receipt_pdf,
        require_mfar_for_out_of_base=req.require_mfar_for_out_of_base,
        allow_partial_line_approval=req.allow_partial_line_approval,
        block_new_advance_when_overdue=req.block_new_advance_when_overdue,
        max_advance_amount_minor=req.max_advance_amount_minor,
        eligible_categories=req.eligible_categories,
        change_justification=req.change_justification,
        rule_overrides=req.rule_overrides,
    )


@router.post("/policies/versions/{version_id}/submit", summary="Submit Policy Version for Approval")
def submit_policy_version(
    version_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(policy_service.submit_policy_version, db, actor, version_id)


@router.post("/policies/versions/{version_id}/approve", summary="Approve & Activate Policy Version (Author != Approver)")
def approve_policy_version(
    version_id: str,
    req: ApproveOrRetirePolicyRequest,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(policy_service.approve_policy_version, db, actor, version_id, req.comments)


@router.post("/policies/versions/{version_id}/retire", summary="Retire Active Policy Version")
def retire_policy_version(
    version_id: str,
    req: ApproveOrRetirePolicyRequest,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(policy_service.retire_policy_version, db, actor, version_id, req.comments)


@router.get("/exception-requests", summary="List Policy Exception Requests")
def list_exception_requests(
    status: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    q = db.query(ExceptionRequest)
    if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "auditor"):
        q = q.filter(ExceptionRequest.employee_id == actor.id)
    if status and status != "all":
        q = q.filter(ExceptionRequest.status == status)
    items = q.order_by(ExceptionRequest.created_at.desc()).all()
    return [i.to_dict() for i in items]


@router.post("/exception-requests", status_code=status.HTTP_201_CREATED, summary="Create Scoped Policy Exception Request")
def create_exception_request(
    req: CreateExceptionRequestSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        policy_service.create_exception_request,
        db=db,
        actor=actor,
        employee_id=req.employee_id or actor.id,
        target_entity_type=req.target_entity_type,
        target_entity_id=req.target_entity_id,
        reporting_period=req.reporting_period,
        violated_rule_code=req.violated_rule_code,
        exception_type=req.exception_type,
        requested_amount_minor=req.requested_amount_minor,
        business_justification=req.business_justification,
        supporting_document_ids=req.supporting_document_ids,
        policy_version_id=req.policy_version_id,
        submit_immediately=req.submit_immediately,
    )


@router.post("/exception-requests/{exception_id}/decide", summary="Approve or Reject Policy Exception Request")
def decide_exception_request(
    exception_id: str,
    req: DecideExceptionRequestSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        policy_service.decide_exception_request,
        db=db,
        actor=actor,
        exception_id=exception_id,
        decision=req.decision,
        decision_reason=req.decision_reason,
        approved_max_amount_minor=req.approved_max_amount_minor,
        expires_at_iso=req.expires_at,
    )


# ==============================================================================
# 3. Document Evidence Upload & Authorized Download Routes
# ==============================================================================

@router.post("/documents", status_code=status.HTTP_201_CREATED, summary="Upload Secure Document Evidence")
def upload_document(
    req: UploadDocumentJsonRequest,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    try:
        raw_bytes = base64.b64decode(req.content_base64)
    except Exception:
        raise HTTPException(status_code=422, detail="Invalid base64 document payload.")
    doc = _handle_domain_call(
        store_document_evidence,
        db=db,
        actor=actor,
        file_bytes=raw_bytes,
        original_filename=req.original_filename,
        document_type=req.document_type,
        department_id=req.department_id,
        entity_type=req.entity_type,
        entity_id=req.entity_id,
        is_restricted=req.is_restricted,
    )
    return doc.to_dict()


@router.get("/documents", summary="List Authorized Document References")
def list_documents(
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    q = db.query(DocumentReference)
    if not actor.has_role("finance_reviewer", "finance_controller", "bank_approver", "auditor"):
        q = q.filter(DocumentReference.is_restricted == 0)
    if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "maker", "verifier", "bank_approver", "evidence_verifier", "auditor"):
        q = q.filter(DocumentReference.uploader_id == actor.id)
    docs = q.order_by(DocumentReference.created_at.desc()).all()
    return [d.to_dict() for d in docs]


@router.get("/documents/{doc_id}/download", summary="Authorized Document Evidence Download")
def download_document(
    doc_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    doc, file_path = _handle_domain_call(get_document_for_download, db, actor, doc_id)
    return FileResponse(
        path=str(file_path),
        media_type=doc.mime_type,
        filename=doc.original_filename,
    )


# ==============================================================================
# 4. Cash Advance & Bank Disbursement Routes
# ==============================================================================

@router.get("/cash-advances", summary="List Cash Advances with Derived Reconciliation")
def list_cash_advances(
    department_id: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    advances = reporting_service._scope_advances_query(db, actor, department_id=department_id)
    return [workflow_service.get_cash_advance_detail(db, actor, a.id) for a in advances]


@router.post("/cash-advances", status_code=status.HTTP_201_CREATED, summary="Create Cash Advance Request")
def create_cash_advance(
    req: CreateCashAdvanceSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.create_cash_advance,
        db=db,
        actor=actor,
        purpose_code=req.purpose_code,
        business_justification=req.business_justification,
        requested_amount_minor=req.requested_amount_minor,
        date_needed=req.date_needed,
        department_id=req.department_id,
        cost_center_id=req.cost_center_id,
        destination_or_site=req.destination_or_site,
        activity_start_date=req.activity_start_date,
        activity_end_date=req.activity_end_date,
        remarks=req.remarks,
        attachment_ids=req.attachment_ids,
        idempotency_key=req.idempotency_key,
        submit_now=req.submit_now,
    )


@router.get("/cash-advances/{advance_id}", summary="Get Cash Advance Detail & Reconciliation")
def get_cash_advance(
    advance_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.get_cash_advance_detail, db, actor, advance_id)


@router.post("/cash-advances/{advance_id}/submit", summary="Submit Cash Advance Request")
def submit_cash_advance(
    advance_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.submit_cash_advance, db, actor, advance_id)


@router.post("/cash-advances/{advance_id}/decide", summary="Approve, Reject, Return, or Cancel Cash Advance")
def decide_cash_advance(
    advance_id: str,
    req: DecideCashAdvanceSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.decide_cash_advance,
        db=db,
        actor=actor,
        advance_id=advance_id,
        decision=req.decision,
        comments=req.comments,
        approved_amount_minor=req.approved_amount_minor,
    )


@router.post("/cash-advances/{advance_id}/disbursements", status_code=status.HTTP_201_CREATED, summary="Maker: Prepare Bank Disbursement")
def prepare_disbursement(
    advance_id: str,
    req: PrepareDisbursementSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.prepare_disbursement,
        db=db,
        actor=actor,
        advance_id=advance_id,
        payment_amount_minor=req.payment_amount_minor,
        payment_method=req.payment_method,
        recipient_account_ref=req.recipient_account_ref,
        idempotency_key=req.idempotency_key,
    )


@router.post("/disbursements/{disbursement_id}/verify", summary="Verifier: Independently Verify Disbursement")
def verify_disbursement(
    disbursement_id: str,
    req: ActionNotesSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.verify_disbursement, db, actor, disbursement_id, req.notes)


@router.post("/disbursements/{disbursement_id}/bank-approve", summary="Bank Approver: Authorize Disbursement")
def approve_disbursement_bank(
    disbursement_id: str,
    req: ActionNotesSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.approve_disbursement_bank, db, actor, disbursement_id, req.notes)


@router.post("/disbursements/{disbursement_id}/execute", summary="Record Manual Bank Execution Reference & Remittance Doc")
def execute_disbursement(
    disbursement_id: str,
    req: ExecuteDisbursementSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.record_disbursement_execution,
        db=db,
        actor=actor,
        disbursement_id=disbursement_id,
        bank_reference=req.bank_reference,
        executed_amount_minor=req.executed_amount_minor,
        evidence_document_id=req.evidence_document_id,
    )


@router.post("/disbursements/{disbursement_id}/verify-evidence", summary="Evidence Verifier: Confirm Bank Evidence & Release Advance")
def verify_disbursement_evidence(
    disbursement_id: str,
    req: ActionNotesSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.verify_disbursement_evidence, db, actor, disbursement_id, req.notes)


@router.post("/disbursements/{disbursement_id}/exception-state", summary="Mark Disbursement Failed, Unknown, or Cancelled")
def mark_disbursement_exception(
    disbursement_id: str,
    req: DisbursementExceptionStateSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.mark_disbursement_exception_state,
        db=db,
        actor=actor,
        disbursement_id=disbursement_id,
        target_status=req.target_status,
        reason=req.reason,
    )


@router.post("/cash-advances/{advance_id}/cash-return", summary="Record Evidenced Employee Cash Return")
def record_cash_return(
    advance_id: str,
    req: CashReturnSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.record_cash_return,
        db=db,
        actor=actor,
        advance_id=advance_id,
        returned_amount_minor=req.returned_amount_minor,
        bank_reference=req.bank_reference,
        evidence_document_id=req.evidence_document_id,
        notes=req.notes,
    )


@router.post("/cash-advances/{advance_id}/reimbursement-settle", summary="Record Evidenced Employee Excess Reimbursement Payment")
def record_reimbursement_settlement(
    advance_id: str,
    req: ReimbursementSettlementSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.record_reimbursement_settlement,
        db=db,
        actor=actor,
        advance_id=advance_id,
        settled_amount_minor=req.settled_amount_minor,
        bank_reference=req.bank_reference,
        evidence_document_id=req.evidence_document_id,
        notes=req.notes,
    )


@router.post("/cash-advances/{advance_id}/close", summary="Finance Controller: Authorize Final Closure & Freeze Snapshot")
def close_cash_advance(
    advance_id: str,
    req: CloseCashAdvanceSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.close_cash_advance,
        db=db,
        actor=actor,
        advance_id=advance_id,
        closure_reason=req.closure_reason,
        authorized_exception_id=req.authorized_exception_id,
    )


# ==============================================================================
# 5. Monthly Expense Liquidation Routes
# ==============================================================================

@router.get("/expense-reports", summary="List Monthly Expense Reports")
def list_expense_reports(
    department_id: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    reports = reporting_service._scope_reports_query(db, actor, department_id=department_id)
    return [workflow_service.get_expense_report_detail(db, actor, r.id) for r in reports]


@router.post("/expense-reports", status_code=status.HTTP_201_CREATED, summary="Create Monthly Expense Report")
def create_expense_report(
    req: CreateExpenseReportSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.create_expense_report,
        db=db,
        actor=actor,
        reporting_period=req.reporting_period,
        business_purpose_summary=req.business_purpose_summary,
        out_of_base_activity=req.out_of_base_activity,
        consolidated_receipt_doc_id=req.consolidated_receipt_doc_id,
        mfar_doc_id=req.mfar_doc_id,
        employee_declaration_confirmed=req.employee_declaration_confirmed,
        remarks=req.remarks,
        idempotency_key=req.idempotency_key,
    )


@router.get("/expense-reports/{report_id}", summary="Get Expense Report Detail")
def get_expense_report(
    report_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.get_expense_report_detail, db, actor, report_id)


@router.patch("/expense-reports/{report_id}", summary="Update Draft/Returned Expense Report Metadata & Evidence")
def update_expense_report(
    report_id: str,
    req: UpdateExpenseReportSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.update_expense_report_metadata,
        db=db,
        actor=actor,
        report_id=report_id,
        consolidated_receipt_doc_id=req.consolidated_receipt_doc_id,
        mfar_doc_id=req.mfar_doc_id,
        employee_declaration_confirmed=req.employee_declaration_confirmed,
        out_of_base_activity=req.out_of_base_activity,
        remarks=req.remarks,
    )


@router.post("/expense-reports/{report_id}/lines", status_code=status.HTTP_201_CREATED, summary="Add Expense Line to Report")
def add_expense_line(
    report_id: str,
    req: AddExpenseLineSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.add_expense_line,
        db=db,
        actor=actor,
        report_id=report_id,
        transaction_date=req.transaction_date,
        expense_category=req.expense_category,
        business_purpose=req.business_purpose,
        merchant_or_payee=req.merchant_or_payee,
        claimed_amount_minor=req.claimed_amount_minor,
        receipt_or_invoice_no=req.receipt_or_invoice_no,
        school_or_destination=req.school_or_destination,
        out_of_base=req.out_of_base,
        receipt_document_id=req.receipt_document_id,
        mfar_document_id=req.mfar_document_id,
    )


@router.post("/expense-reports/{report_id}/evaluate", summary="Dry-Run Policy Evaluation Preview")
def evaluate_expense_report_preview(
    report_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    rep = db.query(ExpenseReport).filter(ExpenseReport.id == report_id).first()
    if not rep:
        raise HTTPException(status_code=404, detail="Expense report not found.")
    lines = db.query(ExpenseLine).filter(ExpenseLine.expense_report_id == rep.id).all()
    return _handle_domain_call(
        policy_service.evaluate_expense_report,
        db=db,
        report=rep,
        lines=lines,
        consume_slot_and_exceptions=False,
    )


@router.post("/expense-reports/{report_id}/submit", summary="Validate Policy & Submit Monthly Expense Report")
def submit_expense_report(
    report_id: str,
    req: SubmitExpenseReportSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.submit_expense_report,
        db=db,
        actor=actor,
        report_id=report_id,
        employee_declaration_confirmed=req.employee_declaration_confirmed,
    )


@router.post("/expense-reports/{report_id}/review", summary="Finance Reviewer: Review Expense Lines & Apply Oldest-First Settlement")
def review_expense_report(
    report_id: str,
    req: ReviewExpenseReportSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.review_expense_report,
        db=db,
        actor=actor,
        report_id=report_id,
        line_decisions=req.line_decisions,
        report_decision=req.report_decision,
        review_notes=req.review_notes,
    )


# ==============================================================================
# 6. Purchase-to-Pay Routes (Vendors, Tax Codes, PRs, Payables, Payments)
# ==============================================================================

@router.get("/vendors", summary="List Vendor / Payee Master Registry")
def list_vendors(
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    vendors = db.query(Vendor).order_by(Vendor.vendor_code.asc()).all()
    include_protected = actor.has_role("maker", "verifier", "bank_approver", "finance_reviewer", "finance_controller", "auditor")
    return [v.to_dict(include_protected_bank=include_protected) for v in vendors]


@router.post("/vendors", status_code=status.HTTP_201_CREATED, summary="Create Vendor Master Record (Pending Verification)")
def create_vendor(
    req: CreateVendorSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.create_vendor,
        db=db,
        actor=actor,
        registered_name=req.registered_name,
        payee_type=req.payee_type,
        tin=req.tin,
        address=req.address,
        contact_person=req.contact_person,
        contact_email=req.contact_email,
        payment_method=req.payment_method,
        bank_name=req.bank_name,
        bank_account_ref=req.bank_account_ref,
        kyc_document_id=req.kyc_document_id,
    )


@router.post("/vendors/{vendor_id}/verify", summary="Verify Vendor Master Record (Creator != Verifier)")
def verify_vendor(
    vendor_id: str,
    req: VerifyVendorSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.verify_vendor, db, actor, vendor_id, req.decision, req.notes)


@router.post("/vendors/{vendor_id}/bank-changes", status_code=status.HTTP_201_CREATED, summary="Request Vendor Bank Account Change")
def request_vendor_bank_change(
    vendor_id: str,
    req: RequestVendorBankChangeSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.request_vendor_bank_change,
        db=db,
        actor=actor,
        vendor_id=vendor_id,
        new_bank_name=req.new_bank_name,
        new_bank_account_ref=req.new_bank_account_ref,
        reason=req.reason,
        supporting_document_id=req.supporting_document_id,
    )


@router.post("/vendor-bank-changes/{change_id}/decide", summary="Approve or Reject Vendor Bank Account Change")
def decide_vendor_bank_change(
    change_id: str,
    req: DecideVendorBankChangeSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.decide_vendor_bank_change, db, actor, change_id, req.decision, req.notes)


@router.get("/tax-codes", summary="List Configured Tax & Withholding Codes")
def list_tax_codes(
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    rows = db.query(TaxCode).order_by(TaxCode.code.asc()).all()
    return [r.to_dict() for r in rows]


@router.post("/tax-codes", status_code=status.HTTP_201_CREATED, summary="Configure or CPA-Approve Tax Code")
def create_tax_code(
    req: TaxCodeSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.create_or_approve_tax_code,
        db=db,
        actor=actor,
        code=req.code,
        description=req.description,
        tax_type=req.tax_type,
        rate_bps=req.rate_bps,
        requires_bir_2307=req.requires_bir_2307,
        approve_as_cpa=req.approve_as_cpa,
    )


@router.get("/purchase-requests", summary="List Purchase Requests")
def list_purchase_requests(
    department_id: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    prs = reporting_service._scope_prs_query(db, actor, department_id=department_id)
    return [workflow_service.get_purchase_request_detail(db, actor, p.id) for p in prs]


@router.post("/purchase-requests", status_code=status.HTTP_201_CREATED, summary="Create Purchase Request")
def create_purchase_request(
    req: CreatePurchaseRequestSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.create_purchase_request,
        db=db,
        actor=actor,
        vendor_id=req.vendor_id,
        payment_category=req.payment_category,
        business_purpose=req.business_purpose,
        requested_amount_minor=req.requested_amount_minor,
        requested_payment_date=req.requested_payment_date,
        department_id=req.department_id,
        cost_center_id=req.cost_center_id,
        budget_reference=req.budget_reference,
        due_date=req.due_date,
        supporting_doc_ids=req.supporting_doc_ids,
        invoice_doc_id=req.invoice_doc_id,
        remarks=req.remarks,
        lines=req.lines,
        idempotency_key=req.idempotency_key,
        submit_now=req.submit_now,
    )


@router.get("/purchase-requests/{pr_id}", summary="Get Purchase Request Detail")
def get_purchase_request(
    pr_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.get_purchase_request_detail, db, actor, pr_id)


@router.post("/purchase-requests/{pr_id}/submit", summary="Submit Purchase Request")
def submit_purchase_request(
    pr_id: str,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.submit_purchase_request, db, actor, pr_id)


@router.post("/purchase-requests/{pr_id}/decide", summary="Approve, Reject, Return, or Cancel Purchase Request")
def decide_purchase_request(
    pr_id: str,
    req: DecidePurchaseRequestSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.decide_purchase_request,
        db=db,
        actor=actor,
        pr_id=pr_id,
        decision=req.decision,
        comments=req.comments,
        approved_amount_minor=req.approved_amount_minor,
    )


@router.get("/payables", summary="List Accounts Payable")
def list_payables(
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    prs = reporting_service._scope_prs_query(db, actor)
    pr_ids = [p.id for p in prs]
    if not pr_ids:
        return []
    aps = (
        db.query(AccountPayable)
        .filter(AccountPayable.purchase_request_id.in_(pr_ids))
        .order_by(AccountPayable.created_at.desc())
        .all()
    )
    return [workflow_service.get_payable_detail(db, actor, ap.id) for ap in aps]


@router.post("/payables", status_code=status.HTTP_201_CREATED, summary="Validate Vendor Invoice & Create Account Payable")
def validate_payable(
    req: ValidatePayableSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.validate_account_payable,
        db=db,
        actor=actor,
        purchase_request_id=req.purchase_request_id,
        invoice_number=req.invoice_number,
        invoice_date=req.invoice_date,
        due_date=req.due_date,
        original_payable_minor=req.original_payable_minor,
        credit_adjustment_minor=req.credit_adjustment_minor,
        vat_code=req.vat_code,
        ewt_code=req.ewt_code,
        payment_terms=req.payment_terms,
        accounting_reference=req.accounting_reference,
    )


@router.post("/payables/{payable_id}/payments", status_code=status.HTTP_201_CREATED, summary="Maker: Prepare Payable Payment Record")
def prepare_payable_payment(
    payable_id: str,
    req: PreparePayablePaymentSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.prepare_payable_payment,
        db=db,
        actor=actor,
        payable_id=payable_id,
        payment_amount_minor=req.payment_amount_minor,
        payment_method=req.payment_method,
        idempotency_key=req.idempotency_key,
    )


@router.post("/payment-records/{payment_id}/verify", summary="Verifier: Independently Verify Payable Payment")
def verify_payable_payment(
    payment_id: str,
    req: ActionNotesSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.verify_payable_payment, db, actor, payment_id, req.notes)


@router.post("/payment-records/{payment_id}/bank-approve", summary="Bank Approver: Authorize Payable Payment")
def approve_payable_payment(
    payment_id: str,
    req: ActionNotesSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(workflow_service.approve_payable_payment_bank, db, actor, payment_id, req.notes)


@router.post("/payment-records/{payment_id}/execute", summary="Record Manual Bank Payment Reference & Remittance Advice")
def execute_payable_payment(
    payment_id: str,
    req: ExecutePayablePaymentSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.record_payable_payment_execution,
        db=db,
        actor=actor,
        payment_id=payment_id,
        bank_reference=req.bank_reference,
        executed_amount_minor=req.executed_amount_minor,
        execution_date=req.execution_date,
        remittance_doc_id=req.remittance_doc_id,
    )


@router.post("/payment-records/{payment_id}/reconcile", summary="Evidence Verifier: Verify Remittance Evidence & Reconcile Payable")
def reconcile_payable_payment(
    payment_id: str,
    req: ReconcilePayablePaymentSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.verify_and_reconcile_payable_payment,
        db=db,
        actor=actor,
        payment_id=payment_id,
        reconciliation_outcome=req.reconciliation_outcome,
        notes=req.notes,
    )


@router.post("/payment-records/{payment_id}/exception-state", summary="Mark Payment Failed, Payment_Unknown, or Cancelled")
def mark_payable_payment_exception(
    payment_id: str,
    req: DisbursementExceptionStateSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        workflow_service.mark_payable_payment_exception,
        db=db,
        actor=actor,
        payment_id=payment_id,
        target_status=req.target_status,
        reason=req.reason,
    )


# ==============================================================================
# 7. Stage 5 Dashboard, KPIs, Tracker, Exception Register, Audit & Exports
# ==============================================================================

@router.get("/dashboard/summary", summary="Database-Backed Finance Operations KPI Summary")
def get_finance_dashboard_summary(
    start_date: Optional[str] = None,
    end_date: Optional[str] = None,
    as_of_date: Optional[str] = None,
    department_id: Optional[str] = None,
    cost_center_id: Optional[str] = None,
    employee_id: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        reporting_service.get_dashboard_summary,
        db=db,
        actor=actor,
        start_date=start_date,
        end_date=end_date,
        as_of_date=as_of_date,
        department_id=department_id,
        cost_center_id=cost_center_id,
        employee_id=employee_id,
    )


@router.get("/kpi-definitions", summary="Explicit KPI Numerator, Denominator & Cutoff Definitions")
def get_kpi_definitions(actor: ResolvedActor = Depends(get_current_actor)):
    return reporting_service.get_kpi_definitions()


@router.get("/business-confirmations", summary="Register of Open Business & CPA Policy Confirmations (C1-C11)")
def get_business_confirmations(actor: ResolvedActor = Depends(get_current_actor)):
    return reporting_service.get_business_confirmations()


@router.get("/tracker", summary="Centralized Finance Record Tracker (Reconciled Read Model)")
def get_centralized_tracker(
    workflow: Optional[str] = None,
    department_id: Optional[str] = None,
    status: Optional[str] = None,
    search: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        reporting_service.get_centralized_tracker,
        db=db,
        actor=actor,
        workflow=workflow,
        department_id=department_id,
        status=status,
        search=search,
    )


@router.get("/tracker/export.csv", summary="Export Permission-Scoped, Formula-Safe UTF-8 BOM CSV")
def export_tracker_csv(
    workflow: Optional[str] = None,
    department_id: Optional[str] = None,
    status: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    csv_text = _handle_domain_call(
        reporting_service.export_tracker_csv,
        db=db,
        actor=actor,
        workflow=workflow,
        department_id=department_id,
        status=status,
    )
    return Response(
        content=csv_text.encode("utf-8"),
        media_type="text/csv; charset=utf-8",
        headers={"Content-Disposition": 'attachment; filename="finance_record_tracker.csv"'},
    )


@router.get("/exception-register", summary="List Operational Control Exception Register")
def list_exception_register(
    status: Optional[str] = None,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(reporting_service.list_exception_register, db, actor, status)


@router.post("/exception-register/{register_id}/resolve", summary="Update or Resolve Exception Register Item")
def resolve_exception_register(
    register_id: str,
    req: ResolveExceptionRegisterSchema,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    return _handle_domain_call(
        reporting_service.resolve_exception_register_item,
        db=db,
        actor=actor,
        register_id=register_id,
        target_status=req.target_status,
        resolution_notes=req.resolution_notes,
        evidence_doc_id=req.evidence_doc_id,
    )


@router.get("/audit-events", summary="List Append-Only Hash-Chained Financial Audit Events")
def list_audit_events(
    entity_type: Optional[str] = None,
    entity_id: Optional[str] = None,
    limit: int = 100,
    actor: ResolvedActor = Depends(get_current_actor),
    db: Session = Depends(get_db),
):
    q = db.query(AuditEvent)
    if not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "auditor", "bank_approver"):
        q = q.filter(AuditEvent.actor_id == actor.id)
    if entity_type and entity_type != "all":
        q = q.filter(AuditEvent.entity_type == entity_type)
    if entity_id:
        q = q.filter(AuditEvent.entity_id == entity_id)
    events = q.order_by(AuditEvent.sequence_no.desc()).limit(min(500, max(1, limit))).all()
    chain_integrity = verify_audit_chain(db)
    return {
        "chain_integrity": chain_integrity,
        "events": [e.to_dict() for e in events],
    }


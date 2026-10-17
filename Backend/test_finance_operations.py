import os
import base64
from datetime import datetime, timedelta, timezone
import pytest
from fastapi.testclient import TestClient

from app import app
from database import Base, engine, SessionLocal
from models import (
    init_db,
    AuditEvent,
    AdvanceLedgerEntry,
    ApprovalHistory,
    ClosingSnapshot,
    CashAdvance,
    Disbursement,
    ExpenseReport,
    ExpenseLine,
    SettlementApplication,
    PeriodCounter,
    ExceptionRequest,
    ExceptionRegister,
    Vendor,
    VendorBankChange,
    TaxCode,
    PurchaseRequest,
    PurchaseRequestLine,
    AccountPayable,
    PaymentRecord,
    DocumentReference,
    FinancialPolicy,
    PolicyVersion,
    PolicyRule,
    seed_finance_db,
)

client = TestClient(app)


def _make_pdf_b64(tag: str = "test") -> str:
    raw = f"%PDF-1.4\n% Fake PDF Evidence ({tag})\n1 0 obj <<>> endobj\n%%EOF\n".encode("utf-8")
    return base64.b64encode(raw).decode("ascii")


def _upload_pdf(user_id: str, doc_type: str = "consolidated_receipt_pdf", tag: str = "doc", restricted: bool = False) -> str:
    resp = client.post(
        "/api/v1/finance/documents",
        headers={"X-User-Id": user_id},
        json={
            "original_filename": f"{tag}.pdf",
            "document_type": doc_type,
            "content_base64": _make_pdf_b64(tag),
            "is_restricted": restricted,
        },
    )
    assert resp.status_code == 201, resp.text
    return resp.json()["id"]


@pytest.fixture(autouse=True)
def reset_finance_tables(monkeypatch):
    monkeypatch.setenv("FINANCE_AUTH_MODE", "dev")
    monkeypatch.setenv("FINANCE_SEED", "dev")
    init_db()
    db = SessionLocal()
    try:
        # Clear transactional finance tables in dependency order while keeping seeded users/roles/matrix/policies
        db.query(ClosingSnapshot).delete()
        db.query(SettlementApplication).delete()
        db.query(AdvanceLedgerEntry).delete()
        db.query(Disbursement).delete()
        db.query(ExpenseLine).delete()
        db.query(ExpenseReport).delete()
        db.query(CashAdvance).delete()
        db.query(PaymentRecord).delete()
        db.query(AccountPayable).delete()
        db.query(PurchaseRequestLine).delete()
        db.query(PurchaseRequest).delete()
        db.query(VendorBankChange).delete()
        db.query(Vendor).delete()
        db.query(TaxCode).delete()
        db.query(PeriodCounter).delete()
        db.query(ExceptionRequest).delete()
        db.query(ExceptionRegister).delete()
        db.query(ApprovalHistory).delete()
        db.query(PolicyRule).delete()
        db.query(PolicyVersion).delete()
        db.query(FinancialPolicy).delete()
        db.query(DocumentReference).delete()
        db.query(AuditEvent).delete()
        db.commit()
        seed_finance_db(db)
    finally:
        db.close()
    yield


# ==============================================================================
# Stage 1 Tests: Identity, RBAC, SoD, Approval Matrix & Append-Only Audit Chain
# ==============================================================================

def test_stage1_rbac_spoofed_role_header_and_prod_act_as_blocked(monkeypatch):
    # 1. Ordinary employee resolves with server-bound 'employee' role
    me_resp = client.get("/api/v1/finance/auth/me", headers={"X-User-Id": "emp_field_01"})
    assert me_resp.status_code == 200
    assert me_resp.json()["roles"] == ["employee"]

    # 2. Client spoofing X-User-Role: finance_controller is blocked (403) and logged
    spoof_resp = client.get(
        "/api/v1/finance/auth/me",
        headers={"X-User-Id": "emp_field_01", "X-User-Role": "finance_controller"},
    )
    assert spoof_resp.status_code == 403
    assert "not trusted" in spoof_resp.json()["detail"].lower()

    # 3. Dev act-as works in dev mode and logs audit event
    act_dev = client.post(
        "/api/v1/finance/auth/act-as",
        headers={"X-User-Id": "emp_field_01"},
        json={"target_user_id": "fin_controller_01"},
    )
    assert act_dev.status_code == 200
    assert "finance_controller" in act_dev.json()["roles"]

    # 4. In production mode, X-Act-As-User-Id and /auth/act-as are blocked (403)
    monkeypatch.setenv("FINANCE_AUTH_MODE", "prod")
    forged_header_resp = client.get(
        "/api/v1/finance/auth/me",
        headers={"X-User-Id": "emp_field_01", "X-Act-As-User-Id": "fin_controller_01"},
    )
    assert forged_header_resp.status_code == 403

    forged_endpoint_resp = client.post(
        "/api/v1/finance/auth/act-as",
        headers={"X-User-Id": "emp_field_01"},
        json={"target_user_id": "fin_controller_01"},
    )
    assert forged_endpoint_resp.status_code == 403


def test_stage1_append_only_audit_and_sysadmin_no_financial_bypass():
    # System admin creates a cash advance and tries to approve another user's advance -> 403
    ca_resp = client.post(
        "/api/v1/finance/cash-advances",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "purpose_code": "school_visit",
            "business_justification": "Field school inspection in Laguna",
            "requested_amount_minor": 1000000,
            "date_needed": "2026-05-15",
            "submit_now": True,
        },
    )
    assert ca_resp.status_code == 201
    adv_id = ca_resp.json()["id"]

    # System administrator has no financial approval authority
    sys_approve = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/decide",
        headers={"X-User-Id": "sys_admin_01"},
        json={"decision": "approved", "comments": "Admin override attempt"},
    )
    assert sys_approve.status_code == 403

    # Verify audit hash chain is valid and ORM blocks mutation/deletion of AuditEvent
    audit_resp = client.get("/api/v1/finance/audit-events", headers={"X-User-Id": "auditor_01"})
    assert audit_resp.status_code == 200
    assert audit_resp.json()["chain_integrity"]["valid"] is True
    assert audit_resp.json()["chain_integrity"]["event_count"] >= 2

    db = SessionLocal()
    try:
        ev = db.query(AuditEvent).first()
        assert ev is not None
        ev.action = "tampered_action"
        with pytest.raises(PermissionError):
            db.commit()
        db.rollback()

        ev2 = db.query(AuditEvent).first()
        db.delete(ev2)
        with pytest.raises(PermissionError):
            db.commit()
        db.rollback()
    finally:
        db.close()


# ==============================================================================
# Stage 2 Tests: Policy Versioning, Cash Advances, 6-Person Disbursement SoD
# ==============================================================================

def test_stage2_policy_versioning_author_cannot_self_approve():
    # Reviewer creates a draft policy version v2
    create_v2 = client.post(
        "/api/v1/finance/policies/FIELD_LIQUIDATION_POLICY/versions",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={
            "effective_from": "2026-06-01",
            "aggregate_monthly_limit_minor": 2000000,
            "max_reports_per_period": 1,
            "submission_deadline_day_of_next_month": 5,
            "finance_sla_days": 5,
            "require_consolidated_receipt_pdf": True,
            "require_mfar_for_out_of_base": True,
            "allow_partial_line_approval": True,
            "block_new_advance_when_overdue": True,
            "change_justification": "Increase field monthly cap to PHP 20,000 for Q3",
        },
    )
    assert create_v2.status_code == 201
    v2_id = create_v2.json()["id"]
    assert create_v2.json()["status"] == "draft"
    assert create_v2.json()["previous_values"]["aggregate_monthly_limit_minor"] == 1500000

    # Submit v2 for approval
    sub_v2 = client.post(
        f"/api/v1/finance/policies/versions/{v2_id}/submit",
        headers={"X-User-Id": "fin_reviewer_01"},
    )
    assert sub_v2.status_code == 200
    assert sub_v2.json()["status"] == "pending_approval"

    # If Finance Controller creates a v3 and tries to self-approve, SoD blocks it!
    create_v3 = client.post(
        "/api/v1/finance/policies/FIELD_LIQUIDATION_POLICY/versions",
        headers={"X-User-Id": "fin_controller_01"},
        json={
            "effective_from": "2026-07-01",
            "aggregate_monthly_limit_minor": 2500000,
            "max_reports_per_period": 1,
            "change_justification": "Controller authored version",
        },
    )
    v3_id = create_v3.json()["id"]
    client.post(f"/api/v1/finance/policies/versions/{v3_id}/submit", headers={"X-User-Id": "fin_controller_01"})
    self_app_v3 = client.post(
        f"/api/v1/finance/policies/versions/{v3_id}/approve",
        headers={"X-User-Id": "fin_controller_01"},
        json={"comments": "Self-approving my own policy"},
    )
    assert self_app_v3.status_code == 403

    # Controller can approve v2 authored by fin_reviewer_01
    app_v2 = client.post(
        f"/api/v1/finance/policies/versions/{v2_id}/approve",
        headers={"X-User-Id": "fin_controller_01"},
        json={"comments": "Ratified v2 policy"},
    )
    assert app_v2.status_code == 200
    assert app_v2.json()["status"] == "active"


def test_stage2_cash_advance_approval_limits_and_6_person_disbursement_sod():
    # 1. Requestor creates PHP 60,000 advance (exceeds supervisor PHP 50,000 matrix limit)
    ca_high = client.post(
        "/api/v1/finance/cash-advances",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "purpose_code": "transportation_field_activity",
            "business_justification": "Regional field rollout across 8 sites",
            "requested_amount_minor": 6000000,
            "date_needed": "2026-05-10",
            "submit_now": True,
        },
    )
    assert ca_high.status_code == 201
    high_id = ca_high.json()["id"]

    # Self-approval by requestor blocked (403)
    self_app = client.post(
        f"/api/v1/finance/cash-advances/{high_id}/decide",
        headers={"X-User-Id": "emp_field_01"},
        json={"decision": "approved", "comments": "Self approve"},
    )
    assert self_app.status_code == 403

    # Supervisor exceeds approval limit (PHP 60,000 > PHP 50,000 max) -> 403
    sup_app_high = client.post(
        f"/api/v1/finance/cash-advances/{high_id}/decide",
        headers={"X-User-Id": "sup_approver_01"},
        json={"decision": "approved", "comments": "Supervisor trying to approve 60k"},
    )
    assert sup_app_high.status_code == 403
    assert "exceeds maximum approval limit" in sup_app_high.json()["detail"].lower()

    # Supervisor can approve within limit by approving PHP 10,000 on a PHP 10,000 request
    ca_10k = client.post(
        "/api/v1/finance/cash-advances",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "purpose_code": "school_visit",
            "business_justification": "Cavite school visit",
            "requested_amount_minor": 1000000,  # PHP 10,000.00
            "date_needed": "2026-05-12",
            "submit_now": True,
        },
    ).json()
    adv_id = ca_10k["id"]

    app_10k = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/decide",
        headers={"X-User-Id": "sup_approver_01"},
        json={"decision": "approved", "comments": "Approved PHP 10,000"},
    )
    assert app_10k.status_code == 200
    assert app_10k.json()["approval_status"] == "approved"
    assert app_10k.json()["payment_status"] == "not_started"
    # Approved is NOT released!
    assert app_10k.json()["reconciliation"]["released_amount_minor"] == 0

    # Check KPI excludes approved-but-unreleased advance from advances_released_minor
    kpi_before = client.get("/api/v1/finance/dashboard/summary", headers={"X-User-Id": "fin_controller_01"}).json()
    assert kpi_before["kpis"]["advances_released_minor"] == 0
    assert kpi_before["workflow_counts"]["cash_advances_approved_unreleased"] == 1

    # 2. Prepare Disbursement (Maker = fin_maker_01)
    disb_resp = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/disbursements",
        headers={"X-User-Id": "fin_maker_01"},
        json={"payment_amount_minor": 1000000, "payment_method": "bank_transfer"},
    )
    assert disb_resp.status_code == 201
    disb_id = disb_resp.json()["id"]

    # Negative SoD: Maker == Verifier must be rejected (403)
    maker_verify = client.post(
        f"/api/v1/finance/disbursements/{disb_id}/verify",
        headers={"X-User-Id": "fin_maker_01"},
        json={"notes": "Maker trying to verify own disbursement"},
    )
    assert maker_verify.status_code == 403
    assert "separation of duties" in maker_verify.json()["detail"].lower()

    # Verifier (fin_verifier_01) verifies
    ver_resp = client.post(
        f"/api/v1/finance/disbursements/{disb_id}/verify",
        headers={"X-User-Id": "fin_verifier_01"},
        json={"notes": "Verified payee and amount"},
    )
    assert ver_resp.status_code == 200
    assert ver_resp.json()["status"] == "verified"

    # Bank Approver (bank_approver_01) authorizes bank release
    bank_resp = client.post(
        f"/api/v1/finance/disbursements/{disb_id}/bank-approve",
        headers={"X-User-Id": "bank_approver_01"},
        json={"notes": "Authorized bank transfer"},
    )
    assert bank_resp.status_code == 200
    assert bank_resp.json()["status"] == "bank_approved"

    # Record manual execution evidence (bank_approver_01 attaches remittance advice PDF) -> executed_unverified
    remit_doc_id = _upload_pdf("bank_approver_01", "bank_remittance_advice", "remittance_advice_10k")
    exec_resp = client.post(
        f"/api/v1/finance/disbursements/{disb_id}/execute",
        headers={"X-User-Id": "bank_approver_01"},
        json={
            "bank_reference": "BPI-REF-20260512-001",
            "executed_amount_minor": 1000000,
            "evidence_document_id": remit_doc_id,
        },
    )
    assert exec_resp.status_code == 200
    assert exec_resp.json()["status"] == "executed_unverified"

    # Still NOT released until independent evidence verification!
    adv_mid = client.get(f"/api/v1/finance/cash-advances/{adv_id}", headers={"X-User-Id": "fin_controller_01"}).json()
    assert adv_mid["reconciliation"]["released_amount_minor"] == 0

    # Negative SoD: Bank Approver cannot also act as Evidence Verifier (403)
    same_ev_ver = client.post(
        f"/api/v1/finance/disbursements/{disb_id}/verify-evidence",
        headers={"X-User-Id": "bank_approver_01"},
        json={"notes": "Trying to verify my own execution evidence"},
    )
    assert same_ev_ver.status_code == 403

    # Distinct 6th actor (evidence_verifier_01) verifies evidence -> evidence_verified & released!
    ev_ver_resp = client.post(
        f"/api/v1/finance/disbursements/{disb_id}/verify-evidence",
        headers={"X-User-Id": "evidence_verifier_01"},
        json={"notes": "Confirmed bank remittance advice matches PHP 10,000.00"},
    )
    assert ev_ver_resp.status_code == 200
    assert ev_ver_resp.json()["status"] == "evidence_verified"

    adv_after = client.get(f"/api/v1/finance/cash-advances/{adv_id}", headers={"X-User-Id": "fin_controller_01"}).json()
    assert adv_after["reconciliation"]["released_amount_minor"] == 1000000
    assert adv_after["reconciliation"]["outstanding_balance_minor"] == 1000000


# ==============================================================================
# Stage 3 Tests: Liquidation, Aggregate Cap, Exceptions, Reconciliation & Closure
# ==============================================================================

def _release_advance_for_employee(employee_id: str, amount_minor: int, bank_ref: str) -> str:
    ca = client.post(
        "/api/v1/finance/cash-advances",
        headers={"X-User-Id": employee_id},
        json={
            "purpose_code": "school_visit",
            "business_justification": "Field activity advance",
            "requested_amount_minor": amount_minor,
            "date_needed": "2026-05-05",
            "submit_now": True,
        },
    ).json()
    adv_id = ca["id"]
    client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/decide",
        headers={"X-User-Id": "sup_approver_01"},
        json={"decision": "approved", "comments": "Approved"},
    )
    disb = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/disbursements",
        headers={"X-User-Id": "fin_maker_01"},
        json={"payment_amount_minor": amount_minor},
    ).json()
    disb_id = disb["id"]
    client.post(f"/api/v1/finance/disbursements/{disb_id}/verify", headers={"X-User-Id": "fin_verifier_01"}, json={"notes": "OK"})
    client.post(f"/api/v1/finance/disbursements/{disb_id}/bank-approve", headers={"X-User-Id": "bank_approver_01"}, json={"notes": "OK"})
    doc_id = _upload_pdf("bank_approver_01", "bank_remittance_advice", f"remit_{bank_ref}")
    client.post(
        f"/api/v1/finance/disbursements/{disb_id}/execute",
        headers={"X-User-Id": "bank_approver_01"},
        json={"bank_reference": bank_ref, "executed_amount_minor": amount_minor, "evidence_document_id": doc_id},
    )
    client.post(
        f"/api/v1/finance/disbursements/{disb_id}/verify-evidence",
        headers={"X-User-Id": "evidence_verifier_01"},
        json={"notes": "Verified release"},
    )
    return adv_id


def test_stage3_hand_calculated_reconciliation_and_closure_gates():
    """
    Plan v2 §11 Acceptance Fixture:
    PHP 10,000 (1,000,000 centavos) released,
    PHP 8,700 (870,000 centavos) approved expenses,
    PHP 1,300 (130,000 centavos) unused returned ->
    remaining balance is 0, but advance cannot close until evidence and Controller sign-off complete.
    """
    adv_id = _release_advance_for_employee("emp_field_01", 1000000, "BNK-10000-01")

    # Upload consolidated OR PDF and MFAR PDF
    or_pdf_id = _upload_pdf("emp_field_01", "consolidated_receipt_pdf", "or_may_2026")
    mfar_pdf_id = _upload_pdf("emp_field_01", "mfar", "mfar_may_2026")

    rep = client.post(
        "/api/v1/finance/expense-reports",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "reporting_period": "2026-05",
            "business_purpose_summary": "May 2026 School Visits",
            "out_of_base_activity": True,
            "consolidated_receipt_doc_id": or_pdf_id,
            "mfar_doc_id": mfar_pdf_id,
            "employee_declaration_confirmed": True,
        },
    ).json()
    rep_id = rep["id"]

    # Add two lines totaling PHP 8,700 (5,000 + 3,700)
    l1_resp = client.post(
        f"/api/v1/finance/expense-reports/{rep_id}/lines",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "transaction_date": "2026-05-06",
            "expense_category": "transportation",
            "business_purpose": "Bus and shuttle to San Pedro school",
            "merchant_or_payee": "JAC Liner Inc.",
            "claimed_amount_minor": 500000,
            "receipt_or_invoice_no": "OR-99101",
            "school_or_destination": "San Pedro National High School",
            "out_of_base": True,
        },
    )
    assert l1_resp.status_code == 201
    l2_resp = client.post(
        f"/api/v1/finance/expense-reports/{rep_id}/lines",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "transaction_date": "2026-05-07",
            "expense_category": "accommodation",
            "business_purpose": "Overnight field stay",
            "merchant_or_payee": "Laguna Field Inn",
            "claimed_amount_minor": 370000,
            "receipt_or_invoice_no": "OR-99102",
            "school_or_destination": "Binan Elementary School",
            "out_of_base": True,
        },
    )
    assert l2_resp.status_code == 201
    lines = l2_resp.json()["lines"]

    # Submit report
    sub_resp = client.post(
        f"/api/v1/finance/expense-reports/{rep_id}/submit",
        headers={"X-User-Id": "emp_field_01"},
        json={"employee_declaration_confirmed": True},
    )
    assert sub_resp.status_code == 200
    assert sub_resp.json()["review_status"] == "submitted"

    # Finance Reviewer approves both lines (PHP 8,700)
    rev_resp = client.post(
        f"/api/v1/finance/expense-reports/{rep_id}/review",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={
            "report_decision": "approved",
            "review_notes": "All receipts and MFAR verified.",
            "line_decisions": [
                {"line_id": lines[0]["id"], "line_status": "approved", "approved_amount_minor": 500000},
                {"line_id": lines[1]["id"], "line_status": "approved", "approved_amount_minor": 370000},
            ],
        },
    )
    assert rev_resp.status_code == 200

    # Check advance reconciliation: Released 10,000 - Approved 8,700 = 1,300 outstanding!
    adv_mid = client.get(f"/api/v1/finance/cash-advances/{adv_id}", headers={"X-User-Id": "emp_field_01"}).json()
    recon_mid = adv_mid["reconciliation"]
    assert recon_mid["released_amount_minor"] == 1000000
    assert recon_mid["approved_expenses_applied_minor"] == 870000
    assert recon_mid["unused_funds_expected_minor"] == 130000
    assert recon_mid["outstanding_balance_minor"] == 130000
    assert adv_mid["settlement_status"] == "settlement_pending"

    # Premature closure while PHP 1,300 is still outstanding must fail (409)
    premature_close = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/close",
        headers={"X-User-Id": "fin_controller_01"},
        json={"closure_reason": "Trying to close before PHP 1,300 cash return"},
    )
    assert premature_close.status_code == 409

    # Record evidenced cash return of PHP 1,300 (130,000 centavos)
    ret_doc_id = _upload_pdf("fin_reviewer_01", "cash_return_receipt", "deposit_slip_1300")
    ret_resp = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/cash-return",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={
            "returned_amount_minor": 130000,
            "bank_reference": "DEP-20260515-1300",
            "evidence_document_id": ret_doc_id,
            "notes": "Unused advance cash deposited to company bank account",
        },
    )
    assert ret_resp.status_code == 200
    recon_zero = ret_resp.json()["reconciliation"]
    assert recon_zero["cash_returned_minor"] == 130000
    assert recon_zero["outstanding_balance_minor"] == 0
    # Zero balance alone does NOT close the advance! Status is 'reconciled', not 'closed'
    assert ret_resp.json()["settlement_status"] == "reconciled"
    assert ret_resp.json()["overall_stage"] == "Reconciled"

    # Non-controller cannot close the advance (403)
    non_ctrl_close = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/close",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={"closure_reason": "Reviewer attempting closure"},
    )
    assert non_ctrl_close.status_code == 403

    # Finance Controller closes the advance -> freezes ClosingSnapshot and sets status to 'closed'
    close_resp = client.post(
        f"/api/v1/finance/cash-advances/{adv_id}/close",
        headers={"X-User-Id": "fin_controller_01"},
        json={"closure_reason": "All receipts, MFAR, and PHP 1,300 deposit slip verified; reconciled to zero."},
    )
    assert close_resp.status_code == 200
    assert close_resp.json()["settlement_status"] == "closed"
    assert close_resp.json()["overall_stage"] == "Closed"


def test_stage3_split_report_cap_bypass_blocked_and_single_use_exception():
    """
    Tests:
    1. First report for 2026-05 claims PHP 10,000 -> succeeds.
    2. Second report in same period 2026-05 claiming PHP 6,000 violates BOTH:
       - MONTHLY_REPORT_FREQUENCY (2 > 1)
       - MONTHLY_AGGREGATE_CAP (10,000 + 6,000 = 16,000 > 15,000)
    3. Self-approving an ExceptionRequest is blocked (403).
    4. Expired ExceptionRequest is not accepted.
    5. Controller approves single-use exceptions -> second report succeeds and consumes the exceptions so a third report cannot reuse them.
    """
    or_doc = _upload_pdf("emp_field_01", "consolidated_receipt_pdf", "or_rep1")
    rep1 = client.post(
        "/api/v1/finance/expense-reports",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "reporting_period": "2026-05",
            "business_purpose_summary": "Report 1 (PHP 10,000)",
            "consolidated_receipt_doc_id": or_doc,
            "employee_declaration_confirmed": True,
        },
    ).json()
    client.post(
        f"/api/v1/finance/expense-reports/{rep1['id']}/lines",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "transaction_date": "2026-05-03",
            "expense_category": "transportation",
            "business_purpose": "Field transport",
            "merchant_or_payee": "Victory Liner",
            "claimed_amount_minor": 1000000,
            "receipt_or_invoice_no": "OR-1001",
        },
    )
    sub1 = client.post(
        f"/api/v1/finance/expense-reports/{rep1['id']}/submit",
        headers={"X-User-Id": "emp_field_01"},
        json={"employee_declaration_confirmed": True},
    )
    assert sub1.status_code == 200

    # Create second report in 2026-05 for PHP 6,000 (aggregate = PHP 16,000 > PHP 15,000)
    or_doc2 = _upload_pdf("emp_field_01", "consolidated_receipt_pdf", "or_rep2")
    rep2 = client.post(
        "/api/v1/finance/expense-reports",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "reporting_period": "2026-05",
            "business_purpose_summary": "Report 2 split bypass attempt (PHP 6,000)",
            "consolidated_receipt_doc_id": or_doc2,
            "employee_declaration_confirmed": True,
        },
    ).json()
    client.post(
        f"/api/v1/finance/expense-reports/{rep2['id']}/lines",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "transaction_date": "2026-05-18",
            "expense_category": "meals",
            "business_purpose": "Field meals",
            "merchant_or_payee": "Jollibee Corp",
            "claimed_amount_minor": 600000,
            "receipt_or_invoice_no": "OR-1002",
        },
    )
    sub2_blocked = client.post(
        f"/api/v1/finance/expense-reports/{rep2['id']}/submit",
        headers={"X-User-Id": "emp_field_01"},
        json={"employee_declaration_confirmed": True},
    )
    assert sub2_blocked.status_code == 422
    detail = sub2_blocked.json()["detail"]
    violated_rules = {r["rule_id"] for r in detail["results"] if r["outcome"] == "violated"}
    assert "MONTHLY_REPORT_FREQUENCY" in violated_rules
    assert "MONTHLY_AGGREGATE_CAP" in violated_rules

    # Request frequency exception & cap exception for rep2
    exc_freq = client.post(
        "/api/v1/finance/exception-requests",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "target_entity_type": "expense_report",
            "target_entity_id": rep2["id"],
            "reporting_period": "2026-05",
            "violated_rule_code": "MONTHLY_REPORT_FREQUENCY",
            "exception_type": "frequency_override",
            "requested_amount_minor": 0,
            "business_justification": "Emergency second school visit batch in May",
        },
    ).json()

    # Self-approval of exception by requester blocked (403)
    self_exc = client.post(
        f"/api/v1/finance/exception-requests/{exc_freq['id']}/decide",
        headers={"X-User-Id": "emp_field_01"},
        json={"decision": "approved", "decision_reason": "Self approve"},
    )
    assert self_exc.status_code == 403

    # Approve frequency exception with ALREADY EXPIRED timestamp -> should expire and not allow submission
    past_iso = (datetime.now(timezone.utc) - timedelta(hours=2)).isoformat()
    client.post(
        f"/api/v1/finance/exception-requests/{exc_freq['id']}/decide",
        headers={"X-User-Id": "fin_controller_01"},
        json={
            "decision": "approved",
            "decision_reason": "Approved with past expiry",
            "expires_at": past_iso,
        },
    )
    sub2_expired = client.post(
        f"/api/v1/finance/expense-reports/{rep2['id']}/submit",
        headers={"X-User-Id": "emp_field_01"},
        json={"employee_declaration_confirmed": True},
    )
    assert sub2_expired.status_code == 422

    # Now create valid non-expired exceptions for BOTH frequency and aggregate cap (up to PHP 16,000 = 1,600,000)
    exc_freq_valid = client.post(
        "/api/v1/finance/exception-requests",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "target_entity_type": "expense_report",
            "target_entity_id": rep2["id"],
            "reporting_period": "2026-05",
            "violated_rule_code": "MONTHLY_REPORT_FREQUENCY",
            "exception_type": "frequency_override",
            "requested_amount_minor": 0,
            "business_justification": "Valid second report override",
        },
    ).json()
    exc_cap_valid = client.post(
        "/api/v1/finance/exception-requests",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "target_entity_type": "expense_report",
            "target_entity_id": rep2["id"],
            "reporting_period": "2026-05",
            "violated_rule_code": "MONTHLY_AGGREGATE_CAP",
            "exception_type": "amount_cap_override",
            "requested_amount_minor": 1600000,
            "business_justification": "Valid PHP 16,000 aggregate cap override",
        },
    ).json()

    client.post(
        f"/api/v1/finance/exception-requests/{exc_freq_valid['id']}/decide",
        headers={"X-User-Id": "fin_controller_01"},
        json={"decision": "approved", "decision_reason": "Controller approved 2nd report"},
    )
    client.post(
        f"/api/v1/finance/exception-requests/{exc_cap_valid['id']}/decide",
        headers={"X-User-Id": "fin_controller_01"},
        json={
            "decision": "approved",
            "decision_reason": "Controller approved PHP 16,000 cap",
            "approved_max_amount_minor": 1600000,
        },
    )

    # Submit rep2 -> now succeeds and consumes both exceptions!
    sub2_ok = client.post(
        f"/api/v1/finance/expense-reports/{rep2['id']}/submit",
        headers={"X-User-Id": "emp_field_01"},
        json={"employee_declaration_confirmed": True},
    )
    assert sub2_ok.status_code == 200
    assert sub2_ok.json()["policy_evaluation"]["overall_outcome"] == "approved_with_exception"

    # Attempting a 3rd report in 2026-05 cannot reuse the consumed exceptions!
    rep3 = client.post(
        "/api/v1/finance/expense-reports",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "reporting_period": "2026-05",
            "business_purpose_summary": "Report 3 trying to reuse consumed exception",
            "consolidated_receipt_doc_id": or_doc2,
            "employee_declaration_confirmed": True,
        },
    ).json()
    client.post(
        f"/api/v1/finance/expense-reports/{rep3['id']}/lines",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "transaction_date": "2026-05-25",
            "expense_category": "meals",
            "business_purpose": "More meals",
            "merchant_or_payee": "Cafe",
            "claimed_amount_minor": 50000,
            "receipt_or_invoice_no": "OR-1003",
        },
    )
    sub3 = client.post(
        f"/api/v1/finance/expense-reports/{rep3['id']}/submit",
        headers={"X-User-Id": "emp_field_01"},
        json={"employee_declaration_confirmed": True},
    )
    assert sub3.status_code == 422


def test_stage3_rejected_expense_shortfall_remains_receivable():
    adv_id = _release_advance_for_employee("emp_field_02", 500000, "BNK-5000-EMP2")
    or_doc = _upload_pdf("emp_field_02", "consolidated_receipt_pdf", "or_emp2")
    rep = client.post(
        "/api/v1/finance/expense-reports",
        headers={"X-User-Id": "emp_field_02"},
        json={
            "reporting_period": "2026-05",
            "business_purpose_summary": "Logistics field expenses",
            "consolidated_receipt_doc_id": or_doc,
            "employee_declaration_confirmed": True,
        },
    ).json()
    l_resp = client.post(
        f"/api/v1/finance/expense-reports/{rep['id']}/lines",
        headers={"X-User-Id": "emp_field_02"},
        json={
            "transaction_date": "2026-05-08",
            "expense_category": "fuel",
            "business_purpose": "Delivery van fuel",
            "merchant_or_payee": "Petron Station",
            "claimed_amount_minor": 500000,
            "receipt_or_invoice_no": "OR-5501",
        },
    ).json()
    line_id = l_resp["lines"][0]["id"]

    client.post(
        f"/api/v1/finance/expense-reports/{rep['id']}/submit",
        headers={"X-User-Id": "emp_field_02"},
        json={"employee_declaration_confirmed": True},
    )

    # Reviewer partially approves only PHP 3,500 out of PHP 5,000 (PHP 1,500 rejected shortfall)
    rev = client.post(
        f"/api/v1/finance/expense-reports/{rep['id']}/review",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={
            "report_decision": "partially_approved",
            "review_notes": "PHP 1,500 personal item on receipt disallowed.",
            "line_decisions": [
                {
                    "line_id": line_id,
                    "line_status": "partially_approved",
                    "approved_amount_minor": 350000,
                    "rejection_reason": "Personal purchase disallowed",
                }
            ],
        },
    )
    assert rev.status_code == 200

    # Verify advance stays in 'shortfall_receivable' with PHP 1,500 due from employee and open exception register item
    adv = client.get(f"/api/v1/finance/cash-advances/{adv_id}", headers={"X-User-Id": "fin_controller_01"}).json()
    assert adv["settlement_status"] == "shortfall_receivable"
    assert adv["reconciliation"]["outstanding_balance_minor"] == 150000
    assert adv["reconciliation"]["rejected_expenses_minor"] == 150000

    exc_reg = client.get("/api/v1/finance/exception-register", headers={"X-User-Id": "fin_controller_01"}).json()
    assert any(e["exception_category"] == "rejected_expense_shortfall" and e["amount_minor"] == 150000 for e in exc_reg)


# ==============================================================================
# Stage 4 Tests: Purchase-to-Pay, Vendor Controls, Duplicate Invoices & Unknowns
# ==============================================================================

def test_stage4_vendor_verification_p2p_partial_payments_and_unknown_outcome():
    # 1. Seeded vendors are 'pending_verification' with no bank data
    vendors = client.get("/api/v1/finance/vendors", headers={"X-User-Id": "fin_reviewer_01"}).json()
    assert len(vendors) >= 3
    supplier = vendors[0]
    assert supplier["verification_status"] == "pending_verification"
    assert supplier.get("bank_account_ref") is None

    # Creator (fin_maker_01) cannot verify their own seeded vendor (403)
    self_ver_vendor = client.post(
        f"/api/v1/finance/vendors/{supplier['id']}/verify",
        headers={"X-User-Id": "fin_maker_01"},
        json={"decision": "verified", "notes": "Self verify"},
    )
    assert self_ver_vendor.status_code == 403

    # Request and approve bank account change (maker != verifier)
    bc = client.post(
        f"/api/v1/finance/vendors/{supplier['id']}/bank-changes",
        headers={"X-User-Id": "fin_maker_01"},
        json={
            "new_bank_name": "BDO Unibank",
            "new_bank_account_ref": "004120987654",
            "reason": "Initial vendor enrollment bank details",
        },
    ).json()
    # Same user cannot approve bank change
    assert (
        client.post(
            f"/api/v1/finance/vendor-bank-changes/{bc['id']}/decide",
            headers={"X-User-Id": "fin_maker_01"},
            json={"decision": "approved"},
        ).status_code
        == 403
    )
    # Verifier approves bank change
    assert (
        client.post(
            f"/api/v1/finance/vendor-bank-changes/{bc['id']}/decide",
            headers={"X-User-Id": "fin_verifier_01"},
            json={"decision": "approved"},
        ).status_code
        == 200
    )

    # Create and approve Purchase Request (PHP 20,000 = 2,000,000 centavos)
    pr = client.post(
        "/api/v1/finance/purchase-requests",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "vendor_id": supplier["id"],
            "payment_category": "supplier_invoice",
            "business_purpose": "Industrial fasteners and safety gear",
            "requested_amount_minor": 2000000,
            "requested_payment_date": "2026-05-20",
            "submit_now": True,
        },
    ).json()
    pr_id = pr["id"]

    client.post(
        f"/api/v1/finance/purchase-requests/{pr_id}/decide",
        headers={"X-User-Id": "sup_approver_01"},
        json={"decision": "approved", "comments": "Budget verified"},
    )

    # Attempting to validate payable while vendor is still 'pending_verification' fails (409)
    unver_ap = client.post(
        "/api/v1/finance/payables",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={
            "purchase_request_id": pr_id,
            "invoice_number": "SI-2026-0088",
            "invoice_date": "2026-05-12",
            "due_date": "2026-05-25",
            "original_payable_minor": 2000000,
        },
    )
    assert unver_ap.status_code == 409

    # Verify vendor by distinct user (fin_reviewer_01)
    client.post(
        f"/api/v1/finance/vendors/{supplier['id']}/verify",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={"decision": "verified", "notes": "BIR 2303 and KYC checked"},
    )

    # Validate payable (without CPA tax code -> flagged tax_review_required)
    ap_resp = client.post(
        "/api/v1/finance/payables",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={
            "purchase_request_id": pr_id,
            "invoice_number": "SI-2026-0088",
            "invoice_date": "2026-05-12",
            "due_date": "2026-05-25",
            "original_payable_minor": 2000000,
        },
    )
    assert ap_resp.status_code == 201
    ap = ap_resp.json()
    ap_id = ap["id"]
    assert ap["tax_review_status"] == "tax_review_required"

    # Duplicate invoice number for same vendor is rejected (409)
    dup_ap = client.post(
        "/api/v1/finance/payables",
        headers={"X-User-Id": "fin_reviewer_01"},
        json={
            "purchase_request_id": pr_id,
            "invoice_number": "si-2026/0088",  # normalizes to SI20260088
            "invoice_date": "2026-05-12",
            "due_date": "2026-05-25",
            "original_payable_minor": 2000000,
        },
    )
    assert dup_ap.status_code == 409

    # Partial payment #1: PHP 12,000 (1,200,000 centavos)
    pay1 = client.post(
        f"/api/v1/finance/payables/{ap_id}/payments",
        headers={"X-User-Id": "fin_maker_01"},
        json={"payment_amount_minor": 1200000},
    ).json()
    client.post(f"/api/v1/finance/payment-records/{pay1['id']}/verify", headers={"X-User-Id": "fin_verifier_01"}, json={"notes": "OK"})
    client.post(f"/api/v1/finance/payment-records/{pay1['id']}/bank-approve", headers={"X-User-Id": "bank_approver_01"}, json={"notes": "OK"})
    rem_doc1 = _upload_pdf("bank_approver_01", "bank_remittance_advice", "ap_remit_1")
    client.post(
        f"/api/v1/finance/payment-records/{pay1['id']}/execute",
        headers={"X-User-Id": "bank_approver_01"},
        json={
            "bank_reference": "BDO-AP-001",
            "executed_amount_minor": 1200000,
            "execution_date": "2026-05-15",
            "remittance_doc_id": rem_doc1,
        },
    )
    rec1 = client.post(
        f"/api/v1/finance/payment-records/{pay1['id']}/reconcile",
        headers={"X-User-Id": "evidence_verifier_01"},
        json={"reconciliation_outcome": "matched", "notes": "Matched PHP 12,000 partial payment"},
    )
    assert rec1.status_code == 200

    # Check payable status is 'partially_paid' with PHP 8,000 (800,000 centavos) remaining
    ap_list = client.get("/api/v1/finance/payables", headers={"X-User-Id": "fin_reviewer_01"}).json()
    ap_curr = next(x for x in ap_list if x["id"] == ap_id)
    assert ap_curr["status"] == "partially_paid"
    assert ap_curr["paid_confirmed_minor"] == 1200000
    assert ap_curr["remaining_balance_minor"] == 800000

    # Payment #2 for remaining PHP 8,000 enters 'payment_unknown' -> blocks duplicate payment preparation
    pay2 = client.post(
        f"/api/v1/finance/payables/{ap_id}/payments",
        headers={"X-User-Id": "fin_maker_01"},
        json={"payment_amount_minor": 800000},
    ).json()
    client.post(f"/api/v1/finance/payment-records/{pay2['id']}/verify", headers={"X-User-Id": "fin_verifier_01"}, json={"notes": "OK"})
    client.post(f"/api/v1/finance/payment-records/{pay2['id']}/bank-approve", headers={"X-User-Id": "bank_approver_01"}, json={"notes": "OK"})
    unk = client.post(
        f"/api/v1/finance/payment-records/{pay2['id']}/exception-state",
        headers={"X-User-Id": "bank_approver_01"},
        json={"target_status": "payment_unknown", "reason": "Bank portal timeout during batch upload"},
    )
    assert unk.status_code == 200
    assert unk.json()["status"] == "payment_unknown"

    # Trying to prepare a new payment while pay2 is 'payment_unknown' is blocked (409)
    retry_blocked = client.post(
        f"/api/v1/finance/payables/{ap_id}/payments",
        headers={"X-User-Id": "fin_maker_01"},
        json={"payment_amount_minor": 800000},
    )
    assert retry_blocked.status_code == 409


# ==============================================================================
# Stage 5 Tests: Document Security, KPIs vs Source Records & CSV Formula Safety
# ==============================================================================

def test_stage5_document_security_kpis_and_formula_safe_csv_export():
    # 1. Invalid magic-byte upload rejected (422)
    bad_upload = client.post(
        "/api/v1/finance/documents",
        headers={"X-User-Id": "emp_field_01"},
        json={
            "original_filename": "fake.pdf",
            "document_type": "consolidated_receipt_pdf",
            "content_base64": base64.b64encode(b"NOT_A_VALID_PDF_OR_IMAGE").decode("ascii"),
        },
    )
    assert bad_upload.status_code == 422

    # 2. Unauthorized document download blocked (403)
    emp1_doc_id = _upload_pdf("emp_field_01", "consolidated_receipt_pdf", "emp1_private")
    unauth_dl = client.get(
        f"/api/v1/finance/documents/{emp1_doc_id}/download",
        headers={"X-User-Id": "emp_field_02"},
    )
    assert unauth_dl.status_code == 403

    # Restricted payroll document blocked for regular supervisor (403), allowed for finance_controller (200)
    rest_doc_id = _upload_pdf("fin_maker_01", "payroll_summary_restricted", "payroll_may", restricted=True)
    sup_dl = client.get(
        f"/api/v1/finance/documents/{rest_doc_id}/download",
        headers={"X-User-Id": "sup_approver_01"},
    )
    assert sup_dl.status_code == 403
    ctrl_dl = client.get(
        f"/api/v1/finance/documents/{rest_doc_id}/download",
        headers={"X-User-Id": "fin_controller_01"},
    )
    assert ctrl_dl.status_code == 200

    # 3. Create advance with formula-injection payload in destination/remarks and verify CSV export neutralizes it
    _release_advance_for_employee("emp_field_01", 750000, "BNK-CSV-7500")
    client.post(
        "/api/v1/finance/cash-advances",
        headers={"X-User-Id": "emp_field_02"},
        json={
            "purpose_code": "other_approved_activity",
            "business_justification": "=cmd|' /C calc'!A0",
            "requested_amount_minor": 300000,
            "date_needed": "2026-05-20",
        },
    )

    # Compare dashboard KPIs with source records
    dash = client.get("/api/v1/finance/dashboard/summary", headers={"X-User-Id": "fin_controller_01"}).json()
    assert dash["kpis"]["advances_released_minor"] == 750000
    assert dash["kpis"]["outstanding_advance_balance_minor"] == 750000

    # Export CSV as fin_controller_01 -> starts with UTF-8 BOM (\ufeff) and raw numeric values
    csv_resp = client.get("/api/v1/finance/tracker/export.csv", headers={"X-User-Id": "fin_controller_01"})
    assert csv_resp.status_code == 200
    csv_text = csv_resp.content.decode("utf-8")
    assert csv_text.startswith("\ufeffReference Number,Workflow")
    assert "7500.00" in csv_text
    assert "₱7,500" not in csv_text  # Raw numeric cell integrity per rule

    # Export CSV as emp_field_02 -> permission-scoped: must NOT include emp_field_01's 7500.00 advance!
    emp2_csv = client.get("/api/v1/finance/tracker/export.csv", headers={"X-User-Id": "emp_field_02"}).content.decode("utf-8")
    assert "emp_field_01" not in emp2_csv
    assert "emp_field_02" in emp2_csv


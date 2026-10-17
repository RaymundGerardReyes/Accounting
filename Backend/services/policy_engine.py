import json
import uuid
from datetime import datetime, timezone
from typing import Any, Optional

from sqlalchemy import update
from sqlalchemy.orm import Session
from sqlalchemy.exc import IntegrityError

from finance_models import (
    FinancialPolicy,
    PolicyVersion,
    PolicyRule,
    PeriodCounter,
    ExceptionRequest,
    ApprovalHistory,
    DocumentReference,
    ExpenseReport,
    ExpenseLine,
    CashAdvance,
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
)

VALID_POLICY_TRANSITIONS: dict[str, set[str]] = {
    "draft": {"pending_approval"},
    "pending_approval": {"active", "draft"},
    "active": {"retired"},
    "retired": set(),
}

VALID_EXCEPTION_TRANSITIONS: dict[str, set[str]] = {
    "draft": {"submitted"},
    "submitted": {"under_review", "approved", "rejected", "expired"},
    "under_review": {"approved", "rejected", "expired"},
    "approved": {"consumed", "expired"},
    "rejected": set(),
    "expired": set(),
    "consumed": set(),
}


class PolicyEngineService:
    def list_policies(self, db: Session) -> list[dict[str, Any]]:
        policies = db.query(FinancialPolicy).order_by(FinancialPolicy.created_at.asc()).all()
        result = []
        for pol in policies:
            versions = (
                db.query(PolicyVersion)
                .filter(PolicyVersion.policy_id == pol.id)
                .order_by(PolicyVersion.version_number.desc())
                .all()
            )
            ver_dicts = []
            for v in versions:
                rules = db.query(PolicyRule).filter(PolicyRule.policy_version_id == v.id).all()
                ver_dicts.append(v.to_dict(rules=[r.to_dict() for r in rules]))
            pol_dict = pol.to_dict()
            pol_dict["versions"] = ver_dicts
            result.append(pol_dict)
        return result

    def get_applicable_policy_version(
        self,
        db: Session,
        scope_workflow: str = "expense_liquidation",
        target_date_str: Optional[str] = None,
    ) -> PolicyVersion:
        eff_date = (target_date_str or manila_today_str())[:10]
        policy = (
            db.query(FinancialPolicy)
            .filter(FinancialPolicy.scope_workflow == scope_workflow)
            .first()
        )
        if not policy:
            raise FinanceNotFoundError(f"No financial policy configured for workflow '{scope_workflow}'.")

        versions = (
            db.query(PolicyVersion)
            .filter(
                PolicyVersion.policy_id == policy.id,
                PolicyVersion.status == "active",
                PolicyVersion.effective_from <= eff_date,
            )
            .order_by(PolicyVersion.version_number.desc())
            .all()
        )
        for v in versions:
            if not v.effective_until or v.effective_until >= eff_date:
                return v

        # Fallback to current active version if target_date precedes initial effective_from
        if policy.current_version_id:
            curr = db.query(PolicyVersion).filter(PolicyVersion.id == policy.current_version_id).first()
            if curr and curr.status == "active":
                return curr

        raise FinanceValidationError(
            f"No active policy version effective on {eff_date} for workflow '{scope_workflow}'."
        )

    def create_policy_version(
        self,
        db: Session,
        actor: ResolvedActor,
        policy_code: str,
        effective_from: str,
        effective_until: Optional[str],
        aggregate_monthly_limit_minor: int,
        max_reports_per_period: int,
        submission_deadline_day_of_next_month: Optional[int],
        finance_sla_days: int,
        require_consolidated_receipt_pdf: bool,
        require_mfar_for_out_of_base: bool,
        allow_partial_line_approval: bool,
        block_new_advance_when_overdue: bool,
        max_advance_amount_minor: Optional[int],
        eligible_categories: list[str],
        change_justification: str,
        rule_overrides: Optional[list[dict[str, Any]]] = None,
    ) -> dict[str, Any]:
        require_roles(actor, "finance_reviewer", "finance_controller", action_desc="author a policy version")
        if not change_justification or len(change_justification.strip()) < 5:
            raise FinanceValidationError("A clear change justification is required when creating a policy version.")
        if aggregate_monthly_limit_minor <= 0 or max_reports_per_period <= 0:
            raise FinanceValidationError("Policy monetary limit and frequency limit must be positive integers.")

        policy = db.query(FinancialPolicy).filter(FinancialPolicy.policy_code == policy_code).first()
        if not policy:
            raise FinanceNotFoundError(f"Policy '{policy_code}' not found.")

        latest = (
            db.query(PolicyVersion)
            .filter(PolicyVersion.policy_id == policy.id)
            .order_by(PolicyVersion.version_number.desc())
            .first()
        )
        next_ver_no = (int(latest.version_number) + 1) if latest else 1
        prev_snapshot = None
        if latest:
            prev_snapshot = {
                "version_number": latest.version_number,
                "aggregate_monthly_limit_minor": latest.aggregate_monthly_limit_minor,
                "max_reports_per_period": latest.max_reports_per_period,
                "effective_from": latest.effective_from,
                "effective_until": latest.effective_until,
                "submission_deadline_day_of_next_month": latest.submission_deadline_day_of_next_month,
                "finance_sla_days": latest.finance_sla_days,
                "require_consolidated_receipt_pdf": bool(latest.require_consolidated_receipt_pdf),
                "require_mfar_for_out_of_base": bool(latest.require_mfar_for_out_of_base),
            }

        ver_id = str(uuid.uuid4())
        ver = PolicyVersion(
            id=ver_id,
            policy_id=policy.id,
            version_number=next_ver_no,
            status="draft",
            effective_from=effective_from[:10],
            effective_until=effective_until[:10] if effective_until else None,
            currency="PHP",
            aggregate_monthly_limit_minor=int(aggregate_monthly_limit_minor),
            max_reports_per_period=int(max_reports_per_period),
            period_type="monthly",
            submission_deadline_day_of_next_month=submission_deadline_day_of_next_month,
            deadline_interpretation="day_of_following_month_configured",
            finance_sla_days=int(finance_sla_days),
            sla_day_type="business_days",
            require_consolidated_receipt_pdf=1 if require_consolidated_receipt_pdf else 0,
            require_mfar_for_out_of_base=1 if require_mfar_for_out_of_base else 0,
            allow_partial_line_approval=1 if allow_partial_line_approval else 0,
            block_new_advance_when_overdue=1 if block_new_advance_when_overdue else 0,
            max_advance_amount_minor=int(max_advance_amount_minor) if max_advance_amount_minor is not None else None,
            eligible_categories_json=json.dumps(eligible_categories),
            previous_values_json=json.dumps(prev_snapshot) if prev_snapshot else None,
            change_justification=change_justification.strip(),
            ratification_status="draft",
            author_id=actor.id,
            created_at=utc_now(),
        )
        db.add(ver)
        db.flush()

        override_map = {ro["rule_code"]: ro for ro in (rule_overrides or []) if "rule_code" in ro}
        default_rules = [
            {
                "rule_code": "MONTHLY_AGGREGATE_CAP",
                "description": "Aggregate monthly claimed expenses per employee must not exceed configured limit.",
                "severity": "block",
                "enforcement_mode": "exception_allowed",
                "is_exception_allowed": True,
                "required_exception_role": "finance_controller",
                "parameters": {"aggregate_monthly_limit_minor": int(aggregate_monthly_limit_minor)},
            },
            {
                "rule_code": "MONTHLY_REPORT_FREQUENCY",
                "description": "Maximum number of non-cancelled/non-rejected expense reports per employee per period.",
                "severity": "block",
                "enforcement_mode": "exception_allowed",
                "is_exception_allowed": True,
                "required_exception_role": "finance_controller",
                "parameters": {"max_reports_per_period": int(max_reports_per_period)},
            },
            {
                "rule_code": "CONSOLIDATED_RECEIPT_PDF",
                "description": "Consolidated PDF of Official Receipts must be attached before submission.",
                "severity": "block",
                "enforcement_mode": "exception_allowed",
                "is_exception_allowed": True,
                "required_exception_role": "finance_controller",
                "parameters": {"required": bool(require_consolidated_receipt_pdf)},
            },
            {
                "rule_code": "OUT_OF_BASE_MFAR",
                "description": "Out-of-base activities require an attached MFAR document and destination/site.",
                "severity": "block",
                "enforcement_mode": "exception_allowed",
                "is_exception_allowed": True,
                "required_exception_role": "finance_controller",
                "parameters": {"required": bool(require_mfar_for_out_of_base)},
            },
            {
                "rule_code": "ELIGIBLE_CATEGORY",
                "description": "Expense lines must use an eligible expense category.",
                "severity": "block",
                "enforcement_mode": "hard_block",
                "is_exception_allowed": False,
                "required_exception_role": "finance_controller",
                "parameters": {"eligible_categories": eligible_categories},
            },
            {
                "rule_code": "DUPLICATE_RECEIPT_CHECK",
                "description": "Flag potential duplicate receipts for Finance review.",
                "severity": "flag",
                "enforcement_mode": "warn_only",
                "is_exception_allowed": True,
                "required_exception_role": "finance_reviewer",
                "parameters": {"check_sha256": True, "check_fingerprint": True},
            },
        ]
        created_rules = []
        for rdef in default_rules:
            ov = override_map.get(rdef["rule_code"], {})
            enf_mode = ov.get("enforcement_mode", rdef["enforcement_mode"])
            exc_allowed = ov.get("is_exception_allowed", rdef["is_exception_allowed"])
            if enf_mode == "hard_block":
                exc_allowed = False
            pr = PolicyRule(
                policy_version_id=ver.id,
                rule_code=rdef["rule_code"],
                description=ov.get("description", rdef["description"]),
                severity=ov.get("severity", rdef["severity"]),
                enforcement_mode=enf_mode,
                is_exception_allowed=1 if exc_allowed else 0,
                required_exception_role=ov.get("required_exception_role", rdef["required_exception_role"]),
                parameters_json=json.dumps(rdef["parameters"]),
            )
            db.add(pr)
            created_rules.append(pr)

        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="policy_version_created",
            entity_type="policy_version",
            entity_id=ver.id,
            previous_state=prev_snapshot,
            new_state={
                "version_number": next_ver_no,
                "status": "draft",
                "aggregate_monthly_limit_minor": aggregate_monthly_limit_minor,
                "max_reports_per_period": max_reports_per_period,
                "effective_from": effective_from[:10],
            },
            reason=change_justification.strip(),
        )
        db.commit()
        db.refresh(ver)
        return ver.to_dict(rules=[r.to_dict() for r in created_rules])

    def submit_policy_version(self, db: Session, actor: ResolvedActor, version_id: str) -> dict[str, Any]:
        require_roles(actor, "finance_reviewer", "finance_controller", action_desc="submit a policy version")
        ver = db.query(PolicyVersion).filter(PolicyVersion.id == version_id).first()
        if not ver:
            raise FinanceNotFoundError("Policy version not found.")
        if "pending_approval" not in VALID_POLICY_TRANSITIONS.get(ver.status, set()):
            raise FinanceConflictError(f"Cannot submit policy version from status '{ver.status}'.")

        prev = ver.status
        ver.status = "pending_approval"
        ver.submitted_at = utc_now()
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="policy_version_submitted",
            entity_type="policy_version",
            entity_id=ver.id,
            previous_state={"status": prev},
            new_state={"status": ver.status},
            reason=ver.change_justification,
        )
        db.commit()
        rules = db.query(PolicyRule).filter(PolicyRule.policy_version_id == ver.id).all()
        return ver.to_dict(rules=[r.to_dict() for r in rules])

    def approve_policy_version(
        self,
        db: Session,
        actor: ResolvedActor,
        version_id: str,
        comments: str,
    ) -> dict[str, Any]:
        check_approval_matrix(db, actor, "policy_approval", "controller_approval", 0)
        ver = db.query(PolicyVersion).filter(PolicyVersion.id == version_id).first()
        if not ver:
            raise FinanceNotFoundError("Policy version not found.")
        if ver.status != "pending_approval":
            raise FinanceConflictError(f"Policy version must be in 'pending_approval' state to approve (current: '{ver.status}').")

        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors={"policy_author": ver.author_id},
            current_duty_label="policy_approver",
            entity_type="policy_version",
            entity_id=ver.id,
        )

        prev_status = ver.status
        ver.status = "active"
        ver.approver_id = actor.id
        ver.approved_at = utc_now()
        ver.ratification_status = "ratified_by_controller"

        policy = db.query(FinancialPolicy).filter(FinancialPolicy.id == ver.policy_id).first()
        if policy:
            policy.current_version_id = ver.id
            policy.updated_at = utc_now()

        db.add(
            ApprovalHistory(
                entity_type="policy_version",
                entity_id=ver.id,
                approver_id=actor.id,
                approver_role=actor.primary_role,
                approval_stage="controller_approval",
                decision="approved",
                approved_amount_minor=ver.aggregate_monthly_limit_minor,
                comments=comments or "Approved and activated policy version.",
                previous_status=prev_status,
                resulting_status="active",
                decided_at=utc_now(),
            )
        )
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="policy_version_approved",
            entity_type="policy_version",
            entity_id=ver.id,
            previous_state={"status": prev_status},
            new_state={"status": "active", "version_number": ver.version_number},
            reason=comments,
        )
        db.commit()
        rules = db.query(PolicyRule).filter(PolicyRule.policy_version_id == ver.id).all()
        return ver.to_dict(rules=[r.to_dict() for r in rules])

    def retire_policy_version(self, db: Session, actor: ResolvedActor, version_id: str, reason: str) -> dict[str, Any]:
        require_roles(actor, "finance_controller", action_desc="retire a policy version")
        ver = db.query(PolicyVersion).filter(PolicyVersion.id == version_id).first()
        if not ver:
            raise FinanceNotFoundError("Policy version not found.")
        if "retired" not in VALID_POLICY_TRANSITIONS.get(ver.status, set()):
            raise FinanceConflictError(f"Cannot retire policy version from status '{ver.status}'.")
        prev = ver.status
        ver.status = "retired"
        ver.retired_at = utc_now()
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="policy_version_retired",
            entity_type="policy_version",
            entity_id=ver.id,
            previous_state={"status": prev},
            new_state={"status": "retired"},
            reason=reason,
        )
        db.commit()
        rules = db.query(PolicyRule).filter(PolicyRule.policy_version_id == ver.id).all()
        return ver.to_dict(rules=[r.to_dict() for r in rules])

    # ==========================================================================
    # Exception Request Management
    # ==========================================================================

    def create_exception_request(
        self,
        db: Session,
        actor: ResolvedActor,
        employee_id: str,
        target_entity_type: str,
        target_entity_id: Optional[str],
        reporting_period: str,
        violated_rule_code: str,
        exception_type: str,
        requested_amount_minor: int,
        business_justification: str,
        supporting_document_ids: Optional[list[str]] = None,
        policy_version_id: Optional[str] = None,
        submit_immediately: bool = True,
    ) -> dict[str, Any]:
        if not business_justification or len(business_justification.strip()) < 5:
            raise FinanceValidationError("Business justification is required for an exception request.")
        if actor.id != employee_id and not actor.has_role("supervisor", "finance_reviewer", "finance_controller"):
            raise FinanceForbiddenError("Employees may only request exceptions for their own transactions.")

        pv = (
            db.query(PolicyVersion).filter(PolicyVersion.id == policy_version_id).first()
            if policy_version_id
            else self.get_applicable_policy_version(db, "expense_liquidation", f"{reporting_period}-01")
        )
        if not pv:
            raise FinanceNotFoundError("Applicable policy version not found.")

        rule = (
            db.query(PolicyRule)
            .filter(
                PolicyRule.policy_version_id == pv.id,
                PolicyRule.rule_code == violated_rule_code,
            )
            .first()
        )
        if rule and (not rule.is_exception_allowed or rule.enforcement_mode == "hard_block"):
            raise FinanceValidationError(
                f"Policy rule '{violated_rule_code}' is configured as 'hard_block' and does not permit exceptions."
            )

        exc_number = next_sequence_number(db, "EXR")
        initial_status = "submitted" if submit_immediately else "draft"
        exc = ExceptionRequest(
            id=str(uuid.uuid4()),
            exception_number=exc_number,
            employee_id=employee_id,
            requester_id=actor.id,
            target_entity_type=target_entity_type,
            target_entity_id=target_entity_id,
            reporting_period=reporting_period[:7],
            policy_version_id=pv.id,
            violated_rule_code=violated_rule_code,
            exception_type=exception_type,
            requested_amount_minor=max(0, int(requested_amount_minor)),
            currency="PHP",
            business_justification=business_justification.strip(),
            supporting_document_ids_json=json.dumps(supporting_document_ids or []),
            status=initial_status,
            submitted_at=utc_now() if submit_immediately else None,
            created_at=utc_now(),
            updated_at=utc_now(),
        )
        db.add(exc)
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="exception_request_created",
            entity_type="exception_request",
            entity_id=exc.id,
            new_state={
                "exception_number": exc_number,
                "violated_rule_code": violated_rule_code,
                "requested_amount_minor": requested_amount_minor,
                "status": initial_status,
            },
            reason=business_justification.strip(),
        )
        db.commit()
        db.refresh(exc)
        return exc.to_dict()

    def decide_exception_request(
        self,
        db: Session,
        actor: ResolvedActor,
        exception_id: str,
        decision: str,  # approved | rejected
        decision_reason: str,
        approved_max_amount_minor: Optional[int] = None,
        expires_at_iso: Optional[str] = None,
    ) -> dict[str, Any]:
        if decision not in ("approved", "rejected"):
            raise FinanceValidationError("Decision must be 'approved' or 'rejected'.")
        if not decision_reason or len(decision_reason.strip()) < 3:
            raise FinanceValidationError("Decision reason is required.")

        exc = db.query(ExceptionRequest).filter(ExceptionRequest.id == exception_id).first()
        if not exc:
            raise FinanceNotFoundError("Exception request not found.")
        if decision not in VALID_EXCEPTION_TRANSITIONS.get(exc.status, set()):
            raise FinanceConflictError(f"Cannot transition exception request from '{exc.status}' to '{decision}'.")

        check_approval_matrix(
            db=db,
            actor=actor,
            workflow="exception_request",
            stage="controller_approval",
            amount_minor=int(exc.requested_amount_minor or 0),
        )
        assert_distinct_actors(
            db=db,
            actor=actor,
            prior_actors={"exception_requester": exc.requester_id, "beneficiary_employee": exc.employee_id},
            current_duty_label="exception_approver",
            entity_type="exception_request",
            entity_id=exc.id,
        )

        prev_status = exc.status
        exc.status = decision
        exc.approver_id = actor.id
        exc.decision_reason = decision_reason.strip()
        exc.decided_at = utc_now()
        if decision == "approved":
            exc.approved_max_amount_minor = (
                int(approved_max_amount_minor)
                if approved_max_amount_minor is not None
                else int(exc.requested_amount_minor)
            )
            if expires_at_iso:
                try:
                    dt = datetime.fromisoformat(expires_at_iso.replace("Z", "+00:00"))
                    if dt.tzinfo is None:
                        dt = dt.replace(tzinfo=timezone.utc)
                    exc.expires_at = dt
                except Exception as e:
                    raise FinanceValidationError(f"Invalid expires_at timestamp: {e}")

        db.add(
            ApprovalHistory(
                entity_type="exception_request",
                entity_id=exc.id,
                approver_id=actor.id,
                approver_role=actor.primary_role,
                approval_stage="controller_approval",
                decision=decision,
                approved_amount_minor=exc.approved_max_amount_minor,
                comments=decision_reason.strip(),
                previous_status=prev_status,
                resulting_status=decision,
                decided_at=utc_now(),
            )
        )
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action=f"exception_request_{decision}",
            entity_type="exception_request",
            entity_id=exc.id,
            previous_state={"status": prev_status},
            new_state={"status": decision, "approved_max_amount_minor": exc.approved_max_amount_minor},
            reason=decision_reason.strip(),
        )
        db.commit()
        db.refresh(exc)
        return exc.to_dict()

    def _find_valid_exception(
        self,
        db: Session,
        employee_id: str,
        reporting_period: str,
        rule_code: str,
        target_entity_id: Optional[str],
        required_amount_minor: int = 0,
    ) -> Optional[ExceptionRequest]:
        candidates = (
            db.query(ExceptionRequest)
            .filter(
                ExceptionRequest.employee_id == employee_id,
                ExceptionRequest.reporting_period == reporting_period[:7],
                ExceptionRequest.violated_rule_code == rule_code,
                ExceptionRequest.status == "approved",
            )
            .order_by(ExceptionRequest.decided_at.desc())
            .all()
        )
        now = utc_now()
        for exc in candidates:
            if exc.expires_at:
                exp_dt = exc.expires_at
                if exp_dt.tzinfo is None:
                    exp_dt = exp_dt.replace(tzinfo=timezone.utc)
                if exp_dt < now:
                    exc.status = "expired"
                    db.flush()
                    continue
            if exc.target_entity_id and target_entity_id and exc.target_entity_id != target_entity_id:
                continue
            if required_amount_minor > 0 and int(exc.approved_max_amount_minor or 0) < required_amount_minor:
                continue
            return exc
        return None

    # ==========================================================================
    # Structured Policy Evaluation (Expense Report & Cash Advance)
    # ==========================================================================

    def evaluate_expense_report(
        self,
        db: Session,
        report: ExpenseReport,
        lines: list[ExpenseLine],
        policy_version: Optional[PolicyVersion] = None,
        consume_slot_and_exceptions: bool = False,
    ) -> dict[str, Any]:
        pv = policy_version or self.get_applicable_policy_version(
            db, "expense_liquidation", f"{report.reporting_period}-01"
        )
        rules = {
            r.rule_code: r
            for r in db.query(PolicyRule).filter(PolicyRule.policy_version_id == pv.id).all()
        }
        eligible_cats = set(json.loads(pv.eligible_categories_json or "[]"))

        results: list[dict[str, Any]] = []
        consumed_exceptions: list[ExceptionRequest] = []
        any_blocked = False
        any_exception_used = False

        def add_rule_outcome(
            rule_code: str,
            passed: bool,
            pass_msg: str,
            fail_msg: str,
            required_amount_minor: int = 0,
            is_warn_only: bool = False,
        ):
            nonlocal any_blocked, any_exception_used
            rule_obj = rules.get(rule_code)
            severity = rule_obj.severity if rule_obj else ("flag" if is_warn_only else "block")
            enf_mode = rule_obj.enforcement_mode if rule_obj else ("warn_only" if is_warn_only else "exception_allowed")
            exc_allowed = bool(rule_obj.is_exception_allowed) if rule_obj else (not is_warn_only)
            if enf_mode == "hard_block":
                exc_allowed = False

            if passed:
                results.append({
                    "rule_id": rule_code,
                    "severity": severity,
                    "enforcement_mode": enf_mode,
                    "outcome": "passed",
                    "message": pass_msg,
                    "is_exception_allowed": exc_allowed,
                    "exception_id": None,
                })
                return

            if enf_mode == "warn_only" or severity == "flag":
                results.append({
                    "rule_id": rule_code,
                    "severity": severity,
                    "enforcement_mode": enf_mode,
                    "outcome": "warning",
                    "message": fail_msg,
                    "is_exception_allowed": exc_allowed,
                    "exception_id": None,
                })
                return

            if exc_allowed:
                exc = self._find_valid_exception(
                    db=db,
                    employee_id=report.employee_id,
                    reporting_period=report.reporting_period,
                    rule_code=rule_code,
                    target_entity_id=report.id,
                    required_amount_minor=required_amount_minor,
                )
                if exc:
                    any_exception_used = True
                    consumed_exceptions.append(exc)
                    results.append({
                        "rule_id": rule_code,
                        "severity": severity,
                        "enforcement_mode": enf_mode,
                        "outcome": "overridden_by_exception",
                        "message": f"{fail_msg} [Overridden by authorized Exception {exc.exception_number}]",
                        "is_exception_allowed": True,
                        "exception_id": exc.id,
                    })
                    return

            any_blocked = True
            results.append({
                "rule_id": rule_code,
                "severity": severity,
                "enforcement_mode": enf_mode,
                "outcome": "violated",
                "message": fail_msg,
                "is_exception_allowed": exc_allowed,
                "exception_id": None,
            })

        # 1. Check Eligible Categories
        invalid_cats = [l.expense_category for l in lines if eligible_cats and l.expense_category not in eligible_cats]
        add_rule_outcome(
            rule_code="ELIGIBLE_CATEGORY",
            passed=(len(invalid_cats) == 0 and len(lines) > 0),
            pass_msg="All expense lines belong to eligible categories.",
            fail_msg=(
                f"Ineligible expense categories found: {', '.join(sorted(set(invalid_cats)))}"
                if invalid_cats
                else "At least one expense line is required."
            ),
        )

        # 2. Check Consolidated Receipt PDF
        has_pdf = False
        if report.consolidated_receipt_doc_id:
            doc = db.query(DocumentReference).filter(DocumentReference.id == report.consolidated_receipt_doc_id).first()
            has_pdf = doc is not None
        add_rule_outcome(
            rule_code="CONSOLIDATED_RECEIPT_PDF",
            passed=(not pv.require_consolidated_receipt_pdf) or has_pdf,
            pass_msg="Consolidated Official Receipts PDF is attached.",
            fail_msg="Missing required consolidated PDF of Official Receipts.",
        )

        # 3. Check Out-of-Base MFAR & destination
        needs_mfar = bool(report.out_of_base_activity) or any(bool(l.out_of_base) for l in lines)
        has_mfar = False
        if report.mfar_doc_id:
            has_mfar = db.query(DocumentReference).filter(DocumentReference.id == report.mfar_doc_id).first() is not None
        elif all((not l.out_of_base) or l.mfar_document_id for l in lines):
            has_mfar = True
        missing_dest = any(bool(l.out_of_base) and not (l.school_or_destination or "").strip() for l in lines)
        mfar_ok = (not pv.require_mfar_for_out_of_base) or (not needs_mfar) or (has_mfar and not missing_dest)
        add_rule_outcome(
            rule_code="OUT_OF_BASE_MFAR",
            passed=mfar_ok,
            pass_msg="Out-of-base MFAR and destination requirements satisfied.",
            fail_msg="Out-of-base activity requires an attached MFAR document and school/destination on lines.",
        )

        # 4. Check Monthly Report Frequency (excluding current report if it already holds a slot)
        other_active_reports = (
            db.query(ExpenseReport)
            .filter(
                ExpenseReport.employee_id == report.employee_id,
                ExpenseReport.reporting_period == report.reporting_period,
                ExpenseReport.id != report.id,
                ExpenseReport.review_status.notin_(["draft", "cancelled", "rejected"]),
            )
            .all()
        )
        projected_report_count = len(other_active_reports) + 1
        freq_ok = projected_report_count <= int(pv.max_reports_per_period)
        add_rule_outcome(
            rule_code="MONTHLY_REPORT_FREQUENCY",
            passed=freq_ok,
            pass_msg=f"Report count ({projected_report_count}) is within period limit ({pv.max_reports_per_period}).",
            fail_msg=f"Submitting report #{projected_report_count} for period {report.reporting_period} exceeds configured frequency limit of {pv.max_reports_per_period} report(s) per employee per period.",
            required_amount_minor=0,
        )

        # 5. Check Aggregate Monthly Cap across all non-cancelled, non-rejected reports in period
        other_report_ids = [r.id for r in other_active_reports]
        prior_period_claimed_minor = 0
        if other_report_ids:
            prior_lines = (
                db.query(ExpenseLine)
                .filter(ExpenseLine.expense_report_id.in_(other_report_ids))
                .all()
            )
            prior_period_claimed_minor = sum(int(pl.claimed_amount_minor) for pl in prior_lines)

        current_claimed_minor = sum(int(l.claimed_amount_minor) for l in lines)
        aggregate_claimed_minor = prior_period_claimed_minor + current_claimed_minor
        cap_ok = aggregate_claimed_minor <= int(pv.aggregate_monthly_limit_minor)
        add_rule_outcome(
            rule_code="MONTHLY_AGGREGATE_CAP",
            passed=cap_ok,
            pass_msg=(
                f"Aggregate period claimed amount ({aggregate_claimed_minor} centavos) is within policy limit "
                f"({pv.aggregate_monthly_limit_minor} centavos)."
            ),
            fail_msg=(
                f"Aggregate period claimed amount ({aggregate_claimed_minor} centavos across {projected_report_count} report(s)) "
                f"exceeds configured monthly limit of {pv.aggregate_monthly_limit_minor} centavos."
            ),
            required_amount_minor=aggregate_claimed_minor,
        )

        # 6. Duplicate receipt flag check
        dup_lines = [l for l in lines if l.duplicate_warning]
        add_rule_outcome(
            rule_code="DUPLICATE_RECEIPT_CHECK",
            passed=(len(dup_lines) == 0),
            pass_msg="No duplicate receipt warnings detected.",
            fail_msg=f"{len(dup_lines)} expense line(s) flagged for potential duplicate receipt review.",
            is_warn_only=True,
        )

        # Atomic PeriodCounter lock & update when consuming slot on submission
        if consume_slot_and_exceptions and not any_blocked:
            effective_max_reports = int(pv.max_reports_per_period)
            if any(r["rule_id"] == "MONTHLY_REPORT_FREQUENCY" and r["outcome"] == "overridden_by_exception" for r in results):
                effective_max_reports = max(effective_max_reports, projected_report_count)

            effective_max_cap = int(pv.aggregate_monthly_limit_minor)
            for exc in consumed_exceptions:
                if exc.violated_rule_code == "MONTHLY_AGGREGATE_CAP" and exc.approved_max_amount_minor:
                    effective_max_cap = max(effective_max_cap, int(exc.approved_max_amount_minor))

            self._atomic_acquire_period_counter(
                db=db,
                report=report,
                current_claimed_minor=current_claimed_minor,
                prior_period_claimed_minor=prior_period_claimed_minor,
                projected_report_count=projected_report_count,
                effective_max_reports=effective_max_reports,
                effective_max_cap=effective_max_cap,
            )

            for exc in consumed_exceptions:
                exc.status = "consumed"
                exc.consumed_at = utc_now()
                exc.consumed_by_entity_id = report.id
                exc.target_entity_id = report.id

        overall_outcome = (
            "blocked"
            if any_blocked
            else ("approved_with_exception" if any_exception_used else "compliant")
        )
        return {
            "policy_version_id": pv.id,
            "policy_version_number": pv.version_number,
            "overall_outcome": overall_outcome,
            "is_compliant": not any_blocked,
            "aggregate_period_claimed_minor": aggregate_claimed_minor,
            "current_report_claimed_minor": current_claimed_minor,
            "policy_limit_minor": pv.aggregate_monthly_limit_minor,
            "results": results,
            "consumed_exception_ids": [e.id for e in consumed_exceptions],
        }

    def _atomic_acquire_period_counter(
        self,
        db: Session,
        report: ExpenseReport,
        current_claimed_minor: int,
        prior_period_claimed_minor: int,
        projected_report_count: int,
        effective_max_reports: int,
        effective_max_cap: int,
    ) -> None:
        counter = (
            db.query(PeriodCounter)
            .filter(
                PeriodCounter.employee_id == report.employee_id,
                PeriodCounter.period_key == report.reporting_period,
                PeriodCounter.rule_code == "MONTHLY_AGGREGATE_CAP",
            )
            .first()
        )
        if not counter:
            try:
                counter = PeriodCounter(
                    id=str(uuid.uuid4()),
                    employee_id=report.employee_id,
                    period_key=report.reporting_period,
                    rule_code="MONTHLY_AGGREGATE_CAP",
                    submission_count=0,
                    aggregate_amount_minor=0,
                    active_report_id=None,
                    updated_at=utc_now(),
                )
                db.add(counter)
                db.flush()
            except IntegrityError:
                db.rollback()
                raise FinanceConflictError("Concurrent submission detected while initializing period counter.")

        # If report already consumed a slot (e.g. resubmitting from returned_for_correction),
        # keep the slot count and update the aggregate amount conditionally
        if report.slot_consumed:
            new_total = prior_period_claimed_minor + current_claimed_minor
            stmt = (
                update(PeriodCounter)
                .where(
                    PeriodCounter.id == counter.id,
                    PeriodCounter.submission_count <= effective_max_reports,
                )
                .values(
                    aggregate_amount_minor=new_total,
                    active_report_id=report.id,
                    updated_at=utc_now(),
                )
            )
            res = db.execute(stmt)
            if res.rowcount != 1 or new_total > effective_max_cap:
                raise FinanceConflictError("Atomic period cap check failed on resubmission.")
        else:
            stmt = (
                update(PeriodCounter)
                .where(
                    PeriodCounter.id == counter.id,
                    PeriodCounter.submission_count < effective_max_reports,
                    (PeriodCounter.aggregate_amount_minor + current_claimed_minor) <= effective_max_cap,
                )
                .values(
                    submission_count=PeriodCounter.submission_count + 1,
                    aggregate_amount_minor=PeriodCounter.aggregate_amount_minor + current_claimed_minor,
                    active_report_id=report.id,
                    updated_at=utc_now(),
                )
            )
            res = db.execute(stmt)
            if res.rowcount != 1:
                raise FinanceConflictError(
                    "Atomic frequency/cap enforcement blocked concurrent or excess period submission."
                )
            report.slot_consumed = 1

    def release_period_slot(
        self,
        db: Session,
        report: ExpenseReport,
        claimed_amount_minor: int,
    ) -> None:
        if not report.slot_consumed:
            return
        counter = (
            db.query(PeriodCounter)
            .filter(
                PeriodCounter.employee_id == report.employee_id,
                PeriodCounter.period_key == report.reporting_period,
                PeriodCounter.rule_code == "MONTHLY_AGGREGATE_CAP",
            )
            .first()
        )
        if counter:
            counter.submission_count = max(0, int(counter.submission_count) - 1)
            counter.aggregate_amount_minor = max(0, int(counter.aggregate_amount_minor) - int(claimed_amount_minor))
            counter.updated_at = utc_now()
        report.slot_consumed = 0
        db.flush()

    def evaluate_cash_advance(
        self,
        db: Session,
        advance: CashAdvance,
        policy_version: Optional[PolicyVersion] = None,
    ) -> dict[str, Any]:
        pv = policy_version or self.get_applicable_policy_version(
            db, "expense_liquidation", advance.date_needed
        )
        results = []
        any_blocked = False

        # Check overdue open advances if block_new_advance_when_overdue is enabled
        today = manila_today_str()
        overdue_advances = (
            db.query(CashAdvance)
            .filter(
                CashAdvance.employee_id == advance.employee_id,
                CashAdvance.id != advance.id,
                CashAdvance.payment_status == "evidence_verified",
                CashAdvance.settlement_status.notin_(["reconciled", "closed"]),
                CashAdvance.liquidation_due_date != None,  # noqa: E711
                CashAdvance.liquidation_due_date < today,
            )
            .all()
        )
        if pv.block_new_advance_when_overdue and overdue_advances:
            exc = self._find_valid_exception(
                db=db,
                employee_id=advance.employee_id,
                reporting_period=advance.date_needed[:7],
                rule_code="OVERDUE_ADVANCE_BLOCK",
                target_entity_id=advance.id,
            )
            if exc:
                results.append({
                    "rule_id": "OVERDUE_ADVANCE_BLOCK",
                    "severity": "block",
                    "enforcement_mode": "exception_allowed",
                    "outcome": "overridden_by_exception",
                    "message": f"Employee has {len(overdue_advances)} overdue advance(s); overridden by Exception {exc.exception_number}.",
                    "is_exception_allowed": True,
                    "exception_id": exc.id,
                })
            else:
                any_blocked = True
                results.append({
                    "rule_id": "OVERDUE_ADVANCE_BLOCK",
                    "severity": "block",
                    "enforcement_mode": "exception_allowed",
                    "outcome": "violated",
                    "message": f"Employee has {len(overdue_advances)} overdue unliquidated advance(s).",
                    "is_exception_allowed": True,
                    "exception_id": None,
                })
        else:
            results.append({
                "rule_id": "OVERDUE_ADVANCE_BLOCK",
                "severity": "block",
                "enforcement_mode": "exception_allowed",
                "outcome": "passed",
                "message": "No blocking overdue advances found.",
                "is_exception_allowed": True,
                "exception_id": None,
            })

        # Check optional advance cap if configured (Note: PHP 15,000 report cap is NOT assumed to be cash advance cap)
        if pv.max_advance_amount_minor is not None and int(advance.requested_amount_minor) > int(pv.max_advance_amount_minor):
            any_blocked = True
            results.append({
                "rule_id": "MAX_ADVANCE_CAP",
                "severity": "block",
                "enforcement_mode": "exception_allowed",
                "outcome": "violated",
                "message": f"Requested advance ({advance.requested_amount_minor}) exceeds configured advance cap ({pv.max_advance_amount_minor}).",
                "is_exception_allowed": True,
                "exception_id": None,
            })
        else:
            results.append({
                "rule_id": "MAX_ADVANCE_CAP",
                "severity": "info",
                "enforcement_mode": "warn_only",
                "outcome": "passed",
                "message": "Advance amount satisfies policy configuration (separate from monthly expense report cap).",
                "is_exception_allowed": True,
                "exception_id": None,
            })

        return {
            "policy_version_id": pv.id,
            "policy_version_number": pv.version_number,
            "overall_outcome": "blocked" if any_blocked else "compliant",
            "is_compliant": not any_blocked,
            "results": results,
        }


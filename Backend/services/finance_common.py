import os
import json
import uuid
import hashlib
from pathlib import Path
from datetime import datetime, timezone, date, timedelta
from zoneinfo import ZoneInfo
from typing import Any, Optional

from sqlalchemy.orm import Session
from finance_models import (
    FinanceUser,
    RoleAssignment,
    ApprovalMatrix,
    NumberSequence,
    HolidayCalendar,
    AuditEvent,
    DocumentReference,
    utc_now,
)

MANILA_TZ = ZoneInfo("Asia/Manila")
UPLOAD_DIR = Path(__file__).resolve().parent.parent / "uploads"
MAX_UPLOAD_BYTES = 10 * 1024 * 1024  # 10 MB


# ==============================================================================
# Typed Domain Exceptions (Mapped to HTTP 401 / 403 / 404 / 409 / 422)
# ==============================================================================

class FinanceDomainError(Exception):
    status_code: int = 400

    def __init__(self, message: str, details: Optional[dict[str, Any]] = None):
        super().__init__(message)
        self.message = message
        self.details = details or {}


class FinanceAuthError(FinanceDomainError):
    status_code = 401


class FinanceForbiddenError(FinanceDomainError):
    status_code = 403


class FinanceNotFoundError(FinanceDomainError):
    status_code = 404


class FinanceConflictError(FinanceDomainError):
    status_code = 409


class FinanceValidationError(FinanceDomainError):
    status_code = 422


# ==============================================================================
# Timezone & Business Day Helpers (Asia/Manila)
# ==============================================================================

def manila_now() -> datetime:
    return datetime.now(MANILA_TZ)


def manila_today_str() -> str:
    return manila_now().strftime("%Y-%m-%d")


def manila_period_str(dt_or_str: Optional[str | datetime] = None) -> str:
    if dt_or_str is None:
        return manila_now().strftime("%Y-%m")
    if isinstance(dt_or_str, datetime):
        return dt_or_str.astimezone(MANILA_TZ).strftime("%Y-%m")
    return str(dt_or_str)[:7]


def add_business_days(db: Session, start_date_str: str, business_days: int) -> str:
    try:
        curr = date.fromisoformat(start_date_str[:10])
    except Exception:
        curr = manila_now().date()
    holidays = {
        h.holiday_date for h in db.query(HolidayCalendar).all()
    }
    remaining = max(0, int(business_days))
    while remaining > 0:
        curr += timedelta(days=1)
        if curr.weekday() < 5 and curr.isoformat() not in holidays:
            remaining -= 1
    return curr.isoformat()


def compute_liquidation_due_date(
    db: Session,
    activity_end_or_needed: str,
    deadline_day_of_next_month: Optional[int] = 2,
) -> str:
    try:
        base_date = date.fromisoformat(activity_end_or_needed[:10])
    except Exception:
        base_date = manila_now().date()

    if deadline_day_of_next_month and 1 <= int(deadline_day_of_next_month) <= 28:
        year = base_date.year + (1 if base_date.month == 12 else 0)
        month = 1 if base_date.month == 12 else base_date.month + 1
        return date(year, month, int(deadline_day_of_next_month)).isoformat()
    return add_business_days(db, base_date.isoformat(), 5)


# ==============================================================================
# Atomic Sequence Number Generator
# ==============================================================================

def next_sequence_number(db: Session, prefix: str) -> str:
    year_prefix = f"{prefix}-{manila_now().strftime('%Y')}" if "-" not in prefix else prefix
    seq = db.query(NumberSequence).filter(NumberSequence.prefix == year_prefix).first()
    if not seq:
        seq = NumberSequence(prefix=year_prefix, current_value=1, updated_at=utc_now())
        db.add(seq)
        db.flush()
        val = 1
    else:
        seq.current_value = int(seq.current_value) + 1
        seq.updated_at = utc_now()
        db.flush()
        val = int(seq.current_value)
    return f"{year_prefix}-{val:04d}"


# ==============================================================================
# Append-Only Hash-Chained Audit Trail
# ==============================================================================

def _canonical_json(val: Any) -> Optional[str]:
    if val is None:
        return None
    if isinstance(val, str):
        return val
    return json.dumps(val, sort_keys=True, default=str)


def record_audit_event(
    db: Session,
    actor_id: str,
    actor_role: str,
    action: str,
    entity_type: str,
    entity_id: str,
    previous_state: Any = None,
    new_state: Any = None,
    reason: Optional[str] = None,
    correlation_id: Optional[str] = None,
    evidence_ref: Optional[str] = None,
) -> AuditEvent:
    last_event = (
        db.query(AuditEvent)
        .order_by(AuditEvent.sequence_no.desc())
        .first()
    )
    seq_no = (int(last_event.sequence_no) + 1) if last_event else 1
    prev_hash = last_event.record_hash if last_event else ("0" * 64)

    occurred = utc_now()
    occurred_iso = occurred.isoformat()
    prev_str = _canonical_json(previous_state)
    new_str = _canonical_json(new_state)

    payload_to_hash = "|".join([
        str(seq_no),
        actor_id,
        actor_role,
        action,
        entity_type,
        entity_id,
        prev_str or "",
        new_str or "",
        reason or "",
        correlation_id or "",
        evidence_ref or "",
        occurred_iso,
        prev_hash,
    ])
    record_hash = hashlib.sha256(payload_to_hash.encode("utf-8")).hexdigest()

    ev = AuditEvent(
        id=str(uuid.uuid4()),
        sequence_no=seq_no,
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        entity_type=entity_type,
        entity_id=entity_id,
        previous_state=prev_str,
        new_state=new_str,
        reason=reason,
        correlation_id=correlation_id,
        evidence_ref=evidence_ref,
        occurred_at=occurred,
        prev_hash=prev_hash,
        record_hash=record_hash,
    )
    db.add(ev)
    db.flush()
    return ev


def verify_audit_chain(db: Session) -> dict[str, Any]:
    events = db.query(AuditEvent).order_by(AuditEvent.sequence_no.asc()).all()
    expected_prev = "0" * 64
    for idx, ev in enumerate(events, start=1):
        if int(ev.sequence_no) != idx:
            return {"valid": False, "broken_at_sequence": ev.sequence_no, "reason": "sequence_gap"}
        if ev.prev_hash != expected_prev:
            return {"valid": False, "broken_at_sequence": ev.sequence_no, "reason": "prev_hash_mismatch"}
        expected_prev = ev.record_hash
    return {"valid": True, "event_count": len(events), "head_hash": expected_prev}


# ==============================================================================
# Server-Bound Identity, RBAC & Separation of Duties (SoD)
# ==============================================================================

class ResolvedActor:
    def __init__(self, user: FinanceUser, roles: list[RoleAssignment], act_as_origin: Optional[str] = None):
        self.user = user
        self.role_assignments = roles
        self.role_names = sorted({r.role for r in roles if r.is_active})
        self.act_as_origin = act_as_origin

    @property
    def id(self) -> str:
        return self.user.id

    @property
    def primary_role(self) -> str:
        return ",".join(self.role_names) if self.role_names else "none"

    def has_role(self, *allowed_roles: str) -> bool:
        return any(r in self.role_names for r in allowed_roles)

    def can_access_department(self, department_id: str) -> bool:
        if self.has_role("finance_reviewer", "finance_controller", "auditor", "maker", "verifier", "bank_approver", "evidence_verifier"):
            return True
        for ra in self.role_assignments:
            if ra.is_active and ra.department_scope in ("*", department_id):
                return True
        return self.user.department_id == department_id

    def to_dict(self) -> dict[str, Any]:
        d = self.user.to_dict(roles=self.role_names)
        d["act_as_origin"] = self.act_as_origin
        d["auth_mode"] = os.getenv("FINANCE_AUTH_MODE", "dev").lower()
        return d


def resolve_actor(
    db: Session,
    x_user_id: Optional[str] = None,
    x_user_role: Optional[str] = None,
    x_act_as_user_id: Optional[str] = None,
) -> ResolvedActor:
    auth_mode = os.getenv("FINANCE_AUTH_MODE", "dev").lower()

    # 1. Handle X-Act-As-User-Id (strictly dev-only, audit-logged)
    effective_user_id = x_user_id
    act_as_origin = None
    if x_act_as_user_id:
        if auth_mode not in ("dev", "test"):
            record_audit_event(
                db=db,
                actor_id=x_user_id or "anonymous",
                actor_role="untrusted",
                action="act_as_blocked_in_prod",
                entity_type="auth",
                entity_id=x_act_as_user_id,
                reason="Attempted to use X-Act-As-User-Id outside dev mode",
            )
            db.commit()
            raise FinanceForbiddenError("Role switching (act-as) is disabled in production mode.")
        act_as_origin = x_user_id or "dev_console"
        effective_user_id = x_act_as_user_id

    if not effective_user_id:
        if auth_mode in ("dev", "test"):
            effective_user_id = "emp_field_01"  # Least-privilege default in dev
        else:
            raise FinanceAuthError("Missing required X-User-Id authentication header.")

    user = db.query(FinanceUser).filter(FinanceUser.id == effective_user_id).first()
    if not user or user.status != "active":
        raise FinanceAuthError(f"Unknown or inactive finance user '{effective_user_id}'.")

    roles = (
        db.query(RoleAssignment)
        .filter(RoleAssignment.user_id == user.id, RoleAssignment.is_active == 1)
        .all()
    )
    actor = ResolvedActor(user=user, roles=roles, act_as_origin=act_as_origin)

    # 2. Reject client-supplied X-User-Role spoofing attempts (V2 & Rule 1)
    if x_user_role:
        claimed_roles = [r.strip() for r in x_user_role.split(",") if r.strip()]
        if any(cr not in actor.role_names for cr in claimed_roles):
            record_audit_event(
                db=db,
                actor_id=actor.id,
                actor_role=actor.primary_role,
                action="role_spoof_attempt_blocked",
                entity_type="auth",
                entity_id=actor.id,
                previous_state={"server_roles": actor.role_names},
                new_state={"claimed_header_roles": claimed_roles},
                reason="Client-supplied X-User-Role header does not match server-bound role assignments.",
            )
            db.commit()
            raise FinanceForbiddenError(
                "Client-supplied X-User-Role header is not trusted and conflicts with server-assigned roles."
            )

    if act_as_origin:
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="dev_act_as_used",
            entity_type="auth",
            entity_id=actor.id,
            reason=f"Dev act-as invoked by {act_as_origin}",
        )

    return actor


def require_roles(actor: ResolvedActor, *allowed_roles: str, action_desc: str = "perform this action") -> None:
    if actor.has_role("system_admin") and not any(r == "system_admin" for r in allowed_roles):
        raise FinanceForbiddenError(
            f"System Administrator role cannot bypass financial controls to {action_desc}."
        )
    if not actor.has_role(*allowed_roles):
        raise FinanceForbiddenError(
            f"User '{actor.id}' (roles: {actor.primary_role}) is not authorized to {action_desc}. Required one of: {', '.join(allowed_roles)}."
        )


def check_approval_matrix(
    db: Session,
    actor: ResolvedActor,
    workflow: str,
    stage: str,
    amount_minor: int,
    department_id: str = "*",
) -> ApprovalMatrix:
    if actor.has_role("system_admin") and not actor.has_role("supervisor", "finance_reviewer", "bank_approver", "finance_controller"):
        raise FinanceForbiddenError("System Administrator has no financial approval authority.")

    entries = (
        db.query(ApprovalMatrix)
        .filter(
            ApprovalMatrix.workflow == workflow,
            ApprovalMatrix.stage == stage,
            ApprovalMatrix.is_active == 1,
        )
        .all()
    )
    matching_role_entries = [
        e for e in entries
        if e.role in actor.role_names and e.department_scope in ("*", department_id)
    ]
    if not matching_role_entries:
        raise FinanceForbiddenError(
            f"User '{actor.id}' (roles: {actor.primary_role}) has no approval authority for workflow '{workflow}' stage '{stage}'."
        )

    for entry in matching_role_entries:
        if int(entry.min_amount_minor) <= int(amount_minor) <= int(entry.max_amount_minor):
            return entry

    max_allowed = max(int(e.max_amount_minor) for e in matching_role_entries)
    raise FinanceForbiddenError(
        f"Amount {amount_minor} centavos exceeds maximum approval limit ({max_allowed} centavos) for user '{actor.id}' in '{workflow}/{stage}'."
    )


def assert_distinct_actors(
    db: Session,
    actor: ResolvedActor,
    prior_actors: dict[str, Optional[str]],
    current_duty_label: str,
    entity_type: str,
    entity_id: str,
) -> None:
    for duty, uid in prior_actors.items():
        if uid and uid == actor.id:
            record_audit_event(
                db=db,
                actor_id=actor.id,
                actor_role=actor.primary_role,
                action="sod_violation_blocked",
                entity_type=entity_type,
                entity_id=entity_id,
                reason=f"Separation of Duties violation: user '{actor.id}' cannot act as both '{duty}' and '{current_duty_label}' on the same transaction.",
            )
            db.commit()
            raise FinanceForbiddenError(
                f"Separation of Duties violation: user '{actor.id}' already acted as '{duty}' and cannot also act as '{current_duty_label}' for this transaction."
            )


# ==============================================================================
# Secure Document Evidence Storage
# ==============================================================================

ALLOWED_SIGNATURES = {
    "application/pdf": [b"%PDF-"],
    "image/png": [b"\x89PNG\r\n\x1a\n"],
    "image/jpeg": [b"\xff\xd8\xff"],
}

RESTRICTED_DOC_TYPES = {"payroll_summary_restricted", "vendor_kyc"}


def detect_and_validate_mime(content: bytes, filename: str, declared_content_type: Optional[str] = None) -> str:
    if not content:
        raise FinanceValidationError("Uploaded document file is empty.")
    if len(content) > MAX_UPLOAD_BYTES:
        raise FinanceValidationError(f"Uploaded file exceeds maximum size limit of {MAX_UPLOAD_BYTES} bytes.")

    for mime, sigs in ALLOWED_SIGNATURES.items():
        for sig in sigs:
            if content.startswith(sig):
                return mime

    raise FinanceValidationError(
        "Unsupported or invalid file format. Only valid PDF (%PDF-), PNG, or JPEG evidence files are permitted."
    )


def store_document_evidence(
    db: Session,
    actor: ResolvedActor,
    file_bytes: bytes,
    original_filename: str,
    document_type: str,
    department_id: Optional[str] = None,
    entity_type: Optional[str] = None,
    entity_id: Optional[str] = None,
    is_restricted: bool = False,
) -> DocumentReference:
    mime_type = detect_and_validate_mime(file_bytes, original_filename)
    sha256_hash = hashlib.sha256(file_bytes).hexdigest()

    UPLOAD_DIR.mkdir(parents=True, exist_ok=True)
    ext = ".pdf" if mime_type == "application/pdf" else (".png" if mime_type == "image/png" else ".jpg")
    stored_filename = f"{uuid.uuid4().hex}{ext}"
    file_path = UPLOAD_DIR / stored_filename
    file_path.write_bytes(file_bytes)

    restricted_flag = 1 if (is_restricted or document_type in RESTRICTED_DOC_TYPES) else 0
    doc_number = next_sequence_number(db, "DOC")
    doc = DocumentReference(
        id=str(uuid.uuid4()),
        document_number=doc_number,
        uploader_id=actor.id,
        department_id=department_id or actor.user.department_id,
        document_type=document_type,
        entity_type=entity_type,
        entity_id=entity_id,
        original_filename=original_filename,
        stored_filename=stored_filename,
        mime_type=mime_type,
        size_bytes=len(file_bytes),
        sha256_hash=sha256_hash,
        is_restricted=restricted_flag,
        retention_status="active",
        created_at=utc_now(),
    )
    db.add(doc)
    record_audit_event(
        db=db,
        actor_id=actor.id,
        actor_role=actor.primary_role,
        action="document_uploaded",
        entity_type="document_reference",
        entity_id=doc.id,
        new_state={"document_number": doc_number, "document_type": document_type, "sha256": sha256_hash},
    )
    db.commit()
    db.refresh(doc)
    return doc


def get_document_for_download(db: Session, actor: ResolvedActor, doc_id: str) -> tuple[DocumentReference, Path]:
    doc = db.query(DocumentReference).filter(DocumentReference.id == doc_id).first()
    if not doc:
        raise FinanceNotFoundError("Document reference not found.")

    # Enforce access control on documents
    if doc.is_restricted:
        if not actor.has_role("finance_reviewer", "finance_controller", "bank_approver", "auditor") and doc.uploader_id != actor.id:
            record_audit_event(
                db=db,
                actor_id=actor.id,
                actor_role=actor.primary_role,
                action="unauthorized_document_access_blocked",
                entity_type="document_reference",
                entity_id=doc.id,
                reason="Attempted to access restricted document without authorization",
            )
            db.commit()
            raise FinanceForbiddenError("You are not authorized to access this restricted financial document.")
    else:
        # Non-restricted documents: uploader, same department supervisor, or finance/auditor roles
        if (
            doc.uploader_id != actor.id
            and not actor.has_role("supervisor", "finance_reviewer", "finance_controller", "maker", "verifier", "bank_approver", "evidence_verifier", "auditor")
        ):
            record_audit_event(
                db=db,
                actor_id=actor.id,
                actor_role=actor.primary_role,
                action="unauthorized_document_access_blocked",
                entity_type="document_reference",
                entity_id=doc.id,
                reason="Employee attempted to access another employee's document",
            )
            db.commit()
            raise FinanceForbiddenError("You are not authorized to access another user's financial document.")

    file_path = UPLOAD_DIR / doc.stored_filename
    if not file_path.exists():
        raise FinanceNotFoundError("Stored document file is missing on disk.")

    if doc.is_restricted:
        record_audit_event(
            db=db,
            actor_id=actor.id,
            actor_role=actor.primary_role,
            action="restricted_document_accessed",
            entity_type="document_reference",
            entity_id=doc.id,
            evidence_ref=doc.document_number,
        )
        db.commit()

    return doc, file_path


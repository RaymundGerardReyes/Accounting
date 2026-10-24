# Backend/webhook_orchestration.py
"""
Authoritative PayMongo & Google Forms Webhook Orchestrator.
Provides:
1. Server-side authoritative Google Forms submission intake (POST /api/v1/integrations/google-forms/submit).
2. Cryptographically verified PayMongo Webhook processing with HMAC-SHA256 (POST /api/v1/webhooks/paymongo).
3. Terminal state protection (a 'paid' order is never overwritten by late failures/expirations).
4. Real-time reverse webhook dispatch to Google Apps Script (doPost) for live Google Sheet sync.
5. SMTP email dispatch on status transitions (paid, failed, expired).
"""

import os
import hmac
import uuid
import json
import hashlib
import smtplib
import urllib.request
from datetime import datetime, timezone
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
from typing import Optional, Dict, Any

from fastapi import APIRouter, Request, HTTPException, Header, BackgroundTasks, Depends, status
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session
from sqlalchemy import text

# Import database session & models
from database import get_db, SessionLocal
from models import Order, PaymentAttempt, WebhookEvent, FulfillmentOutbox
from providers.paymongo import PayMongoProvider
from services.payment_orchestrator import PaymentOrchestrator
from services.receipt_service import format_philippine_phone

router = APIRouter(tags=["Webhook Orchestration"])
orchestrator = PaymentOrchestrator(provider=PayMongoProvider())

# Environment Configuration
PAYMONGO_WEBHOOK_SECRET = os.getenv("PAYMONGO_WEBHOOK_SECRET", "")
GOOGLE_FORMS_SECRET = os.getenv("GOOGLE_FORMS_SECRET", "rge_gforms_live_sec_89f3a21")
GOOGLE_APPS_SCRIPT_WEBHOOK_URL = os.getenv("GOOGLE_APPS_SCRIPT_WEBHOOK_URL", "")
APP_DOMAIN = (os.getenv("NEXT_PUBLIC_APP_DOMAIN") or os.getenv("APP_DOMAIN") or "https://rge-accounting-3aa707d5f496.herokuapp.com").rstrip("/")

SMTP_HOST = os.getenv("SMTP_HOST", "smtp.gmail.com")
SMTP_PORT = int(os.getenv("SMTP_PORT", "587"))
SMTP_USERNAME = os.getenv("SMTP_USER", "") or os.getenv("SMTP_USERNAME", "")
SMTP_PASSWORD = os.getenv("SMTP_APP_PASSWORD", "") or os.getenv("SMTP_PASSWORD", "")
SMTP_FROM_EMAIL = os.getenv("SMTP_FROM_EMAIL", SMTP_USERNAME or "noreply@acme.com")

# 1. Map all PayMongo and Simulator event types to normalized actions
PAID_EVENTS = {
    "checkout_session.payment.paid",
    "payment.paid",
    "qr.paid",
    "qrph.paid",
    "payment_intent.succeeded",
}
FAILED_EVENTS = {
    "payment.failed",
    "checkout_session.payment.failed",
    "payment_intent.payment_failed",
    "source.failed",
}
EXPIRED_EVENTS = {
    "checkout_session.expired",
    "qr.expired",
    "qrph.expired",
}

TERMINAL_ATTEMPT_STATUSES = {"succeeded", "failed", "expired", "cancelled"}

# Authoritative Server-Side Product Catalog (in PHP centavos - prevents price tampering)
AUTHORITATIVE_PRICE_CATALOG_MINOR: Dict[str, int] = {
    "product a": 25000,   # PHP 250.00
    "product b": 17800,   # PHP 178.00
    "product c": 15600,   # PHP 156.00
    "product d": 19500,   # PHP 195.00
    "product e": 11200,   # PHP 112.00
    "financial accounting & reporting vol. 1": 45000,
    "financial accounting and reporting vol. 1": 45000,
    "financial accounting & reporting": 45000,
    "financial accounting and reporting": 45000,
    "advanced financial accounting & reporting": 52000,
    "advanced financial accounting and reporting": 52000,
    "auditing theory & practice": 48000,
    "auditing theory and practice": 48000,
    "auditing theory": 48000,
    "management advisory services": 42000,
    "philippine tax law & principles": 50000,
    "philippine tax law and principles": 50000,
}


def extract_price_from_text(text: Optional[str]) -> Optional[int]:
    """Extracts centavo amount from strings like '₱450.00', 'PHP 520', '480.00', or catalog lookup."""
    if not text:
        return None
    import re
    # 1. Match explicit Philippine Peso symbol ₱ or PHP first (avoids mistaking 'P2 Reviewer' as ₱2)
    m_sym = re.search(r'(?:[₱]|PHP|Php|php)\s*([\d,]+(?:\.\d{1,2})?)', text)
    if m_sym:
        try:
            val = float(m_sym.group(1).replace(",", ""))
            return int(round(val * 100))
        except ValueError:
            pass

    # 2. Check catalog lookup before bare 'P' matching
    clean_lower = text.lower().strip()
    for book_key, price_cents in AUTHORITATIVE_PRICE_CATALOG_MINOR.items():
        if book_key in clean_lower:
            return price_cents

    # 3. Match bare 'P' followed by numbers with word boundary or separator
    m_p = re.search(r'(?:^|[\s\-\(])P\s*([\d,]+(?:\.\d{1,2})?)', text)
    if m_p:
        try:
            val = float(m_p.group(1).replace(",", ""))
            return int(round(val * 100))
        except ValueError:
            pass

    return None


def resolve_bookstore_fields(
    raw_answers: Optional[Dict[str, Any]],
    notes: Optional[str],
    direct_book: Optional[str] = None,
    direct_student_id: Optional[str] = None,
    direct_instructor: Optional[str] = None,
    reference_code: Optional[str] = None,
) -> tuple[Optional[str], Optional[str], Optional[str]]:
    """
    Extracts book reference, student ID, and instructor credit from direct fields,
    raw_answers map, or notes string.
    """
    import re
    book = (direct_book or "").strip() or None
    student_id = (direct_student_id or "").strip() or None
    instructor = (direct_instructor or "").strip() or None

    # 1. Search raw_answers dictionary
    if raw_answers:
        for k, v in raw_answers.items():
            if not v:
                continue
            k_clean = str(k).strip().lower()
            v_clean = str(v).strip()

            if not book and any(bk in k_clean for bk in ["book", "material", "course material", "textbook"]):
                book = v_clean
            if not student_id and any(sk in k_clean for sk in ["student id", "enrollee id", "student / enrollee", "id number", "student no", "student_id"]):
                student_id = v_clean
            if not instructor and (
                any(ik in k_clean for ik in ["instructor", "professor", "prof.", "faculty"])
                or ("credit" in k_clean and "card" not in k_clean)
            ):
                instructor = v_clean

    # 2. Check reference_code if book still not found
    if not book and reference_code and reference_code.lower() != "product a":
        ref_lower = reference_code.lower()
        if any(bk in ref_lower for bk in AUTHORITATIVE_PRICE_CATALOG_MINOR) or "₱" in reference_code or "php" in ref_lower:
            book = reference_code

    # 3. Search notes text if student_id, instructor, or book are still missing
    if notes:
        if not student_id:
            m_sid = re.search(r'(?:Student\s*(?:/\s*Enrollee\s*)?ID(?:\s*Number)?|Enrollee\s*ID|ID\s*Number|Student\s*(?:No\.?|#|Number))[:=\s]+([^\n|,;]+)', notes, re.IGNORECASE)
            if m_sid:
                student_id = m_sid.group(1).strip()
        if not instructor:
            m_inst = re.search(r'(?:Faculty\s*(?:Credit)?|Instructor(?:\s*Credit)?|Enrolled\s*Instructor(?:\s*to\s*Credit)?|Professor|Prof\.)[:=\s]+([^\n|,;]+)', notes, re.IGNORECASE)
            if m_inst:
                instructor = m_inst.group(1).strip()
        if not book:
            m_bk = re.search(r'(?:Book(?:\s*/\s*Course\s*Material)?|Course\s*Material|Textbook)[:=\s]+([^\n|,;]+)', notes, re.IGNORECASE)
            if m_bk:
                book = m_bk.group(1).strip()

    return book, student_id, instructor


class GoogleFormSubmissionPayload(BaseModel):
    response_id: str = Field(..., description="Unique Google Form response ID")
    customer_name: str = Field(..., description="Customer full name")
    customer_email: str = Field(..., description="Customer email address")
    customer_phone: Optional[str] = Field(None, description="Contact phone number")
    title: Optional[str] = Field(None, description="Order description")
    reference_code: str = Field("Product A", description="Product selection or reference")
    item_reference: Optional[str] = Field(None, description="Alias for reference_code")
    category: str = Field("product_order", description="Order category")
    quantity: int = Field(1, ge=1, description="Quantity")
    amount_minor: Optional[int] = Field(None, description="Optional custom centavos")
    payment_method: Optional[str] = Field("hosted_checkout", description="Payment channel")
    notes: Optional[str] = Field(None, description="Optional remarks")
    sheet_row: Optional[int] = Field(None, description="Row index in sheet")
    sheet_row_index: Optional[int] = Field(None, description="Alias for sheet_row")
    raw_answers: Optional[Dict[str, Any]] = Field(None, description="Raw form answers")
    student_id: Optional[str] = Field(None, description="Student or CPA review enrollee ID number")
    instructor_credit: Optional[str] = Field(None, description="Enrolled faculty instructor to credit")
    book_title: Optional[str] = Field(None, description="Course book or educational material")


# ---------------------------------------------------------------------------
# Helper 1: Verify PayMongo Webhook Signature (Anti-Tampering)
# ---------------------------------------------------------------------------
def verify_paymongo_signature(raw_body: bytes, signature_header: Optional[str] = None, secret: Optional[str] = None, sig_header: Optional[str] = None, webhook_secret: Optional[str] = None) -> bool:
    """
    Verifies the Paymongo-Signature header: t=<timestamp>,te=<test_sig>,li=<live_sig>
    If secret is empty in development/testing, allows sandbox test webhooks.
    """
    actual_sig = sig_header if sig_header is not None else signature_header
    actual_sec = webhook_secret if webhook_secret is not None else secret
    target_secret = (actual_sec if actual_sec is not None else os.getenv("PAYMONGO_WEBHOOK_SECRET", "")).strip()
    if not target_secret:
        return True
    if not actual_sig:
        return False

    parts = {}
    for item in actual_sig.split(","):
        if "=" in item:
            k, v = item.strip().split("=", 1)
            parts[k.strip()] = v.strip()

    timestamp = parts.get("t")
    test_sig = parts.get("te")
    live_sig = parts.get("li")

    if not timestamp:
        return False

    signed_payload = f"{timestamp}.{raw_body.decode('utf-8')}".encode("utf-8")
    expected_sig = hmac.new(
        target_secret.encode("utf-8"),
        msg=signed_payload,
        digestmod=hashlib.sha256,
    ).hexdigest()

    if live_sig and hmac.compare_digest(expected_sig, live_sig):
        return True
    if test_sig and hmac.compare_digest(expected_sig, test_sig):
        return True
    return False


def resolve_attempt_and_order(db: Session, resource: dict, OrderModel=Order, PaymentAttemptModel=PaymentAttempt):
    """
    Resolves PaymentAttempt and Order across checkout_session (cs_...),
    payment (pay_...), and qrph resources using metadata first, then provider_payment_id.
    """
    resource_id = resource.get("id")
    attrs = resource.get("attributes") or {}
    metadata = attrs.get("metadata") or {}

    # Also check nested payments array if this is a checkout_session payload
    payments_list = attrs.get("payments") or []
    if not metadata and payments_list:
        first_payment_attrs = (payments_list[0].get("attributes") if isinstance(payments_list[0], dict) else {}) or {}
        metadata = first_payment_attrs.get("metadata") or {}

    attempt_id = metadata.get("payment_attempt_id")
    order_id = metadata.get("order_id")

    attempt = None
    order = None

    # Priority 1: Exact payment_attempt_id from metadata
    if attempt_id:
        attempt = db.query(PaymentAttemptModel).filter(PaymentAttemptModel.id == attempt_id).first()

    # Priority 2: Match provider_payment_id (works for cs_... and cs_sim_...)
    if not attempt and resource_id:
        attempt = (
            db.query(PaymentAttemptModel)
            .filter(PaymentAttemptModel.provider_payment_id == resource_id)
            .first()
        )

    # Priority 3: Check checkout_session_id inside a payment resource
    checkout_session_id = attrs.get("checkout_session_id") or metadata.get("checkout_session_id")
    if not attempt and checkout_session_id:
        attempt = (
            db.query(PaymentAttemptModel)
            .filter(PaymentAttemptModel.provider_payment_id == checkout_session_id)
            .first()
        )

    # Priority 4: Fallback to latest attempt for order_id
    if not attempt and order_id:
        attempt = (
            db.query(PaymentAttemptModel)
            .filter(PaymentAttemptModel.order_id == order_id)
            .order_by(PaymentAttemptModel.created_at.desc())
            .first()
        )

    if attempt:
        order = db.query(OrderModel).filter(OrderModel.id == attempt.order_id).first()
    elif order_id:
        order = db.query(OrderModel).filter(OrderModel.id == order_id).first()

    return attempt, order


def apply_webhook_transition(
    db: Session,
    event_type: str,
    resource: dict,
    attempt,
    order,
    OutboxModel=FulfillmentOutbox,
    force_override: bool = False,
) -> dict:
    """
    Applies PAID, FAILED, or EXPIRED state transitions to PaymentAttempt and Order.
    Set `force_override=True` in the Testing Simulator to allow re-testing transitions
    even if the attempt was previously marked terminal.
    """
    now = datetime.now(timezone.utc)
    attrs = resource.get("attributes") or {}

    if not attempt:
        return {"transitioned": False, "reason": "payment_attempt_not_found"}

    # Guard terminal states in production webhooks unless force_override is True (Testing API)
    if attempt.status in TERMINAL_ATTEMPT_STATUSES and not force_override:
        return {
            "transitioned": False,
            "reason": f"attempt_already_in_terminal_state:{attempt.status}",
            "attempt_status": attempt.status,
            "order_status": order.status if order else None,
        }

    if event_type in PAID_EVENTS:
        attempt.status = "succeeded"
        attempt.paid_at = now
        attempt.failure_code = None
        attempt.failure_message = None
        attempt.updated_at = now

        if order:
            order.status = "paid"
            order.updated_at = now

            # Enqueue fulfillment outbox record if not already queued
            existing_outbox = (
                db.query(OutboxModel)
                .filter(
                    OutboxModel.payment_attempt_id == attempt.id,
                    OutboxModel.event_type == "order.paid",
                )
                .first()
            )
            if not existing_outbox:
                db.add(
                    OutboxModel(
                        id=str(uuid.uuid4()),
                        order_id=order.id,
                        payment_attempt_id=attempt.id,
                        event_type="order.paid",
                        status="completed",
                        created_at=now,
                        processed_at=now,
                    )
                )

    elif event_type in FAILED_EVENTS:
        attempt.status = "failed"
        attempt.failure_code = attrs.get("failed_code") or "PAYMENT_FAILED"
        attempt.failure_message = attrs.get("failed_message") or "Payment processing failed."
        attempt.updated_at = now

        # Keep order in 'pending' so the user can retry with a new attempt,
        # or update metadata/status as needed by your accounting workflow
        if order and force_override and order.status == "paid":
            order.status = "pending"
            order.updated_at = now

    elif event_type in EXPIRED_EVENTS:
        attempt.status = "expired"
        attempt.expires_at = now
        attempt.failure_code = attrs.get("failed_code") or "SESSION_EXPIRED"
        attempt.failure_message = attrs.get("failed_message") or "Checkout session expired."
        attempt.updated_at = now

        if order and force_override and order.status == "paid":
            order.status = "pending"
            order.updated_at = now

    else:
        return {"transitioned": False, "reason": f"unhandled_event_type:{event_type}"}

    db.commit()
    return {
        "transitioned": True,
        "event_type": event_type,
        "attempt_id": attempt.id,
        "attempt_status": attempt.status,
        "order_id": order.id if order else None,
        "order_status": order.status if order else None,
    }


# ---------------------------------------------------------------------------
# Helper 2: Send SMTP Email Based on PayMongo Webhook Outcome
# ---------------------------------------------------------------------------
def send_webhook_smtp_email(
    customer_email: str,
    customer_name: str,
    order_number: str,
    reference_code: str,
    amount_minor: int,
    outcome: str,  # "paid" | "failed" | "expired" | "checkout_created"
    checkout_url: Optional[str] = None,
    official_receipt_no: Optional[str] = None,
    failure_message: Optional[str] = None,
) -> bool:
    """Sends a real-time SMTP email when PayMongo confirms paid, failed, or expired."""
    user = os.getenv("SMTP_USER", "") or SMTP_USERNAME
    password = os.getenv("SMTP_APP_PASSWORD", "") or SMTP_PASSWORD
    if not customer_email or not user or not password:
        print(f"[SMTP Notice] Skipping email to {customer_email} (SMTP credentials not configured).")
        return False

    amount_php = f"{amount_minor / 100:,.2f}"

    if outcome == "paid":
        receipt_label = f"Official Receipt {official_receipt_no}" if official_receipt_no else "Receipt"
        subject = f"[{receipt_label}] Payment Confirmed - {order_number}"
        badge_color = "#16a34a"
        status_label = "PAYMENT SUCCEEDED (PAID)"
        body_message = (
            f"<p>Thank you! Your payment for <strong>{reference_code}</strong> has been confirmed via PayMongo.</p>"
            f"<p><strong>Official Receipt No:</strong> {official_receipt_no or 'N/A'}</p>"
        )
    elif outcome == "failed":
        subject = f"[Action Required] Payment Failed for Order {order_number}"
        badge_color = "#dc2626"
        status_label = "PAYMENT FAILED"
        reason_str = failure_message or "Your payment provider declined or could not complete the transaction."
        body_message = (
            f"<p>We were unable to process your payment for <strong>{reference_code}</strong>.</p>"
            f"<p><strong>Reason:</strong> {reason_str}</p>"
            + (
                f"<p><a href='{checkout_url}' style='background:#dc2626;color:#fff;padding:10px 20px;"
                f"text-decoration:none;border-radius:6px;display:inline-block;'>Retry Payment</a></p>"
                if checkout_url else ""
            )
        )
    elif outcome == "expired":
        subject = f"[Expired] Payment Session Expired for Order {order_number}"
        badge_color = "#d97706"
        status_label = "CHECKOUT SESSION EXPIRED"
        body_message = (
            f"<p>Your payment link for <strong>{reference_code}</strong> ({order_number}) has expired before completion.</p>"
            "<p>Please submit a new request or contact our cashier desk if you still wish to proceed.</p>"
        )
    else:
        subject = f"[Payment Link] Complete Your Order {order_number}"
        badge_color = "#171717"
        status_label = "AWAITING PAYMENT"
        body_message = (
            f"<p>We received your Google Form registration for <strong>{reference_code}</strong>.</p>"
            f"<p><a href='{checkout_url}' style='background:#d92626;color:#fff;padding:12px 24px;"
            f"text-decoration:none;border-radius:6px;display:inline-block;font-weight:bold;'>"
            f"Pay Now via PayMongo (QR Ph / GCash / Maya / Card)</a></p>"
        )

    html_content = f"""
    <div style="font-family: Arial, sans-serif; max-width: 580px; margin: 0 auto; padding: 24px; border: 1px solid #e5e5e5; border-radius: 8px;">
      <div style="background: {badge_color}; color: #ffffff; padding: 6px 12px; border-radius: 4px; display: inline-block; font-size: 12px; font-weight: bold;">
        {status_label}
      </div>
      <h2 style="color: #171717; margin-top: 16px;">Order {order_number}</h2>
      <p style="color: #404040;">Hello <strong>{customer_name}</strong>,</p>
      {body_message}
      <hr style="border: none; border-top: 1px solid #eaeaea; margin: 20px 0;" />
      <p style="color: #171717; font-size: 15px;"><strong>Amount:</strong> PHP {amount_php}</p>
      <p style="color: #737373; font-size: 12px;">R.G.E Accounting Firm Inc. - Automated Treasury & PayMongo Orchestration</p>
    </div>
    """

    try:
        msg = MIMEMultipart("alternative")
        msg["Subject"] = subject
        msg["From"] = SMTP_FROM_EMAIL
        msg["To"] = customer_email
        msg.attach(MIMEText(html_content, "html"))

        with smtplib.SMTP(SMTP_HOST, SMTP_PORT, timeout=15) as server:
            server.ehlo()
            server.starttls()
            server.login(user, password)
            server.sendmail(SMTP_FROM_EMAIL, [customer_email], msg.as_string())
        return True
    except Exception as exc:
        print(f"[SMTP Error] Failed to send email to {customer_email}: {exc}")
        return False


# ---------------------------------------------------------------------------
# Helper 3: Push Live Status Back to Google Sheets (Reverse Webhook)
# ---------------------------------------------------------------------------
def sync_status_to_google_sheet(
    response_id: str,
    order_number: str,
    payment_status: str,
    official_receipt_no: Optional[str] = None,
    failure_reason: Optional[str] = None,
    webhook_url: Optional[str] = None,
    secret: Optional[str] = None,
) -> bool:
    """Calls the Google Apps Script doPost(e) Web App URL to update the Google Sheet row live."""
    target_url = (webhook_url or os.getenv("GOOGLE_APPS_SCRIPT_WEBHOOK_URL", "")).strip()
    if not target_url:
        return False

    auth_secret = (secret or os.getenv("GOOGLE_FORMS_SECRET", "rge_gforms_live_sec_89f3a21")).strip()
    payload_bytes = json.dumps({
        "secret": auth_secret,
        "response_id": response_id,
        "order_number": order_number,
        "payment_status": payment_status.upper(),
        "official_receipt_no": official_receipt_no or "",
        "failure_reason": failure_reason or "",
        "updated_at": datetime.now(timezone.utc).isoformat(),
    }).encode("utf-8")

    req = urllib.request.Request(
        target_url,
        data=payload_bytes,
        headers={"Content-Type": "application/json", "Accept": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return 200 <= resp.status < 400
    except Exception as exc:
        print(f"[Google Sheet Sync Notice] Failed to callback Apps Script Web App: {exc}")
        return False


# ---------------------------------------------------------------------------
# Endpoint 1: Google Forms Submission Intake
# ---------------------------------------------------------------------------
@router.post("/api/v1/integrations/google-forms/submit", status_code=status.HTTP_201_CREATED)
async def ingest_google_form_submission(
    payload: GoogleFormSubmissionPayload,
    background_tasks: BackgroundTasks,
    x_google_forms_secret: Optional[str] = Header(default=None, alias="X-Google-Forms-Secret"),
    db: Session = Depends(get_db),
):
    """
    Receives form submissions from Google Apps Script, enforces authoritative centavo pricing,
    creates the Order & PayMongo Checkout Session, and returns the checkout link.
    """
    expected_secret = os.getenv("GOOGLE_FORMS_SECRET", "rge_gforms_live_sec_89f3a21").strip()
    if not x_google_forms_secret or not hmac.compare_digest(x_google_forms_secret.strip(), expected_secret):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Missing or invalid X-Google-Forms-Secret header.",
        )

    clean_response_id = payload.response_id.strip()
    if not clean_response_id:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="response_id is required.")

    # Resolve bookstore items, student ID, and instructor credit
    book_ref, student_id, instructor_credit = resolve_bookstore_fields(
        raw_answers=payload.raw_answers,
        notes=payload.notes,
        direct_book=payload.book_title,
        direct_student_id=payload.student_id,
        direct_instructor=payload.instructor_credit,
        reference_code=payload.item_reference or payload.reference_code,
    )

    raw_ref = (payload.item_reference or payload.reference_code or "").strip()
    if (not raw_ref or raw_ref.lower() == "product a") and book_ref:
        ref_code = book_ref
    else:
        ref_code = raw_ref or "Product A"

    qty = max(1, int(payload.quantity))
    target_row = payload.sheet_row if payload.sheet_row is not None else payload.sheet_row_index

    # 1. Atomic Idempotency Check: search if an order for this Google Form response already exists
    existing_orders = db.query(Order).all()
    for o in existing_orders:
        m = o.metadata_dict or {}
        if m.get("google_form_response_id") == clean_response_id or m.get("google_forms_response_id") == clean_response_id:
            latest_attempt = (
                db.query(PaymentAttempt)
                .filter(PaymentAttempt.order_id == o.id)
                .order_by(PaymentAttempt.created_at.desc())
                .first()
            )
            checkout_url = latest_attempt.checkout_url if latest_attempt else ""
            attempt_id = latest_attempt.id if latest_attempt else ""
            provider_session_id = latest_attempt.provider_payment_id if latest_attempt else ""
            amount_php = f"₱{o.amount_minor / 100:,.2f}"

            return {
                "status": o.status,
                "message": "Idempotent match: Order already generated for this Google Form submission.",
                "order_id": o.id,
                "order_number": o.order_number,
                "order_title": o.title,
                "payment_attempt_id": attempt_id,
                "provider_payment_id": provider_session_id,
                "amount_minor": o.amount_minor,
                "amount_php": amount_php,
                "currency": o.currency,
                "checkout_url": checkout_url or "",
                "customer_email": payload.customer_email,
                "customer_name": payload.customer_name,
                "is_duplicate": True,
                "idempotent_replay": True,
            }

    # 2. Format customer phone
    formatted_phone = format_philippine_phone(payload.customer_phone) if payload.customer_phone else None

    # Compute authoritative pricing for bookstore materials if not explicitly provided
    resolved_amount_minor = payload.amount_minor
    if (resolved_amount_minor is None or resolved_amount_minor <= 0) and (book_ref or ref_code):
        unit_price = extract_price_from_text(book_ref or ref_code)
        if unit_price:
            resolved_amount_minor = unit_price * qty

    # 3. Metadata for audit trail & reverse sync
    meta = {
        "source": "google_forms",
        "google_form_response_id": clean_response_id,
        "google_forms_response_id": clean_response_id,
        "sheet_row": target_row,
        "sheet_row_index": target_row,
        "customer_name": payload.customer_name.strip(),
        "customer_email": payload.customer_email.strip().lower(),
        "customer_phone": formatted_phone or payload.customer_phone,
        "reference_code": ref_code,
        "book_title": book_ref or ref_code,
        "student_id": student_id,
        "instructor_credit": instructor_credit,
        "notes": payload.notes,
        "raw_answers": payload.raw_answers,
    }

    credit_tag = f" [Credit: {instructor_credit}]" if instructor_credit else ""
    title = payload.title or f"{ref_code} (Qty: {qty}) - {payload.customer_name.strip()}{credit_tag}"

    # 4. Create Order via PaymentOrchestrator
    try:
        order = orchestrator.create_order(
            db=db,
            title=title,
            category=payload.category if payload.category in ("product_order", "department_invoice") else "product_order",
            reference_code=ref_code,
            amount_minor=resolved_amount_minor,
            quantity=qty,
            user_id="google_forms_agent",
            metadata=meta,
        )
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Failed to create order from form submission: {str(e)}",
        )

    # 5. Initiate PayMongo Checkout Session
    clean_base = APP_DOMAIN
    if not clean_base.startswith("http://") and not clean_base.startswith("https://"):
        clean_base = f"https://{clean_base}"

    success_url = f"{clean_base}/payment/return?orderId={order.id}"
    cancel_url = f"{clean_base}/payment/return?orderId={order.id}&cancelled=true"
    idempotency_key = f"gforms:{clean_response_id}"

    try:
        attempt = await orchestrator.initiate_payment_attempt(
            db=db,
            order_id=order.id,
            idempotency_key=idempotency_key,
            payment_method=payload.payment_method or "hosted_checkout",
            success_url=success_url,
            cancel_url=cancel_url,
        )
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to initiate PayMongo checkout session: {str(e)}",
        )

    amount_php = f"₱{order.amount_minor / 100:,.2f}"

    return {
        "status": order.status,
        "message": "Order and PayMongo checkout session generated successfully.",
        "order_id": order.id,
        "order_number": order.order_number,
        "order_title": order.title,
        "payment_attempt_id": attempt.id,
        "provider_payment_id": attempt.provider_payment_id or "",
        "amount_minor": order.amount_minor,
        "amount_php": amount_php,
        "currency": order.currency,
        "checkout_url": attempt.checkout_url or "",
        "customer_email": payload.customer_email,
        "customer_name": payload.customer_name,
        "is_duplicate": False,
        "idempotent_replay": False,
    }


# ---------------------------------------------------------------------------
# Endpoint 2: Authoritative PayMongo Webhook Receiver
# ---------------------------------------------------------------------------
@router.post("/api/v1/webhooks/paymongo", summary="Authoritative PayMongo Webhook Receiver")
async def handle_paymongo_webhook(
    request: Request,
    background_tasks: BackgroundTasks,
    paymongo_signature: Optional[str] = Header(default=None, alias="Paymongo-Signature"),
    db: Session = Depends(get_db),
):
    """
    Authoritative PayMongo Webhook handler.
    1. Verifies HMAC-SHA256 signature to prevent tampering.
    2. Processes checkout_session.payment.paid, payment.failed, checkout_session.expired.
    3. Enforces terminal state protection (a 'paid' order cannot be reverted).
    4. Triggers SMTP email notification & Google Sheet live status update.
    """
    raw_body = await request.body()

    if not paymongo_signature:
        raise HTTPException(status_code=401, detail="Missing Paymongo-Signature header.")

    if not verify_paymongo_signature(raw_body, paymongo_signature, os.getenv("PAYMONGO_WEBHOOK_SECRET", "")):
        raise HTTPException(status_code=401, detail="Invalid Paymongo-Signature header.")

    try:
        result = orchestrator.process_webhook(
            db=db,
            raw_body=raw_body,
            signature_header=paymongo_signature,
        )

        # Trigger background notifications if order details are resolved
        body_json = json.loads(raw_body.decode("utf-8"))
        event_attrs = body_json.get("data", {}).get("attributes", {})
        event_type = event_attrs.get("type", "")
        res_data = event_attrs.get("data", {})
        res_attrs = res_data.get("attributes", {})
        res_meta = res_attrs.get("metadata", {}) or {}

        customer_email = res_meta.get("customer_email") or res_meta.get("email")
        customer_name = res_meta.get("customer_name") or "Valued Customer"
        gform_id = res_meta.get("google_forms_response_id") or res_meta.get("google_form_response_id")
        order_no = res_meta.get("order_number") or ""

        outcome_map = {
            "checkout_session.payment.paid": "paid",
            "payment.paid": "paid",
            "payment.failed": "failed",
            "checkout_session.expired": "expired",
            "qrph.expired": "expired",
            "qr.expired": "expired",
            "qr.paid": "paid",
            "refund.succeeded": "refunded",
        }
        outcome = outcome_map.get(event_type)

        if not order_no and result.get("order_number"):
            order_no = result.get("order_number")

        if (not customer_email or not gform_id) and result.get("order_id"):
            db_order = db.query(Order).filter(Order.id == result.get("order_id")).first()
            if db_order:
                o_dict = db_order.metadata_dict or {}
                customer_email = customer_email or o_dict.get("customer_email")
                if customer_name == "Valued Customer" and o_dict.get("customer_name"):
                    customer_name = o_dict.get("customer_name")
                gform_id = gform_id or o_dict.get("google_forms_response_id") or o_dict.get("google_form_response_id")

        if outcome and customer_email:
            background_tasks.add_task(
                send_webhook_smtp_email,
                customer_email=customer_email,
                customer_name=customer_name,
                order_number=order_no,
                reference_code=res_meta.get("reference_code", "Product"),
                amount_minor=int(res_attrs.get("amount", 0)),
                outcome=outcome,
                official_receipt_no=result.get("official_receipt_number"),
            )

        if outcome and gform_id:
            background_tasks.add_task(
                sync_status_to_google_sheet,
                response_id=gform_id,
                order_number=order_no,
                payment_status=outcome,
                official_receipt_no=result.get("official_receipt_number"),
            )

        return result
    except PermissionError as e:
        raise HTTPException(status_code=401, detail=str(e))
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Internal webhook processing error: {str(e)}")


class SimulatePaymentAttemptRequest(BaseModel):
    outcome: str = Field(..., description="'paid', 'failed', or 'expired'")
    force_override: bool = Field(True, description="Force override terminal states")
    failed_code: Optional[str] = None
    failed_message: Optional[str] = None


@router.post("/api/v1/payments/attempts/{attempt_id}/simulate", summary="Dedicated Testing Simulator for Payment Attempt")
def simulate_payment_webhook(
    attempt_id: str,
    body: SimulatePaymentAttemptRequest,
    db: Session = Depends(get_db),
):
    attempt = (
        db.query(PaymentAttempt)
        .filter(
            (PaymentAttempt.id == attempt_id)
            | (PaymentAttempt.provider_payment_id == attempt_id)
            | (PaymentAttempt.order_id == attempt_id)
        )
        .order_by(PaymentAttempt.created_at.desc())
        .first()
    )
    if not attempt:
        raise HTTPException(status_code=404, detail="Payment attempt not found")

    order = db.query(Order).filter(Order.id == attempt.order_id).first()
    now = datetime.now(timezone.utc)

    outcome_clean = body.outcome.strip().lower()
    event_map = {
        "paid": ("checkout_session.payment.paid", f"evt_sim_{uuid.uuid4().hex[:12]}"),
        "failed": ("payment.failed", f"evt_fail_{uuid.uuid4().hex[:36]}"),
        "expired": ("checkout_session.expired", f"evt_exp_{uuid.uuid4().hex[:36]}"),
    }
    if outcome_clean not in event_map:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported outcome '{body.outcome}'. Must be 'paid', 'failed', or 'expired'."
        )

    event_type, provider_event_id = event_map[outcome_clean]

    resource = {
        "id": attempt.provider_payment_id or f"cs_sim_{uuid.uuid4().hex[:16]}",
        "type": "checkout_session",
        "attributes": {
            "status": outcome_clean,
            "failed_code": body.failed_code or ("DECLINED" if outcome_clean == "failed" else None),
            "failed_message": body.failed_message or ("Simulated failure" if outcome_clean == "failed" else None),
            "metadata": {
                "order_id": order.id if order else attempt.order_id,
                "payment_attempt_id": attempt.id,
            },
        },
    }

    synthetic_payload = {
        "data": {
            "id": provider_event_id,
            "type": "event",
            "attributes": {
                "type": event_type,
                "livemode": False,
                "data": resource,
            },
        }
    }

    # Record in webhook_events table
    webhook_record = WebhookEvent(
        id=str(uuid.uuid4()),
        provider="paymongo",
        provider_event_id=provider_event_id,
        event_type=event_type,
        processing_status="received",
        received_at=now,
        payload=json.dumps(synthetic_payload),
    )
    db.add(webhook_record)
    db.flush()

    result = apply_webhook_transition(
        db=db,
        event_type=event_type,
        resource=resource,
        attempt=attempt,
        order=order,
        OutboxModel=FulfillmentOutbox,
        force_override=body.force_override,
    )

    webhook_record.processing_status = "processed"
    webhook_record.processed_at = datetime.now(timezone.utc)
    db.commit()

    return {
        "ok": True,
        "provider_event_id": provider_event_id,
        **result,
    }

"""
Google Forms & Google Apps Script Integration Router.

Provides an authoritative, high-throughput intake endpoint that pairs Google Form
submissions directly to PayMongo checkout sessions and registers them in the
Accounting & Payments platform.
"""

import os
import json
import uuid
import hmac
import hashlib
import smtplib
import urllib.request
from datetime import datetime, timezone
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
from typing import Optional, Dict, Any
from fastapi import APIRouter, Depends, HTTPException, Header, Request, status
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session

from database import get_db
from models import Order, PaymentAttempt
from providers.paymongo import PayMongoProvider
from services.payment_orchestrator import PaymentOrchestrator
from services.receipt_service import format_philippine_phone

router = APIRouter(prefix="/api/v1/integrations/google-forms", tags=["Google Forms Integration"])
orchestrator = PaymentOrchestrator(provider=PayMongoProvider())

GOOGLE_FORMS_SECRET = os.getenv("GOOGLE_FORMS_SECRET", "rge_gforms_live_sec_89f3a21")
GOOGLE_APPS_SCRIPT_WEBHOOK_URL = os.getenv("GOOGLE_APPS_SCRIPT_WEBHOOK_URL", "")

SMTP_HOST = os.getenv("SMTP_HOST", "smtp.gmail.com")
SMTP_PORT = int(os.getenv("SMTP_PORT", "587"))
SMTP_USERNAME = os.getenv("SMTP_USERNAME", "") or os.getenv("SMTP_USER", "")
SMTP_PASSWORD = os.getenv("SMTP_PASSWORD", "") or os.getenv("SMTP_APP_PASSWORD", "")
SMTP_FROM_EMAIL = os.getenv("SMTP_FROM_EMAIL", "R.G.E Accounting Firm Inc. <noreply@rge-accounting.com>")


def get_expected_gforms_secret() -> str:
    """Retrieves the authoritative secret configured in .env or defaults to safe fallback."""
    return os.getenv("GOOGLE_FORMS_SECRET", "rge_gforms_live_sec_89f3a21").strip()


def verify_google_forms_request(
    raw_body: bytes,
    secret_header: Optional[str],
    signature_header: Optional[str] = None,
) -> bool:
    """Verifies X-Google-Forms-Secret and optional X-Google-Forms-Signature (HMAC-SHA256)."""
    expected_sec = get_expected_gforms_secret()
    if not secret_header or not hmac.compare_digest(secret_header.strip(), expected_sec):
        return False
    if signature_header:
        expected_hmac = hmac.new(
            expected_sec.encode("utf-8"),
            msg=raw_body,
            digestmod=hashlib.sha256,
        ).hexdigest()
        if not hmac.compare_digest(expected_hmac, signature_header.strip()):
            return False
    return True


def notify_paymongo_webhook_outcome(
    *,
    outcome: str,  # "PAID" | "FAILED" | "EXPIRED"
    order_number: str,
    order_title: str,
    amount_minor: int,
    customer_email: str,
    customer_name: str,
    response_id: Optional[str] = None,
    payment_attempt_id: Optional[str] = None,
    sheet_row_index: Optional[int] = None,
    provider_event_id: Optional[str] = None,
    official_receipt_no: Optional[str] = None,
    checkout_url: Optional[str] = None,
    failure_code: Optional[str] = None,
    failure_message: Optional[str] = None,
) -> None:
    """
    Executed ONLY after a verified PayMongo Webhook is processed:
    1. Sends the authoritative SMTP email (Official Receipt on PAID, Retry on FAILED, Notice on EXPIRED).
    2. Calls the Google Apps Script doPost(e) Web App to synchronize the Google Sheet row live.
    """
    normalized_outcome = outcome.upper()
    amount_php = f"₱{amount_minor / 100:,.2f}"

    # 1. Dispatch Authoritative SMTP Email
    if customer_email and SMTP_USERNAME and SMTP_PASSWORD:
        if normalized_outcome == "PAID":
            subject = f"Official Receipt {official_receipt_no or ''} - Order {order_number} (PAID)"
            badge_bg = "#16a34a"
            headline = "Payment Confirmed & Official Receipt Issued"
            details_html = (
                f"<p>Your payment for <strong>{order_title}</strong> ({amount_php}) has been verified via PayMongo.</p>"
                f"<p><strong>Official Receipt Number:</strong> {official_receipt_no or 'Issued in Portal'}</p>"
            )
        elif normalized_outcome == "FAILED":
            subject = f"Payment Failed: Action Required for Order {order_number}"
            badge_bg = "#dc2626"
            headline = "Payment Attempt Failed"
            details_html = (
                f"<p>Your payment attempt for <strong>{order_title}</strong> ({amount_php}) could not be completed.</p>"
                f"<p><strong>Failure Code:</strong> {failure_code or 'DECLINED'} — {failure_message or 'Please try again.'}</p>"
                + (
                    f"<p><a href='{checkout_url}' style='background:#dc2626;color:#fff;padding:12px 24px;"
                    f"border-radius:6px;text-decoration:none;font-weight:600;display:inline-block;'>"
                    f"Retry Secure Payment &rarr;</a></p>"
                    if checkout_url else ""
                )
            )
        else:  # EXPIRED
            subject = f"Payment Link Expired: Order {order_number}"
            badge_bg = "#d97706"
            headline = "PayMongo Checkout Session Expired"
            details_html = (
                f"<p>The checkout link for <strong>{order_title}</strong> ({amount_php}) has expired.</p>"
                "<p>If you still wish to complete your order, please submit a new form or contact our finance desk.</p>"
            )

        html_body = f"""
        <div style="font-family: -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px;">
          <div style="background: {badge_bg}; color: #fff; padding: 6px 12px; border-radius: 4px; display: inline-block; font-size: 12px; font-weight: 700;">
            {normalized_outcome}
          </div>
          <h2 style="color: #0f172a; margin-top: 16px;">{headline}</h2>
          <p>Dear <strong>{customer_name}</strong>,</p>
          {details_html}
          <hr style="border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;" />
          <p style="font-size: 12px; color: #64748b;">Order Reference: {order_number} | Event ID: {provider_event_id or 'N/A'}</p>
        </div>
        """

        try:
            msg = MIMEMultipart("alternative")
            msg["Subject"] = subject
            msg["From"] = SMTP_FROM_EMAIL
            msg["To"] = customer_email
            msg.attach(MIMEText(html_body, "html"))

            with smtplib.SMTP(SMTP_HOST, SMTP_PORT, timeout=15) as server:
                server.ehlo()
                server.starttls()
                server.login(SMTP_USERNAME, SMTP_PASSWORD)
                server.sendmail(SMTP_FROM_EMAIL, [customer_email], msg.as_string())
        except Exception as smtp_err:
            print(f"[SMTP Error] Could not send {normalized_outcome} email to {customer_email}: {smtp_err}")

    # 2. Synchronize Live Status Back to Google Sheets via Apps Script doPost(e)
    script_url = (os.getenv("GOOGLE_APPS_SCRIPT_WEBHOOK_URL", "")).strip()
    if script_url and (response_id or order_number):
        callback_payload = json.dumps({
            "secret": get_expected_gforms_secret(),
            "response_id": response_id,
            "order_number": order_number,
            "payment_attempt_id": payment_attempt_id,
            "sheet_row_index": sheet_row_index,
            "payment_status": normalized_outcome,
            "official_receipt_no": official_receipt_no or "",
            "provider_event_id": provider_event_id or "",
            "failure_code": failure_code or "",
            "failure_message": failure_message or "",
            "updated_at": datetime.now(timezone.utc).isoformat(),
        }).encode("utf-8")

        req = urllib.request.Request(
            script_url,
            data=callback_payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=10) as _:
                pass
        except Exception as gas_err:
            print(f"[Google Sheets Callback Notice] {gas_err}")


# Authoritative Bookstore Catalog (PHP Centavos)
BOOKSTORE_CATALOG_MINOR: Dict[str, int] = {
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
    for book_key, price_cents in BOOKSTORE_CATALOG_MINOR.items():
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
        if any(bk in ref_lower for bk in BOOKSTORE_CATALOG_MINOR) or "₱" in reference_code or "php" in ref_lower:
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


class GoogleFormSubmissionRequest(BaseModel):
    response_id: str = Field(..., description="Unique Google Form Response ID")
    sheet_row_index: Optional[int] = Field(None, description="Row index in connected Google Sheet")
    sheet_row: Optional[int] = Field(None, description="Row index alias in connected Google Sheet")
    customer_name: str = Field(..., description="Customer full name")
    customer_email: str = Field(..., description="Customer email for payment link & receipt delivery")
    customer_phone: Optional[str] = Field(None, description="Philippine mobile number (+639... / 09...)")
    item_reference: Optional[str] = Field("Product A", description="Selected item code, product, or department")
    reference_code: Optional[str] = Field(None, description="Alias for item_reference (e.g. from Google Apps Script)")
    title: Optional[str] = Field(None, description="Custom explicit title for the order")
    category: str = Field("product_order", description="'product_order' or 'department_invoice'")
    quantity: int = Field(1, ge=1, description="Quantity ordered")
    amount_minor: Optional[int] = Field(None, ge=100, description="Optional custom amount in PHP centavos")
    payment_method: str = Field("hosted_checkout", description="Payment channel: hosted_checkout, qrph, gcash_maya, card")
    notes: Optional[str] = Field(None, description="Optional notes or instructions from the form")
    raw_answers: Optional[Dict[str, Any]] = Field(None, description="Raw form answers map for audit trail")
    student_id: Optional[str] = Field(None, description="Student or CPA review enrollee ID number")
    instructor_credit: Optional[str] = Field(None, description="Enrolled faculty instructor to credit")
    book_title: Optional[str] = Field(None, description="Course book or educational material")


class GoogleFormSubmissionResponse(BaseModel):
    status: str
    message: str
    order_id: str
    order_number: str
    order_title: str
    amount_minor: int
    amount_php: str
    currency: str
    checkout_url: str
    payment_attempt_id: str
    provider_payment_id: Optional[str] = None
    customer_email: str
    customer_name: str
    is_duplicate: bool


@router.post(
    "/submit",
    response_model=GoogleFormSubmissionResponse,
    status_code=status.HTTP_201_CREATED,
    summary="Ingest Google Form Response and Pair with PayMongo Checkout",
)
async def submit_google_form(
    req: GoogleFormSubmissionRequest,
    x_google_forms_secret: Optional[str] = Header(None, alias="X-Google-Forms-Secret"),
    x_google_forms_signature: Optional[str] = Header(None, alias="X-Google-Forms-Signature"),
    db: Session = Depends(get_db),
):
    """
    Ingests a Google Form submission from Google Apps Script, validates the shared secret,
    computes authoritative server-side pricing, generates an Order and PayMongo Checkout Session,
    and returns the checkout URL for immediate customer email dispatch and Google Sheet recording.
    """
    # 1. Authenticate shared secret
    expected_secret = get_expected_gforms_secret()
    if not x_google_forms_secret or x_google_forms_secret.strip() != expected_secret:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Unauthorized: Missing or invalid X-Google-Forms-Secret header.",
        )

    clean_response_id = req.response_id.strip()
    if not clean_response_id:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="response_id is required.")

    # 2. Idempotency Check: search if an order for this Google Form response already exists
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
            prov_pm_id = latest_attempt.provider_payment_id if latest_attempt else ""
            amount_php = f"₱{o.amount_minor / 100:,.2f}"

            return GoogleFormSubmissionResponse(
                status=o.status,
                message="Idempotent match: Order already generated for this Google Form submission.",
                order_id=o.id,
                order_number=o.order_number,
                order_title=o.title,
                amount_minor=o.amount_minor,
                amount_php=amount_php,
                currency=o.currency,
                checkout_url=checkout_url or "",
                payment_attempt_id=attempt_id,
                provider_payment_id=prov_pm_id,
                customer_email=req.customer_email,
                customer_name=req.customer_name,
                is_duplicate=True,
            )

    # 3. Format customer contact info & resolve field aliases
    formatted_phone = format_philippine_phone(req.customer_phone) if req.customer_phone else None
    row_idx = req.sheet_row if req.sheet_row is not None else req.sheet_row_index

    # Resolve bookstore items, student ID, and instructor credit from direct fields, raw_answers, and notes
    book_ref, student_id, instructor_credit = resolve_bookstore_fields(
        raw_answers=req.raw_answers,
        notes=req.notes,
        direct_book=req.book_title,
        direct_student_id=req.student_id,
        direct_instructor=req.instructor_credit,
        reference_code=req.reference_code or req.item_reference,
    )

    raw_ref = (req.reference_code or req.item_reference or "").strip()
    if (not raw_ref or raw_ref.lower() == "product a") and book_ref:
        ref_code = book_ref
    else:
        ref_code = raw_ref or "Product A"

    # Compute authoritative pricing for bookstore materials if not explicitly provided
    resolved_amount_minor = req.amount_minor
    if (resolved_amount_minor is None or resolved_amount_minor <= 0) and (book_ref or ref_code):
        unit_price = extract_price_from_text(book_ref or ref_code)
        if unit_price:
            resolved_amount_minor = unit_price * req.quantity

    if req.title:
        computed_title = req.title.strip()
    else:
        credit_tag = f" [Credit: {instructor_credit}]" if instructor_credit else ""
        computed_title = f"{ref_code} (Qty: {req.quantity}) - {req.customer_name.strip()}{credit_tag}"

    meta = {
        "source": "google_forms",
        "google_form_response_id": clean_response_id,
        "google_forms_response_id": clean_response_id,
        "sheet_row_index": row_idx,
        "sheet_row": row_idx,
        "customer_name": req.customer_name.strip(),
        "customer_email": req.customer_email.strip().lower(),
        "customer_phone": formatted_phone or req.customer_phone,
        "reference_code": ref_code,
        "book_title": book_ref or ref_code,
        "student_id": student_id,
        "instructor_credit": instructor_credit,
        "notes": req.notes,
        "raw_answers": req.raw_answers,
    }

    # 4. Create Order via PaymentOrchestrator (handles authoritative server-side pricing)
    try:
        order = orchestrator.create_order(
            db=db,
            title=computed_title,
            category=req.category if req.category in ("product_order", "department_invoice") else "product_order",
            reference_code=ref_code,
            amount_minor=resolved_amount_minor,
            quantity=req.quantity,
            user_id="google_forms_agent",
            metadata=meta,
        )
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Failed to create order from form submission: {str(e)}",
        )

    # 5. Initiate PayMongo Checkout Session
    # Construct canonical success & cancel URLs pointing back to the web application
    base_domain = os.getenv("NEXT_PUBLIC_APP_DOMAIN") or os.getenv("APP_DOMAIN") or "https://rge-accounting-3aa707d5f496.herokuapp.com"
    clean_base = base_domain.strip().rstrip("/")
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
            payment_method=req.payment_method or "hosted_checkout",
            success_url=success_url,
            cancel_url=cancel_url,
        )
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to initiate PayMongo checkout session: {str(e)}",
        )

    amount_php = f"₱{order.amount_minor / 100:,.2f}"

    return GoogleFormSubmissionResponse(
        status=order.status,
        message="Order and PayMongo checkout session generated successfully.",
        order_id=order.id,
        order_number=order.order_number,
        order_title=order.title,
        amount_minor=order.amount_minor,
        amount_php=amount_php,
        currency=order.currency,
        checkout_url=attempt.checkout_url or "",
        payment_attempt_id=attempt.id,
        provider_payment_id=attempt.provider_payment_id,
        customer_email=req.customer_email,
        customer_name=req.customer_name,
        is_duplicate=False,
    )


@router.get("/status", summary="Check Google Forms Integration Status")
def get_google_forms_status(db: Session = Depends(get_db)):
    """
    Returns integration status and counts of orders ingested via Google Forms.
    """
    all_orders = db.query(Order).all()
    gform_orders = [o for o in all_orders if (o.metadata_dict or {}).get("source") == "google_forms"]
    paid_count = sum(1 for o in gform_orders if o.status == "paid")
    pending_count = sum(1 for o in gform_orders if o.status == "pending")

    return {
        "status": "active",
        "ingested_orders_count": len(gform_orders),
        "paid_orders_count": paid_count,
        "pending_orders_count": pending_count,
        "secret_configured": bool(get_expected_gforms_secret()),
    }


def sync_status_to_google_sheet(
    response_id: str,
    order_number: str,
    payment_status: str,
    official_receipt_no: Optional[str] = None,
    failure_reason: Optional[str] = None,
    webhook_url: Optional[str] = None,
    secret: Optional[str] = None,
) -> bool:
    """
    Calls the Google Apps Script doPost(e) Web App endpoint to update the Google Sheet row in real time.
    """
    target_url = (webhook_url or os.getenv("GOOGLE_APPS_SCRIPT_WEBHOOK_URL", "")).strip()
    if not target_url:
        return False

    auth_secret = (secret or get_expected_gforms_secret()).strip()
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



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

# Authoritative Server-Side Product Catalog (in PHP centavos - prevents price tampering)
AUTHORITATIVE_PRICE_CATALOG_MINOR: Dict[str, int] = {
    "product a": 25000,   # PHP 250.00
    "product b": 17800,   # PHP 178.00
    "product c": 15600,   # PHP 156.00
    "product d": 19500,   # PHP 195.00
    "product e": 11200,   # PHP 112.00
}


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


# ---------------------------------------------------------------------------
# Helper 1: Verify PayMongo Webhook Signature (Anti-Tampering)
# ---------------------------------------------------------------------------
def verify_paymongo_signature(raw_body: bytes, signature_header: Optional[str], secret: Optional[str] = None) -> bool:
    """
    Verifies the Paymongo-Signature header: t=<timestamp>,te=<test_sig>,li=<live_sig>
    If secret is empty in development/testing, allows sandbox test webhooks.
    """
    target_secret = (secret if secret is not None else os.getenv("PAYMONGO_WEBHOOK_SECRET", "")).strip()
    if not target_secret:
        return True
    if not signature_header:
        return False

    parts = {}
    for item in signature_header.split(","):
        if "=" in item:
            k, v = item.strip().split("=", 1)
            parts[k] = v

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

    ref_code = (payload.item_reference or payload.reference_code or "Product A").strip()
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
        "notes": payload.notes,
        "raw_answers": payload.raw_answers,
    }

    title = payload.title or f"{ref_code} (Qty: {qty}) - {payload.customer_name.strip()}"

    # 4. Create Order via PaymentOrchestrator
    try:
        order = orchestrator.create_order(
            db=db,
            title=title,
            category=payload.category if payload.category in ("product_order", "department_invoice") else "product_order",
            reference_code=ref_code,
            amount_minor=payload.amount_minor,
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

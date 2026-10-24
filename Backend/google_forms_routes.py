"""
Google Forms & Google Apps Script Integration Router.

Provides an authoritative, high-throughput intake endpoint that pairs Google Form
submissions directly to PayMongo checkout sessions and registers them in the
Accounting & Payments platform.
"""

import os
import json
import uuid
import urllib.request
from datetime import datetime, timezone
from typing import Optional, Dict, Any
from fastapi import APIRouter, Depends, HTTPException, Header, status
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session

from database import get_db
from models import Order, PaymentAttempt
from providers.paymongo import PayMongoProvider
from services.payment_orchestrator import PaymentOrchestrator
from services.receipt_service import format_philippine_phone

router = APIRouter(prefix="/api/v1/integrations/google-forms", tags=["Google Forms Integration"])
orchestrator = PaymentOrchestrator(provider=PayMongoProvider())


def get_expected_gforms_secret() -> str:
    """Retrieves the authoritative secret configured in .env or defaults to safe fallback."""
    return os.getenv("GOOGLE_FORMS_SECRET", "rge_gforms_live_sec_89f3a21").strip()


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
    ref_code = (req.reference_code or req.item_reference or "Product A").strip()
    row_idx = req.sheet_row if req.sheet_row is not None else req.sheet_row_index
    computed_title = req.title.strip() if req.title else f"{ref_code} (Qty: {req.quantity}) - {req.customer_name.strip()}"

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
            amount_minor=req.amount_minor,
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



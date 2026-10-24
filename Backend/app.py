import os
import json
import uuid
from pathlib import Path
from typing import Optional, List, Dict, Any
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request, Response, Depends, HTTPException, Header, status, Body
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session
from dotenv import load_dotenv

# Load authoritative monorepo environment configuration (E:\Accounting\.env)
_root_env = Path(__file__).resolve().parent.parent / ".env"
if _root_env.exists():
    load_dotenv(_root_env)

from database import get_db
from models import init_db, Order, PaymentAttempt, WebhookEvent, FulfillmentOutbox
from services.payment_orchestrator import PaymentOrchestrator
from providers.paymongo import PayMongoProvider
from finance_routes import router as finance_router
from integration_routes import router as integration_router
from auth_routes import router as auth_router
from cost_allocation_routes import router as cost_allocation_router

DATA_DIR = Path(__file__).parent / "data"

# Lifespan for startup DB initialization
@asynccontextmanager
async def lifespan(app: FastAPI):
    # Initialize DB tables and seed records
    try:
        init_db()
    except Exception as e:
        print(f"[ERROR] Database schema initialization notice: {e}", flush=True)
    yield

app = FastAPI(
    title="Acme Cost Accounting & Live Payment Orchestration API",
    version="2.0.0",
    description="Unified API providing legacy cost-analysis endpoints and live PayMongo payment orchestration.",
    lifespan=lifespan,
)

# CORS middleware for Next.js frontend & Cloudflare domain
FRONTEND_ORIGIN = os.getenv("NEXT_PUBLIC_APP_DOMAIN") or os.getenv("APP_DOMAIN") or os.getenv("NEXT_PUBLIC_APP_URL")

allowed_origins = [
    "http://localhost:3000",
    "http://127.0.0.1:3000",
    "http://localhost:5000",
    "http://127.0.0.1:5000",
]

if FRONTEND_ORIGIN:
    clean_origin = FRONTEND_ORIGIN.strip().rstrip("/")
    if clean_origin.startswith("http://") or clean_origin.startswith("https://"):
        if clean_origin not in allowed_origins:
            allowed_origins.append(clean_origin)
    else:
        allowed_origins.append(f"https://{clean_origin}")
        allowed_origins.append(f"http://{clean_origin}")

if "*" not in allowed_origins:
    allowed_origins.append("*")

app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(finance_router)
app.include_router(integration_router)
app.include_router(auth_router)
app.include_router(cost_allocation_router)

orchestrator = PaymentOrchestrator(provider=PayMongoProvider())

# --- Legacy Helper Functions ---
def read_json_file(filename: str):
    file_path = DATA_DIR / filename
    if not file_path.exists():
        return None
    with open(file_path, "r") as f:
        return json.load(f)

def write_json_file(filename: str, data: Any):
    file_path = DATA_DIR / filename
    with open(file_path, "w") as f:
        json.dump(data, f, indent=4)

# ==========================================
# 0. Health Checks
# ==========================================

@app.get("/health", summary="Health Check")
@app.get("/api/health", summary="API Health Check")
def health_check():
    return {"status": "ok", "service": "Acme Cost Accounting & PayMongo Orchestrator", "version": "2.0.0"}


# ==========================================
# 1. Legacy API Routes (100% Backward Compatible)
# ==========================================

@app.get("/api/dashboard", summary="Legacy Dashboard Data")
def get_dashboard_data():
    data = read_json_file("dashboard_data.json")
    if data is None:
        raise HTTPException(status_code=404, detail="Dashboard data not found.")
    return data

@app.get("/api/department/{department_name}", summary="Legacy Department Detail")
def get_department_detail(department_name: str):
    slug = department_name.lower().replace(" ", "-")
    department_details = read_json_file("department_detail_data.json")
    if department_details is None:
        raise HTTPException(status_code=404, detail="Department details data not found.")
    data = department_details.get(slug)
    if not data:
        raise HTTPException(status_code=404, detail=f"Data for department '{department_name}' not found.")
    return data

@app.get("/api/products", summary="Legacy Product Line Analysis")
def get_product_analysis():
    data = read_json_file("product_data.json")
    if data is None:
        raise HTTPException(status_code=404, detail="Product data not found.")
    return data

@app.get("/api/variance", summary="Legacy Variance Analysis")
def get_variance_analysis():
    data = read_json_file("variance_data.json")
    if data is None:
        raise HTTPException(status_code=404, detail="Variance data not found.")
    return data

@app.get("/api/configuration", summary="Legacy Configuration Data")
def get_configuration():
    data = read_json_file("configuration.json")
    if data is None:
        raise HTTPException(status_code=404, detail="Configuration data not found.")
    return data

@app.put("/api/configuration", summary="Update General Configuration")
async def update_configuration(request: Request):
    """
    Persists configuration updates to configuration.json.
    """
    body = await request.json()
    cfg_data = read_json_file("configuration.json") or {}
    if "config" in body:
        cfg_data["config"] = {**cfg_data.get("config", {}), **body["config"]}
    write_json_file("configuration.json", cfg_data)
    return {"success": True, "config": cfg_data.get("config")}

@app.post("/api/configuration/departments", summary="Save Departments")
async def save_departments(request: Request):
    """
    Persists department modifications to configuration.json.
    """
    body = await request.json()
    departments = body.get("departments")
    if departments is None:
        raise HTTPException(status_code=400, detail="Missing departments array.")
    cfg_data = read_json_file("configuration.json") or {}
    cfg_data["departments"] = departments
    write_json_file("configuration.json", cfg_data)
    return {"success": True, "departments": departments}

@app.post("/api/configuration/users", summary="Save Users")
async def save_users(request: Request):
    """
    Persists user access list modifications to configuration.json.
    """
    body = await request.json()
    users = body.get("users")
    if users is None:
        raise HTTPException(status_code=400, detail="Missing users array.")
    cfg_data = read_json_file("configuration.json") or {}
    cfg_data["users"] = users
    write_json_file("configuration.json", cfg_data)
    return {"success": True, "users": users}


# ==========================================
# 2. Payment Orchestration Schemas & Routes (/api/v1/*)
# ==========================================

class CreateOrderRequest(BaseModel):
    title: str = Field(..., description="Order or invoice title")
    category: str = Field("product_order", description="'product_order' or 'department_invoice'")
    reference_code: str = Field(..., description="Product name or department slug")
    quantity: int = Field(1, ge=1, description="Quantity for products")
    amount_minor: Optional[int] = Field(None, description="Authoritative minor amount in centavos")
    customer_name: Optional[str] = Field(None, description="Customer or payer name")
    customer_email: Optional[str] = Field(None, description="Customer receipt email")
    customer_phone: Optional[str] = Field(None, description="Customer contact or mobile number")
    metadata: Optional[Dict[str, Any]] = None

class CreatePaymentAttemptRequest(BaseModel):
    idempotency_key: Optional[str] = Field(None, description="Client-provided idempotency key")
    payment_method: str = Field("hosted_checkout", description="Payment method channel")
    success_url: Optional[str] = None
    cancel_url: Optional[str] = None
    customer_name: Optional[str] = None
    customer_email: Optional[str] = None
    customer_phone: Optional[str] = None

class SimulateWebhookRequest(BaseModel):
    payment_attempt_id: str
    event_type: str = Field("checkout_session.payment.paid", description="Webhook event type")
    duplicate: bool = Field(False, description="Whether to simulate duplicate event ID")
    payment_method: Optional[str] = Field(None, description="Simulated payment method channel (e.g. qrph, card_visa, gcash, maya, dob_bpi)")

class ResendReceiptEmailRequest(BaseModel):
    to_email: Optional[str] = Field(None, description="Recipient email address for receipt dispatch")

class ExpirePaymentRequest(BaseModel):
    reason: Optional[str] = Field("Manual expiration / session timeout", description="Reason for session expiration")

class FailPaymentRequest(BaseModel):
    code: Optional[str] = Field(None, description="Failure code")
    message: Optional[str] = Field(None, description="Failure explanation")
    failure_code: Optional[str] = Field(None, description="Alternative failure code alias")
    failure_message: Optional[str] = Field(None, description="Alternative failure message alias")

@app.get("/api/v1/orders", summary="List Orders & Invoices")
def list_orders(status: Optional[str] = None, db: Session = Depends(get_db)):
    query = db.query(Order).order_by(Order.created_at.desc())
    if status:
        query = query.filter(Order.status == status)
    orders = query.all()

    result = []
    for o in orders:
        o_dict = o.to_dict()
        latest_attempt = (
            db.query(PaymentAttempt)
            .filter(PaymentAttempt.order_id == o.id)
            .order_by(PaymentAttempt.created_at.desc())
            .first()
        )
        o_dict["latest_attempt"] = latest_attempt.to_dict() if latest_attempt else None
        result.append(o_dict)
    return result

@app.post("/api/v1/orders", summary="Create New Order or Invoice", status_code=status.HTTP_201_CREATED)
def create_order(req: CreateOrderRequest, db: Session = Depends(get_db)):
    try:
        from services.receipt_service import format_philippine_phone
        meta = dict(req.metadata or {})
        if req.customer_name:
            meta["customer_name"] = req.customer_name
        if req.customer_email:
            meta["customer_email"] = req.customer_email
        if req.customer_phone:
            meta["customer_phone"] = format_philippine_phone(req.customer_phone) or req.customer_phone

        order = orchestrator.create_order(
            db=db,
            title=req.title,
            category=req.category,
            reference_code=req.reference_code,
            amount_minor=req.amount_minor,
            quantity=req.quantity,
            metadata=meta,
        )
        return order.to_dict()
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

@app.get("/api/v1/orders/{order_id}", summary="Get Order Details")
def get_order(order_id: str, db: Session = Depends(get_db)):
    order = db.query(Order).filter(Order.id == order_id).first()
    if not order:
        raise HTTPException(status_code=404, detail="Order not found.")
    attempts = (
        db.query(PaymentAttempt)
        .filter(PaymentAttempt.order_id == order.id)
        .order_by(PaymentAttempt.created_at.desc())
        .all()
    )
    res = order.to_dict()
    res["attempts"] = [a.to_dict() for a in attempts]
    res["latest_attempt"] = attempts[0].to_dict() if attempts else None
    return res

@app.post("/api/v1/orders/{order_id}/payment-attempts", summary="Initiate Payment Attempt (PayMongo Checkout)", status_code=status.HTTP_201_CREATED)
async def create_payment_attempt(
    order_id: str,
    req: CreatePaymentAttemptRequest,
    x_idempotency_key: Optional[str] = Header(None, alias="Idempotency-Key"),
    db: Session = Depends(get_db),
):
    idempotency_key = req.idempotency_key or x_idempotency_key or str(uuid.uuid4())
    try:
        # Update customer details in order metadata if provided
        order = db.query(Order).filter(Order.id == order_id).first()
        if order:
            from services.receipt_service import format_philippine_phone
            o_meta = dict(order.metadata_dict or {})
            updated_o = False
            if req.customer_name:
                o_meta["customer_name"] = req.customer_name
                updated_o = True
            if req.customer_email:
                o_meta["customer_email"] = req.customer_email
                updated_o = True
            if req.customer_phone:
                o_meta["customer_phone"] = format_philippine_phone(req.customer_phone) or req.customer_phone
                updated_o = True
            if updated_o:
                order.metadata_dict = o_meta
                db.add(order)
                db.commit()

        attempt = await orchestrator.initiate_payment_attempt(
            db=db,
            order_id=order_id,
            idempotency_key=idempotency_key,
            payment_method=req.payment_method,
            success_url=req.success_url,
            cancel_url=req.cancel_url,
        )
        return attempt.to_dict()
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Orchestration failure: {str(e)}")

def _resolve_payment_attempt_and_order(db: Session, identifier: str) -> tuple[Optional[PaymentAttempt], Optional[Order]]:
    """
    Authoritatively resolves a payment attempt and its associated order by any valid reference:
    - Exact Payment Attempt ID (UUID)
    - PayMongo Checkout Session / Payment ID (cs_... or raw 24-hex session token)
    - Order ID (UUID)
    - Order Number (ORD-...)
    """
    if not identifier:
        return None, None
    clean_id = str(identifier).strip()

    # 1. Match attempt by exact ID
    attempt = db.query(PaymentAttempt).filter(PaymentAttempt.id == clean_id).first()
    if attempt:
        order = db.query(Order).filter(Order.id == attempt.order_id).first()
        return attempt, order

    # 2. Match attempt by provider_payment_id (e.g. cs_633306eb5b337f661a7ab7ea or 633306eb5b337f661a7ab7ea)
    attempt = db.query(PaymentAttempt).filter(PaymentAttempt.provider_payment_id == clean_id).first()
    if not attempt and not clean_id.startswith("cs_"):
        attempt = db.query(PaymentAttempt).filter(PaymentAttempt.provider_payment_id == f"cs_{clean_id}").first()
    if not attempt and len(clean_id) >= 8:
        attempt = db.query(PaymentAttempt).filter(PaymentAttempt.provider_payment_id.like(f"%{clean_id}%")).first()
    if attempt:
        order = db.query(Order).filter(Order.id == attempt.order_id).first()
        return attempt, order

    # 3. Match attempt by order_id
    attempt = (
        db.query(PaymentAttempt)
        .filter(PaymentAttempt.order_id == clean_id)
        .order_by(PaymentAttempt.created_at.desc())
        .first()
    )
    if attempt:
        order = db.query(Order).filter(Order.id == attempt.order_id).first()
        return attempt, order

    # 4. Match attempt by order_number (e.g. ORD-202610-0427)
    order = db.query(Order).filter(Order.order_number == clean_id).first()
    if order:
        attempt = (
            db.query(PaymentAttempt)
            .filter(PaymentAttempt.order_id == order.id)
            .order_by(PaymentAttempt.created_at.desc())
            .first()
        )
        return attempt, order

    # 5. Check if identifier is Order ID directly
    order = db.query(Order).filter(Order.id == clean_id).first()
    if order:
        attempt = (
            db.query(PaymentAttempt)
            .filter(PaymentAttempt.order_id == order.id)
            .order_by(PaymentAttempt.created_at.desc())
            .first()
        )
        return attempt, order

    return None, None

@app.get("/api/v1/payments/{payment_id}", summary="Poll & Auto-Reconcile Payment Status")
async def get_payment_status(
    payment_id: str,
    auto_reconcile: bool = True,
    db: Session = Depends(get_db),
):
    attempt, order = _resolve_payment_attempt_and_order(db, payment_id)
    if not attempt and not order:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")

    # Proactive real-time gateway synchronization:
    # If attempt is in a non-terminal state and has a provider session ID, automatically reconcile with PayMongo
    if (
        auto_reconcile
        and attempt
        and attempt.provider_payment_id
        and attempt.status not in ("succeeded", "failed", "cancelled", "expired")
    ):
        try:
            await orchestrator.reconcile_payment_attempt(db, attempt.id)
            db.refresh(attempt)
            if order:
                db.refresh(order)
        except Exception as sync_err:
            print(f"Auto-sync reconciliation check note: {sync_err}")

    # Synchronize attempt if order was already confirmed paid
    if order and order.status == "paid" and attempt and attempt.status not in ("succeeded", "duplicate_paid_review"):
        attempt.status = "succeeded"
        if not attempt.paid_at:
            attempt.paid_at = order.updated_at or utc_now()
        db.commit()
        db.refresh(attempt)

    if not attempt and order:
        res = {
            "id": f"att_ord_{order.id[:8]}",
            "order_id": order.id,
            "provider": "paymongo",
            "payment_method": "hosted_checkout",
            "status": "succeeded" if order.status == "paid" else "pending",
            "amount_minor": order.amount_minor,
            "currency": order.currency,
            "order": order.to_dict(),
        }
        if order.status == "paid":
            from services.receipt_service import build_receipt_and_acknowledgement, persist_receipt_on_order
            synth_att = PaymentAttempt(
                id=res["id"],
                order_id=order.id,
                payment_method="hosted_checkout",
                status="succeeded",
                amount_minor=order.amount_minor,
                currency=order.currency,
            )
            receipt_rec = build_receipt_and_acknowledgement(order, synth_att)
            persist_receipt_on_order(db, order, synth_att, receipt_rec)
            res["receipt"] = receipt_rec
        return res

    res = attempt.to_dict()
    res["order"] = order.to_dict() if order else None
    if attempt.status == "expired":
        res["session_expired"] = True
        res["can_retry"] = True
        res["checkout_url"] = None

    if order:
        o_meta = order.metadata_dict or {}
        res["customer"] = {
            "name": o_meta.get("customer_name") or "Valued Customer",
            "email": o_meta.get("customer_email") or o_meta.get("email"),
            "phone": o_meta.get("customer_phone") or o_meta.get("phone"),
        }

    if attempt.status == "succeeded" and order:
        meta = order.metadata_dict
        receipt_rec = meta.get("receipt_records", {}).get(attempt.id) or meta.get("latest_receipt")
        if not receipt_rec:
            from services.receipt_service import build_receipt_and_acknowledgement, persist_receipt_on_order
            receipt_rec = build_receipt_and_acknowledgement(order, attempt)
            persist_receipt_on_order(db, order, attempt, receipt_rec)
        res["receipt"] = receipt_rec
    return res

@app.get("/api/v1/payments/{payment_id}/receipt", summary="Get Official Receipt & Acknowledgement Data")
def get_payment_receipt(payment_id: str, db: Session = Depends(get_db)):
    attempt, order = _resolve_payment_attempt_and_order(db, payment_id)
    if not attempt and not order:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")
    if not order and attempt:
        order = db.query(Order).filter(Order.id == attempt.order_id).first()
    if not order:
        raise HTTPException(status_code=404, detail="Associated order not found.")
    if (attempt and attempt.status != "succeeded") and order.status != "paid":
        raise HTTPException(
            status_code=400,
            detail="Official Receipt & Acknowledgement Report is only issued for succeeded payments.",
        )

    if not attempt:
        attempt = PaymentAttempt(
            id=f"att_ord_{order.id[:8]}",
            order_id=order.id,
            payment_method="hosted_checkout",
            status="succeeded",
            amount_minor=order.amount_minor,
            currency=order.currency,
        )

    from services.receipt_service import build_receipt_and_acknowledgement, persist_receipt_on_order
    meta = order.metadata_dict
    receipt_data = meta.get("receipt_records", {}).get(attempt.id) or meta.get("latest_receipt")
    if not receipt_data:
        receipt_data = build_receipt_and_acknowledgement(order, attempt)
        persist_receipt_on_order(db, order, attempt, receipt_data)
    return receipt_data

@app.get("/api/v1/payments/{payment_id}/receipt/pdf", summary="Download or Stream Official Receipt PDF")
def get_payment_receipt_pdf(payment_id: str, download: bool = False, db: Session = Depends(get_db)):
    attempt, order = _resolve_payment_attempt_and_order(db, payment_id)
    if not attempt and not order:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")
    if not order and attempt:
        order = db.query(Order).filter(Order.id == attempt.order_id).first()
    if not order:
        raise HTTPException(status_code=404, detail="Associated order not found.")
    if (attempt and attempt.status != "succeeded") and order.status != "paid":
        raise HTTPException(
            status_code=400,
            detail="Official Receipt & Acknowledgement Report is only available for succeeded payments.",
        )

    if not attempt:
        attempt = PaymentAttempt(
            id=f"att_ord_{order.id[:8]}",
            order_id=order.id,
            payment_method="hosted_checkout",
            status="succeeded",
            amount_minor=order.amount_minor,
            currency=order.currency,
        )

    from services.receipt_service import build_receipt_and_acknowledgement, generate_receipt_pdf_bytes, persist_receipt_on_order
    meta = order.metadata_dict
    receipt_data = meta.get("receipt_records", {}).get(attempt.id) or meta.get("latest_receipt")
    if not receipt_data:
        receipt_data = build_receipt_and_acknowledgement(order, attempt)
        persist_receipt_on_order(db, order, attempt, receipt_data)

    pdf_bytes = generate_receipt_pdf_bytes(receipt_data)
    filename = receipt_data.get("pdf_filename") or f"Acme_OR_ACK_{order.order_number}.pdf"
    disposition_type = "attachment" if download else "inline"

    return Response(
        content=pdf_bytes,
        media_type="application/pdf",
        headers={
            "Content-Disposition": f'{disposition_type}; filename="{filename}"',
            "Cache-Control": "public, max-age=3600",
        },
    )

@app.post("/api/v1/payments/{payment_id}/receipt/email", summary="Resend Official Receipt & Acknowledgement via SMTP")
def resend_payment_receipt_email(
    payment_id: str,
    req: Optional[ResendReceiptEmailRequest] = Body(None),
    db: Session = Depends(get_db),
):
    attempt, order = _resolve_payment_attempt_and_order(db, payment_id)
    if not attempt and not order:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")
    if not order and attempt:
        order = db.query(Order).filter(Order.id == attempt.order_id).first()
    if not order:
        raise HTTPException(status_code=404, detail="Associated order not found.")
    if (attempt and attempt.status != "succeeded") and order.status != "paid":
        raise HTTPException(status_code=400, detail="Cannot dispatch receipt for non-succeeded payment attempt.")

    if not attempt:
        attempt = PaymentAttempt(
            id=f"att_ord_{order.id[:8]}",
            order_id=order.id,
            payment_method="hosted_checkout",
            status="succeeded",
            amount_minor=order.amount_minor,
            currency=order.currency,
        )

    from services.email_service import EmailService
    from services.receipt_service import (
        build_receipt_and_acknowledgement,
        generate_receipt_pdf_bytes,
        persist_receipt_on_order,
        resolve_recipient_email,
    )
    from models import utc_now

    email_svc = EmailService()
    if not email_svc.is_configured():
        raise HTTPException(status_code=503, detail="SMTP notification service is not configured with valid credentials.")

    meta = order.metadata_dict
    recipient = (req.to_email.strip() if req and req.to_email and req.to_email.strip() else None) or resolve_recipient_email(meta)
    if not recipient:
        raise HTTPException(status_code=400, detail="No valid recipient email address could be resolved.")

    receipt_data = meta.get("receipt_records", {}).get(attempt.id) or meta.get("latest_receipt")
    if not receipt_data:
        receipt_data = build_receipt_and_acknowledgement(order, attempt)

    pdf_bytes = generate_receipt_pdf_bytes(receipt_data)
    pdf_filename = receipt_data["pdf_filename"]

    receipt_html = email_svc.render_payment_receipt_html(
        order_number=order.order_number,
        title=order.title,
        amount_php=receipt_data["amount_php"],
        payment_method=receipt_data["payment_method_label"],
        paid_at=receipt_data.get("paid_at_manila") or "Just now",
        reference_id=attempt.id,
        official_receipt_number=receipt_data["official_receipt_number"],
        acknowledgement_number=receipt_data["acknowledgement_number"],
        provider_payment_id=receipt_data["provider_payment_id"],
        vatable_sales_php=receipt_data["vatable_sales_php"],
        vat_amount_php=receipt_data["vat_amount_php"],
        verification_hash=receipt_data["verification_hash"],
        pdf_filename=pdf_filename,
        payment_rail=receipt_data["payment_rail"],
        ledger_status=receipt_data.get("ledger_status"),
    )

    attachments = [{"filename": pdf_filename, "content": pdf_bytes, "subtype": "pdf"}]
    dispatch_res = email_svc.send_email(
        to_email=recipient,
        subject=f"[Official Receipt & Acknowledgement] Payment Confirmed - {order.order_number}",
        html_body=receipt_html,
        attachments=attachments,
    )

    smtp_info = {
        "status": "sent" if dispatch_res.get("success") else "failed",
        "recipient": recipient,
        "sent_at": utc_now().isoformat() if dispatch_res.get("success") else None,
        "message": dispatch_res.get("message", "Dispatched"),
        "latency_ms": dispatch_res.get("latency_ms", 0),
        "details": dispatch_res.get("details"),
    }
    receipt_data["smtp_delivery"] = smtp_info
    persist_receipt_on_order(db, order, attempt, receipt_data)

    if not dispatch_res.get("success"):
        raise HTTPException(status_code=502, detail=f"Failed to dispatch email: {dispatch_res.get('message')}")

    return {
        "success": True,
        "message": f"Official Receipt & Acknowledgement PDF successfully dispatched to {recipient}",
        "recipient": recipient,
        "smtp_delivery": smtp_info,
        "receipt": receipt_data,
    }

@app.get("/api/v1/orders/{order_id}/receipt", summary="Get Order Official Receipt Data")
def get_order_receipt(order_id: str, db: Session = Depends(get_db)):
    attempt, order = _resolve_payment_attempt_and_order(db, order_id)
    if not order:
        order = db.query(Order).filter((Order.id == order_id) | (Order.order_number == order_id)).first()
    if not order:
        raise HTTPException(status_code=404, detail="Order not found.")
    target_id = attempt.id if attempt else order.id
    return get_payment_receipt(target_id, db)

@app.get("/api/v1/orders/{order_id}/receipt/pdf", summary="Download Order Official Receipt PDF")
def get_order_receipt_pdf(order_id: str, download: bool = False, db: Session = Depends(get_db)):
    attempt, order = _resolve_payment_attempt_and_order(db, order_id)
    if not order:
        order = db.query(Order).filter((Order.id == order_id) | (Order.order_number == order_id)).first()
    if not order:
        raise HTTPException(status_code=404, detail="Order not found.")
    target_id = attempt.id if attempt else order.id
    return get_payment_receipt_pdf(target_id, download=download, db=db)

@app.post("/api/v1/webhooks/paymongo", summary="PayMongo Webhook Receiver")
async def paymongo_webhook(
    request: Request,
    paymongo_signature: Optional[str] = Header(None, alias="Paymongo-Signature"),
    db: Session = Depends(get_db),
):
    raw_body = await request.body()
    if not paymongo_signature:
        raise HTTPException(status_code=401, detail="Missing Paymongo-Signature header.")

    try:
        result = orchestrator.process_webhook(
            db=db,
            raw_body=raw_body,
            signature_header=paymongo_signature,
        )
        return JSONResponse(status_code=200, content=result)
    except PermissionError as e:
        raise HTTPException(status_code=401, detail=str(e))
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Internal webhook processing error: {str(e)}")

@app.post("/api/v1/admin/payments/{payment_id}/reconcile", summary="Reconcile Payment Attempt")
async def reconcile_payment(payment_id: str, db: Session = Depends(get_db)):
    try:
        result = await orchestrator.reconcile_payment_attempt(db, payment_id)
        return result
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/v1/admin/payments/reconcile-all", summary="Reconcile All Unresolved Payments")
async def reconcile_all(db: Session = Depends(get_db)):
    try:
        results = await orchestrator.reconcile_all_unresolved(db)
        return {"reconciled_count": len(results), "details": results}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/api/v1/admin/webhook-events", summary="List Received Webhook Events")
def list_webhook_events(limit: int = 50, db: Session = Depends(get_db)):
    events = (
        db.query(WebhookEvent)
        .order_by(WebhookEvent.received_at.desc())
        .limit(limit)
        .all()
    )
    return [e.to_dict() for e in events]

@app.get("/api/v1/admin/fulfillment-outbox", summary="List Fulfillment Outbox Records")
def list_fulfillment_outbox(db: Session = Depends(get_db)):
    items = (
        db.query(FulfillmentOutbox)
        .order_by(FulfillmentOutbox.created_at.desc())
        .limit(50)
        .all()
    )
    return [i.to_dict() for i in items]

@app.post("/api/v1/admin/fulfillment-outbox/process", summary="Process Pending Fulfillment Outbox Records")
def trigger_process_fulfillment_outbox(retry_failed: bool = False, db: Session = Depends(get_db)):
    processed_count = orchestrator.process_fulfillment_outbox(db, retry_failed=retry_failed)
    return {"processed_count": processed_count, "status": "completed"}

@app.post("/api/v1/sandbox/simulate-webhook", summary="Simulate Signed PayMongo Webhook")
async def simulate_webhook(req: SimulateWebhookRequest, db: Session = Depends(get_db)):
    """
    Interactive test endpoint: creates and dispatches a valid HMAC-signed PayMongo webhook
    for testing paid, failed, expired, and duplicate event transitions live.
    """
    attempt = (
        db.query(PaymentAttempt)
        .filter(
            (PaymentAttempt.id == req.payment_attempt_id)
            | (PaymentAttempt.order_id == req.payment_attempt_id)
            | (PaymentAttempt.provider_payment_id == req.payment_attempt_id)
        )
        .order_by(PaymentAttempt.created_at.desc())
        .first()
    )
    if not attempt:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")

    order = db.query(Order).filter(Order.id == attempt.order_id).first()
    event_id = "evt_sim_fixed_duplicate" if req.duplicate else f"evt_sim_{uuid.uuid4().hex[:12]}"

    pm_code = req.payment_method or attempt.payment_method or "hosted_checkout"
    from services.receipt_service import get_payment_method_info
    pm_info = get_payment_method_info(pm_code)
    resolved_pm_code = pm_info["code"]

    if req.payment_method:
        attempt.payment_method = resolved_pm_code
        db.commit()

    payload_dict = {
        "data": {
            "id": event_id,
            "type": "event",
            "attributes": {
                "type": req.event_type,
                "livemode": False,
                "data": {
                    "id": attempt.provider_payment_id or f"cs_sim_{uuid.uuid4().hex[:12]}",
                    "type": "checkout_session",
                    "attributes": {
                        "status": "paid" if "paid" in req.event_type else ("expired" if "expired" in req.event_type else ("cancelled" if "cancel" in req.event_type else "failed")),
                        "payment_method_used": resolved_pm_code,
                        "line_items": [
                            {
                                "amount": attempt.amount_minor,
                                "currency": attempt.currency,
                                "name": order.title if order else "Simulated Order",
                            }
                        ],
                        "metadata": {
                            "order_id": attempt.order_id,
                            "payment_attempt_id": attempt.id,
                            "payment_method": resolved_pm_code,
                        },
                        "failed_code": (
                            "CARD_DECLINED" if "failed" in req.event_type
                            else ("SESSION_EXPIRED" if "expired" in req.event_type
                            else ("SESSION_CANCELLED" if "cancel" in req.event_type else None))
                        ),
                        "failed_message": (
                            "Card was declined by test bank." if "failed" in req.event_type
                            else ("Checkout session expired before completion." if "expired" in req.event_type
                            else ("Checkout session was cancelled." if "cancel" in req.event_type else None))
                        ),
                    },
                },
            },
        }
    }
    raw_body = json.dumps(payload_dict).encode("utf-8")
    provider: PayMongoProvider = orchestrator.provider  # type: ignore
    signature = provider.sign_simulated_payload(raw_body, is_live=False)

    # Process via normal webhook handler
    result = orchestrator.process_webhook(db=db, raw_body=raw_body, signature_header=signature)

    # If the simulated event is 'expired' and there is a real PayMongo provider session ID, also invalidate remote session
    if "expired" in req.event_type and attempt.provider_payment_id and not attempt.provider_payment_id.startswith("cs_sim_"):
        try:
            await orchestrator.provider.expire_checkout_session(attempt.provider_payment_id)
        except Exception as e:
            print(f"Notice: Remote gateway expire during simulate_webhook: {e}")

    return {
        "simulated_event_id": event_id,
        "signature_header": signature,
        "result": result,
    }

@app.post("/api/v1/payments/{payment_id}/expire", summary="Expire Active Payment Attempt & Remote Gateway Session")
async def expire_payment(
    payment_id: str,
    req: Optional[ExpirePaymentRequest] = None,
    db: Session = Depends(get_db),
):
    attempt, order = _resolve_payment_attempt_and_order(db, payment_id)
    if not attempt:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")
    reason = req.reason if req and req.reason else "Manual expiration / session timeout"
    try:
        updated = await orchestrator.expire_payment_attempt(db, attempt.id, reason=reason)
        return {
            "success": True,
            "status": "expired",
            "message": f"Payment attempt '{attempt.id}' authoritatively expired.",
            "attempt": updated.to_dict(),
        }
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/v1/payments/{payment_id}/fail", summary="Fail Active Payment Attempt")
async def fail_payment(
    payment_id: str,
    req: Optional[FailPaymentRequest] = None,
    db: Session = Depends(get_db),
):
    attempt, order = _resolve_payment_attempt_and_order(db, payment_id)
    if not attempt:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")
    code = (req.failure_code or req.code) if req and (req.failure_code or req.code) else "CARD_DECLINED"
    message = (req.failure_message or req.message) if req and (req.failure_message or req.message) else "Payment declined by test rail."
    try:
        updated = await orchestrator.fail_payment_attempt(db, attempt.id, code=code, message=message)
        return {
            "success": True,
            "status": "failed",
            "message": f"Payment attempt '{attempt.id}' marked failed.",
            "attempt": updated.to_dict(),
        }
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/v1/sandbox/create-and-expire-test", summary="Quick Test: Create and Expire Fresh Test Order & Attempt", status_code=status.HTTP_201_CREATED)
async def create_and_expire_test(
    channel: str = "qrph",
    db: Session = Depends(get_db),
):
    """
    Creates a fresh test order with an active payment attempt, then immediately expires it.
    Verifies that the checkout session is authoritatively invalidated and blocked.
    """
    test_id = uuid.uuid4().hex[:8]
    order = orchestrator.create_order(
        db=db,
        title=f"Expired Test Order {test_id}",
        category="product_order",
        reference_code="Product A",
        quantity=1,
        metadata={
            "customer_name": "Test Simulation User",
            "customer_email": "test.sim@example.com",
            "customer_phone": "+63 917 000 0000",
            "test_mode": "expire_simulation",
        },
    )
    attempt = await orchestrator.initiate_payment_attempt(
        db=db,
        order_id=order.id,
        idempotency_key=f"sim_exp_key_{test_id}",
        payment_method=channel,
    )
    expired_attempt = await orchestrator.expire_payment_attempt(
        db=db,
        attempt_id=attempt.id,
        reason="Interactive sandbox test expiration",
    )
    return {
        "success": True,
        "message": f"Created and expired test attempt '{expired_attempt.id}' for order '{order.order_number}'.",
        "order": order.to_dict(),
        "attempt": expired_attempt.to_dict(),
    }

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("app:app", host="0.0.0.0", port=5000, reload=True)

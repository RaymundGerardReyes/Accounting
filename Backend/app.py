import os
import json
import uuid
from pathlib import Path
from typing import Optional, List, Dict, Any
from contextlib import asynccontextmanager

from fastapi import FastAPI, Request, Response, Depends, HTTPException, Header, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from sqlalchemy.orm import Session
from dotenv import load_dotenv

# Load .env if present
load_dotenv(Path(__file__).parent / ".env")

from database import get_db
from models import init_db, Order, PaymentAttempt, WebhookEvent, FulfillmentOutbox
from services.payment_orchestrator import PaymentOrchestrator
from providers.paymongo import PayMongoProvider
from finance_routes import router as finance_router

DATA_DIR = Path(__file__).parent / "data"

# Lifespan for startup DB initialization
@asynccontextmanager
async def lifespan(app: FastAPI):
    # Initialize DB tables and seed records
    init_db()
    yield

app = FastAPI(
    title="Acme Cost Accounting & Live Payment Orchestration API",
    version="2.0.0",
    description="Unified API providing legacy cost-analysis endpoints and live PayMongo payment orchestration.",
    lifespan=lifespan,
)

# CORS middleware for Next.js frontend
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:3000", "http://127.0.0.1:3000", "*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(finance_router)

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
    metadata: Optional[Dict[str, Any]] = None

class CreatePaymentAttemptRequest(BaseModel):
    idempotency_key: Optional[str] = Field(None, description="Client-provided idempotency key")
    payment_method: str = Field("hosted_checkout", description="Payment method channel")
    success_url: Optional[str] = None
    cancel_url: Optional[str] = None

class SimulateWebhookRequest(BaseModel):
    payment_attempt_id: str
    event_type: str = Field("checkout_session.payment.paid", description="Webhook event type")
    duplicate: bool = Field(False, description="Whether to simulate duplicate event ID")

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
        order = orchestrator.create_order(
            db=db,
            title=req.title,
            category=req.category,
            reference_code=req.reference_code,
            amount_minor=req.amount_minor,
            quantity=req.quantity,
            metadata=req.metadata,
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

@app.get("/api/v1/payments/{payment_id}", summary="Poll Payment Status")
def get_payment_status(payment_id: str, db: Session = Depends(get_db)):
    attempt = db.query(PaymentAttempt).filter(PaymentAttempt.id == payment_id).first()
    if not attempt:
        raise HTTPException(status_code=404, detail="Payment attempt not found.")
    order = db.query(Order).filter(Order.id == attempt.order_id).first()
    res = attempt.to_dict()
    res["order"] = order.to_dict() if order else None
    return res

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
                        "status": "paid" if "paid" in req.event_type else "failed",
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
                        },
                        "failed_code": "CARD_DECLINED" if "failed" in req.event_type else None,
                        "failed_message": "Card was declined by test bank." if "failed" in req.event_type else None,
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
    return {
        "simulated_event_id": event_id,
        "signature_header": signature,
        "result": result,
    }

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("app:app", host="0.0.0.0", port=5000, reload=True)

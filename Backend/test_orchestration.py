import pytest
import json
import uuid
from fastapi.testclient import TestClient
from app import app
from database import Base, engine, SessionLocal
from models import Order, PaymentAttempt, WebhookEvent, FulfillmentOutbox
from providers.paymongo import PayMongoProvider

client = TestClient(app)

@pytest.fixture(autouse=True)
def setup_db():
    Base.metadata.create_all(bind=engine)
    yield
    # Cleanup between tests can be added if needed

def test_legacy_dashboard():
    response = client.get("/api/dashboard")
    assert response.status_code == 200
    data = response.json()
    assert "costTrendData" in data
    assert "departmentData" in data
    assert len(data["departmentMetrics"]) >= 4

def test_legacy_departments_no_404():
    for dept in ["manufacturing", "assembly", "qa", "logistics"]:
        response = client.get(f"/api/department/{dept}")
        assert response.status_code == 200, f"Department '{dept}' returned {response.status_code}"
        data = response.json()
        assert "costBreakdown" in data
        assert "departmentStats" in data

def test_legacy_products_and_variance():
    resp_prod = client.get("/api/products")
    assert resp_prod.status_code == 200
    assert "productData" in resp_prod.json()

    resp_var = client.get("/api/variance")
    assert resp_var.status_code == 200
    assert "varianceData" in resp_var.json()

def test_legacy_configuration_get_and_put():
    resp_cfg = client.get("/api/configuration")
    assert resp_cfg.status_code == 200
    cfg_data = resp_cfg.json()
    assert cfg_data["config"]["reportingCurrency"] == "PHP"

    # Test PUT configuration
    update_resp = client.put("/api/configuration", json={"config": {"companyName": "Acme Manufacturing Inc."}})
    assert update_resp.status_code == 200
    assert update_resp.json()["config"]["companyName"] == "Acme Manufacturing Inc."

def test_order_creation_server_side_pricing():
    # Product order with automatic unit price lookup for Product A (₱125 -> ₱125.00 * 10 = ₱1,250.00 = 125,000 centavos)
    resp = client.post("/api/v1/orders", json={
        "title": "Test Product A Purchase",
        "category": "product_order",
        "reference_code": "Product A",
        "quantity": 10
    })
    assert resp.status_code == 201
    order = resp.json()
    assert order["amount_minor"] == 125000
    assert order["currency"] == "PHP"
    assert order["status"] == "pending"

def test_payment_attempt_and_idempotency():
    # 1. Create order
    resp = client.post("/api/v1/orders", json={
        "title": "Idempotency Test Order",
        "category": "product_order",
        "reference_code": "Product B",
        "quantity": 5
    })
    order_id = resp.json()["id"]

    # 2. First attempt with idempotency key
    key = f"idemp_{uuid.uuid4().hex}"
    attempt_resp1 = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": key}
    )
    assert attempt_resp1.status_code == 201
    attempt1 = attempt_resp1.json()
    assert attempt1["status"] in ("requires_action", "pending")
    assert attempt1["checkout_url"] is not None

    # 3. Second attempt with exact same idempotency key must return same attempt
    attempt_resp2 = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": key}
    )
    assert attempt_resp2.status_code == 201
    attempt2 = attempt_resp2.json()
    assert attempt2["id"] == attempt1["id"]
    assert attempt2["provider_payment_id"] == attempt1["provider_payment_id"]

def test_webhook_hmac_verification_and_state_transitions():
    # 1. Create order and attempt
    resp_order = client.post("/api/v1/orders", json={
        "title": "Webhook Transition Order",
        "category": "product_order",
        "reference_code": "Product A",
        "quantity": 2
    })
    order_id = resp_order.json()["id"]

    key = f"idemp_{uuid.uuid4().hex}"
    resp_att = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": key}
    )
    attempt_id = resp_att.json()["id"]
    provider_payment_id = resp_att.json()["provider_payment_id"]

    # 2. Forged signature should be rejected (401)
    forged_body = json.dumps({"data": {"id": "evt_forged", "attributes": {"type": "payment.paid"}}}).encode("utf-8")
    forged_resp = client.post(
        "/api/v1/webhooks/paymongo",
        content=forged_body,
        headers={"Paymongo-Signature": "t=123456,te=invalid_forged_hash", "Content-Type": "application/json"}
    )
    assert forged_resp.status_code == 401

    # 3. Valid HMAC signed webhook (checkout_session.payment.paid)
    provider = PayMongoProvider()
    event_id = f"evt_{uuid.uuid4().hex}"
    valid_payload = {
        "data": {
            "id": event_id,
            "type": "event",
            "attributes": {
                "type": "checkout_session.payment.paid",
                "data": {
                    "id": provider_payment_id,
                    "type": "checkout_session",
                    "attributes": {
                        "status": "paid",
                        "metadata": {
                            "order_id": order_id,
                            "payment_attempt_id": attempt_id
                        }
                    }
                }
            }
        }
    }
    raw_body = json.dumps(valid_payload).encode("utf-8")
    valid_sig = provider.sign_simulated_payload(raw_body)

    wh_resp = client.post(
        "/api/v1/webhooks/paymongo",
        content=raw_body,
        headers={"Paymongo-Signature": valid_sig, "Content-Type": "application/json"}
    )
    assert wh_resp.status_code == 200
    assert wh_resp.json()["status"] == "succeeded"

    # 4. Verify order is marked paid and attempt is succeeded
    poll_resp = client.get(f"/api/v1/payments/{attempt_id}")
    assert poll_resp.status_code == 200
    payment_data = poll_resp.json()
    assert payment_data["status"] == "succeeded"
    assert payment_data["order"]["status"] == "paid"

    # 5. Webhook deduplication: sending exact same event_id again returns duplicate: True
    wh_dup_resp = client.post(
        "/api/v1/webhooks/paymongo",
        content=raw_body,
        headers={"Paymongo-Signature": valid_sig, "Content-Type": "application/json"}
    )
    assert wh_dup_resp.status_code == 200
    assert wh_dup_resp.json().get("duplicate") is True

    # 6. Monotonic Terminal States: A late payment.failed event must NOT revert 'succeeded'
    late_failed_payload = {
        "data": {
            "id": f"evt_late_{uuid.uuid4().hex}",
            "type": "event",
            "attributes": {
                "type": "payment.failed",
                "data": {
                    "id": provider_payment_id,
                    "attributes": {
                        "failed_code": "LATE_FAILURE",
                        "metadata": {
                            "order_id": order_id,
                            "payment_attempt_id": attempt_id
                        }
                    }
                }
            }
        }
    }
    late_raw = json.dumps(late_failed_payload).encode("utf-8")
    late_sig = provider.sign_simulated_payload(late_raw)
    late_resp = client.post(
        "/api/v1/webhooks/paymongo",
        content=late_raw,
        headers={"Paymongo-Signature": late_sig, "Content-Type": "application/json"}
    )
    assert late_resp.status_code == 200
    # Attempt remains 'succeeded'
    poll_resp_after = client.get(f"/api/v1/payments/{attempt_id}")
    assert poll_resp_after.json()["status"] == "succeeded"
    assert poll_resp_after.json()["order"]["status"] == "paid"


def test_reconciliation_and_outbox():
    # Create order and attempt
    resp_order = client.post("/api/v1/orders", json={
        "title": "Reconciliation Test Order",
        "category": "product_order",
        "reference_code": "Product C",
        "quantity": 1
    })
    order_id = resp_order.json()["id"]

    resp_att = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"reconcile_{uuid.uuid4().hex}"}
    )
    attempt_id = resp_att.json()["id"]

    # Reconcile attempt
    recon_resp = client.post(f"/api/v1/admin/payments/{attempt_id}/reconcile")
    assert recon_resp.status_code == 200

    # Reconcile all
    recon_all_resp = client.post("/api/v1/admin/payments/reconcile-all")
    assert recon_all_resp.status_code == 200
    assert "reconciled_count" in recon_all_resp.json()

    # Fulfillment outbox list
    outbox_resp = client.get("/api/v1/admin/fulfillment-outbox")
    assert outbox_resp.status_code == 200
    assert isinstance(outbox_resp.json(), list)


def test_get_order_detail_includes_latest_attempt():
    resp_order = client.post("/api/v1/orders", json={
        "title": "Detail Attempt Test Order",
        "category": "product_order",
        "reference_code": "Product D",
        "quantity": 1
    })
    order_id = resp_order.json()["id"]

    att_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"detail_test_{uuid.uuid4().hex}"}
    )
    attempt_id = att_resp.json()["id"]

    get_resp = client.get(f"/api/v1/orders/{order_id}")
    assert get_resp.status_code == 200
    detail = get_resp.json()
    assert "latest_attempt" in detail
    assert detail["latest_attempt"] is not None
    assert detail["latest_attempt"]["id"] == attempt_id
    assert len(detail["attempts"]) >= 1


def test_idempotency_conflict_across_different_orders():
    resp1 = client.post("/api/v1/orders", json={
        "title": "Order 1",
        "category": "product_order",
        "reference_code": "Product A",
        "quantity": 1
    })
    order_id_1 = resp1.json()["id"]

    resp2 = client.post("/api/v1/orders", json={
        "title": "Order 2",
        "category": "product_order",
        "reference_code": "Product B",
        "quantity": 1
    })
    order_id_2 = resp2.json()["id"]

    shared_key = f"shared_idemp_{uuid.uuid4().hex}"

    # First order uses the key -> 201 Created
    att1 = client.post(
        f"/api/v1/orders/{order_id_1}/payment-attempts",
        json={"idempotency_key": shared_key}
    )
    assert att1.status_code == 201

    # Second order attempts to reuse the same key -> Must be rejected (400)
    att2 = client.post(
        f"/api/v1/orders/{order_id_2}/payment-attempts",
        json={"idempotency_key": shared_key}
    )
    assert att2.status_code == 400
    assert "already in use for another order" in att2.json()["detail"]


def test_payment_attempt_on_cancelled_order_rejected():
    resp = client.post("/api/v1/orders", json={
        "title": "Cancelled Order Test",
        "category": "product_order",
        "reference_code": "Product A",
        "quantity": 1
    })
    order_id = resp.json()["id"]

    # Cancel the order directly in DB
    db = SessionLocal()
    ord_obj = db.query(Order).filter(Order.id == order_id).first()
    ord_obj.status = "cancelled"
    db.commit()
    db.close()

    # Attempt to create payment attempt must fail
    att = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"canc_test_{uuid.uuid4().hex}"}
    )
    assert att.status_code == 400
    assert "cancelled" in att.json()["detail"].lower()


def test_department_invoice_pricing_lookup():
    resp = client.post("/api/v1/orders", json={
        "title": "Logistics Monthly Settlement",
        "category": "department_invoice",
        "reference_code": "logistics"
    })
    assert resp.status_code == 201
    order = resp.json()
    # Logistics default is ₱10,000.00 = 1,000,000 centavos
    assert order["amount_minor"] == 1000000
    assert order["currency"] == "PHP"


def test_terminal_state_immutability_expired_and_failed():
    provider = PayMongoProvider()
    resp_order = client.post("/api/v1/orders", json={
        "title": "Terminal State Order",
        "category": "product_order",
        "reference_code": "Product E",
        "quantity": 1
    })
    order_id = resp_order.json()["id"]

    att_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"term_key_{uuid.uuid4().hex}"}
    )
    attempt_id = att_resp.json()["id"]
    provider_id = att_resp.json()["provider_payment_id"]

    # 1. Expire the attempt
    exp_payload = {
        "data": {
            "id": f"evt_exp_{uuid.uuid4().hex}",
            "type": "event",
            "attributes": {
                "type": "checkout_session.expired",
                "data": {
                    "id": provider_id,
                    "attributes": {
                        "metadata": {"payment_attempt_id": attempt_id, "order_id": order_id}
                    }
                }
            }
        }
    }
    raw_exp = json.dumps(exp_payload).encode("utf-8")
    sig_exp = provider.sign_simulated_payload(raw_exp)
    wh_exp = client.post(
        "/api/v1/webhooks/paymongo",
        content=raw_exp,
        headers={"Paymongo-Signature": sig_exp, "Content-Type": "application/json"}
    )
    assert wh_exp.status_code == 200
    assert client.get(f"/api/v1/payments/{attempt_id}").json()["status"] == "expired"

    # 2. Delayed payment.failed must NOT overwrite expired status
    fail_payload = {
        "data": {
            "id": f"evt_late_fail_{uuid.uuid4().hex}",
            "type": "event",
            "attributes": {
                "type": "payment.failed",
                "data": {
                    "id": provider_id,
                    "attributes": {
                        "failed_code": "LATE_DECLINE",
                        "metadata": {"payment_attempt_id": attempt_id, "order_id": order_id}
                    }
                }
            }
        }
    }
    raw_fail = json.dumps(fail_payload).encode("utf-8")
    sig_fail = provider.sign_simulated_payload(raw_fail)
    wh_fail = client.post(
        "/api/v1/webhooks/paymongo",
        content=raw_fail,
        headers={"Paymongo-Signature": sig_fail, "Content-Type": "application/json"}
    )
    assert wh_fail.status_code == 200

    # Verify attempt remains 'expired'
    poll = client.get(f"/api/v1/payments/{attempt_id}").json()
    assert poll["status"] == "expired"


def test_simulate_webhook_with_order_id_fallback():
    resp_order = client.post("/api/v1/orders", json={
        "title": "Simulate Fallback Order",
        "category": "product_order",
        "reference_code": "Product B",
        "quantity": 2
    })
    order_id = resp_order.json()["id"]

    att_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"fallback_sim_{uuid.uuid4().hex}"}
    )
    attempt_id = att_resp.json()["id"]

    # Simulate passing order_id instead of payment_attempt_id
    sim_resp = client.post(
        "/api/v1/sandbox/simulate-webhook",
        json={"payment_attempt_id": order_id, "event_type": "checkout_session.payment.paid"}
    )
    assert sim_resp.status_code == 200
    assert sim_resp.json()["result"]["status"] == "succeeded"

    # Verify order is paid
    poll = client.get(f"/api/v1/payments/{attempt_id}").json()
    assert poll["status"] == "succeeded"
    assert poll["order"]["status"] == "paid"



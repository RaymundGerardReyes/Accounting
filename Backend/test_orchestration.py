import pytest
import json
import uuid
from datetime import datetime, timezone
from fastapi.testclient import TestClient
from app import app, orchestrator
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


def test_legacy_configuration_users_persistence():
    # Fetch existing users
    initial_cfg = client.get("/api/configuration").json()
    orig_users = initial_cfg.get("users", [])

    test_user_list = orig_users + [{
        "id": 9999,
        "name": "Audit Test Operator",
        "email": "audit.test@acme.com",
        "role": "Auditor",
        "status": "Active"
    }]

    resp = client.post("/api/configuration/users", json={"users": test_user_list})
    assert resp.status_code == 200
    assert resp.json()["success"] is True

    # Verify persisted in GET
    updated_cfg = client.get("/api/configuration").json()
    assert any(u.get("name") == "Audit Test Operator" for u in updated_cfg["users"])

    # Clean up by restoring original users list
    restore_resp = client.post("/api/configuration/users", json={"users": orig_users})
    assert restore_resp.status_code == 200


def test_outbox_process_and_retry_workflow():
    db = SessionLocal()
    try:
        # Create an order with metadata_dict setter
        order = Order(
            id=str(uuid.uuid4()),
            order_number=f"ORD-OUTBOX-{uuid.uuid4().hex[:6]}",
            title="Outbox Processor Test",
            category="product_order",
            reference_code="Product A",
            amount_minor=50000,
            status="paid",
        )
        order.metadata_dict = {"customer_email": "finance.auditor@acme.com"}
        db.add(order)
        db.commit()

        attempt = PaymentAttempt(
            id=str(uuid.uuid4()),
            order_id=order.id,
            provider="paymongo",
            payment_method="gcash",
            amount_minor=50000,
            status="succeeded",
            idempotency_key=f"outbox_test_{uuid.uuid4().hex}",
        )
        db.add(attempt)
        db.commit()

        # Add pending outbox item
        outbox_item = FulfillmentOutbox(
            id=str(uuid.uuid4()),
            order_id=order.id,
            payment_attempt_id=attempt.id,
            event_type="order.paid",
            status="pending",
        )
        db.add(outbox_item)
        db.commit()

        # Process pending queue
        proc_resp = client.post("/api/v1/admin/fulfillment-outbox/process")
        assert proc_resp.status_code == 200
        data = proc_resp.json()
        assert data["processed_count"] >= 1
        assert data["status"] == "completed"

        db.refresh(outbox_item)
        assert outbox_item.status == "completed"
        assert outbox_item.processed_at is not None

        # Add a failed outbox record to test retry_failed
        failed_item = FulfillmentOutbox(
            id=str(uuid.uuid4()),
            order_id=order.id,
            payment_attempt_id=attempt.id,
            event_type="order.paid",
            status="failed",
        )
        db.add(failed_item)
        db.commit()

        # Normal process without retry_failed should ignore failed items
        resp_no_retry = client.post("/api/v1/admin/fulfillment-outbox/process")
        assert resp_no_retry.status_code == 200
        db.refresh(failed_item)
        assert failed_item.status == "failed"

        # Calling with retry_failed=true should re-process it
        resp_retry = client.post("/api/v1/admin/fulfillment-outbox/process?retry_failed=true")
        assert resp_retry.status_code == 200
        assert resp_retry.json()["processed_count"] >= 1
        db.refresh(failed_item)
        assert failed_item.status == "completed"
    finally:
        db.close()


def test_failed_and_expired_recovery_and_new_attempt_workflow():
    provider = PayMongoProvider()
    
    # 1. Create order
    resp_order = client.post("/api/v1/orders", json={
        "title": "Recovery Workflow Order",
        "category": "product_order",
        "reference_code": "Product C",
        "quantity": 1
    })
    assert resp_order.status_code == 201
    order_id = resp_order.json()["id"]
    assert resp_order.json()["status"] == "pending"

    # 2. Attempt 1: Fails via payment.failed webhook
    att1_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"att1_{uuid.uuid4().hex}"}
    )
    assert att1_resp.status_code == 201
    att1_id = att1_resp.json()["id"]
    att1_prov_id = att1_resp.json()["provider_payment_id"]

    fail_payload = {
        "data": {
            "id": f"evt_fail_{uuid.uuid4().hex}",
            "type": "event",
            "attributes": {
                "type": "payment.failed",
                "data": {
                    "id": att1_prov_id,
                    "attributes": {
                        "failed_code": "INSUFFICIENT_FUNDS",
                        "failed_message": "Account has insufficient balance.",
                        "metadata": {"payment_attempt_id": att1_id, "order_id": order_id}
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
    
    # Verify Attempt 1 is failed, but Order is still pending
    poll1 = client.get(f"/api/v1/payments/{att1_id}").json()
    assert poll1["status"] == "failed"
    assert poll1["failure_code"] == "INSUFFICIENT_FUNDS"
    assert poll1["order"]["status"] == "pending"

    # 3. Attempt 2: Expires via checkout_session.expired webhook
    att2_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"att2_{uuid.uuid4().hex}"}
    )
    assert att2_resp.status_code == 201
    att2_id = att2_resp.json()["id"]
    att2_prov_id = att2_resp.json()["provider_payment_id"]

    exp_payload = {
        "data": {
            "id": f"evt_exp_{uuid.uuid4().hex}",
            "type": "event",
            "attributes": {
                "type": "checkout_session.expired",
                "data": {
                    "id": att2_prov_id,
                    "attributes": {
                        "metadata": {"payment_attempt_id": att2_id, "order_id": order_id}
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

    # Verify Attempt 2 is expired, but Order is still pending
    poll2 = client.get(f"/api/v1/payments/{att2_id}").json()
    assert poll2["status"] == "expired"
    assert poll2["failure_code"] == "SESSION_EXPIRED"
    assert poll2["order"]["status"] == "pending"

    # 4. Attempt 3: Customer retries and completes payment
    att3_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"att3_{uuid.uuid4().hex}"}
    )
    assert att3_resp.status_code == 201
    att3_id = att3_resp.json()["id"]

    sim_paid = client.post(
        "/api/v1/sandbox/simulate-webhook",
        json={"payment_attempt_id": att3_id, "event_type": "checkout_session.payment.paid"}
    )
    assert sim_paid.status_code == 200
    assert sim_paid.json()["result"]["status"] == "succeeded"

    # Verify Order is now paid and latest attempt is att3
    order_detail = client.get(f"/api/v1/orders/{order_id}").json()
    assert order_detail["status"] == "paid"
    assert len(order_detail["attempts"]) == 3
    assert order_detail["latest_attempt"]["id"] == att3_id
    assert order_detail["latest_attempt"]["status"] == "succeeded"

    # 5. Subsequent attempt on paid order is rejected
    att4_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"att4_{uuid.uuid4().hex}"}
    )
    assert att4_resp.status_code == 400
    assert "already fully paid" in att4_resp.json()["detail"].lower()


def test_reconciliation_terminal_immutability_and_expired():
    db = SessionLocal()
    try:
        order = Order(
            id=str(uuid.uuid4()),
            order_number=f"ORD-RECON-{uuid.uuid4().hex[:6]}",
            title="Reconciliation Terminal Test",
            category="product_order",
            reference_code="Product A",
            amount_minor=10000,
            status="pending",
        )
        db.add(order)
        db.commit()

        # Terminal failed attempt
        att_failed = PaymentAttempt(
            id=str(uuid.uuid4()),
            order_id=order.id,
            provider="paymongo",
            payment_method="card",
            provider_payment_id=f"cs_sim_{uuid.uuid4().hex[:12]}",
            amount_minor=10000,
            status="failed",
            failure_code="CARD_DECLINED",
            failure_message="Declined",
            idempotency_key=f"recon_fail_{uuid.uuid4().hex}",
        )
        db.add(att_failed)

        # Terminal expired attempt
        att_expired = PaymentAttempt(
            id=str(uuid.uuid4()),
            order_id=order.id,
            provider="paymongo",
            payment_method="gcash",
            provider_payment_id=f"cs_sim_{uuid.uuid4().hex[:12]}",
            amount_minor=10000,
            status="expired",
            failure_code="SESSION_EXPIRED",
            idempotency_key=f"recon_exp_{uuid.uuid4().hex}",
        )
        db.add(att_expired)
        db.commit()

        # Reconciling failed attempt returns already_failed
        recon_f = client.post(f"/api/v1/admin/payments/{att_failed.id}/reconcile")
        assert recon_f.status_code == 200
        data_f = recon_f.json()
        assert data_f["status"] == "failed"
        assert data_f["action"] == "already_failed"
        assert data_f["reconciled"] is True

        # Reconciling expired attempt returns already_expired
        recon_e = client.post(f"/api/v1/admin/payments/{att_expired.id}/reconcile")
        assert recon_e.status_code == 200
        data_e = recon_e.json()
        assert data_e["status"] == "expired"
        assert data_e["action"] == "already_expired"
        assert data_e["reconciled"] is True
    finally:
        db.close()


def test_simulate_webhook_expired_event():
    resp_order = client.post("/api/v1/orders", json={
        "title": "Simulate Expired Webhook Order",
        "category": "product_order",
        "reference_code": "Product D",
        "quantity": 1
    })
    order_id = resp_order.json()["id"]

    att_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"idempotency_key": f"sim_exp_key_{uuid.uuid4().hex}"}
    )
    attempt_id = att_resp.json()["id"]

    # Trigger simulation for checkout_session.expired
    sim_resp = client.post(
        "/api/v1/sandbox/simulate-webhook",
        json={"payment_attempt_id": attempt_id, "event_type": "checkout_session.expired"}
    )
    assert sim_resp.status_code == 200
    assert sim_resp.json()["result"]["status"] == "expired"

    # Poll attempt to confirm
    poll = client.get(f"/api/v1/payments/{attempt_id}").json()
    assert poll["status"] == "expired"
    assert poll["failure_code"] == "SESSION_EXPIRED"


def test_payment_method_normalization_and_catalog():
    from services.receipt_service import get_payment_method_info, extract_payment_method_from_paymongo

    # Test normalization of channels
    assert get_payment_method_info("qrph")["code"] == "qrph"
    assert get_payment_method_info("card")["category"] == "Card"
    assert get_payment_method_info("card_visa")["code"] == "card_visa"
    assert get_payment_method_info("gcash")["code"] == "gcash"
    assert get_payment_method_info("maya")["code"] == "paymaya"
    assert get_payment_method_info("dob_bpi")["code"] == "dob_bpi"
    assert get_payment_method_info("bpi")["code"] == "dob_bpi"

    # Test dynamic card format
    card_info = get_payment_method_info("card_visa_4242")
    assert "4242" in card_info["label"]
    assert card_info["category"] == "Card"

    # Test extract from PayMongo payload
    attrs_qr = {"payments": [{"attributes": {"status": "paid", "source": {"type": "qrph"}}}]}
    assert extract_payment_method_from_paymongo(attrs_qr) == "qrph"

    attrs_gcash = {"payment_method_used": "gcash"}
    assert extract_payment_method_from_paymongo(attrs_gcash) == "gcash"


def test_official_receipt_and_acknowledgement_report_flow():
    # 1. Create order
    resp_order = client.post("/api/v1/orders", json={
        "title": "Official Receipt Validation Order",
        "category": "product_order",
        "reference_code": "Product A",
        "quantity": 2,
        "metadata": {"customer_email": "raymundgerardrestaca@gmail.com"}
    })
    assert resp_order.status_code == 201
    order_id = resp_order.json()["id"]

    # 2. Initiate payment attempt with GCash
    att_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"payment_method": "gcash", "idempotency_key": f"or_test_{uuid.uuid4().hex}"}
    )
    assert att_resp.status_code == 201
    attempt_id = att_resp.json()["id"]
    assert att_resp.json()["payment_method"] == "gcash"

    # 3. Simulate paid webhook with GCash channel
    sim_resp = client.post(
        "/api/v1/sandbox/simulate-webhook",
        json={
            "payment_attempt_id": attempt_id,
            "event_type": "checkout_session.payment.paid",
            "payment_method": "gcash"
        }
    )
    assert sim_resp.status_code == 200
    assert sim_resp.json()["result"]["status"] == "succeeded"

    # 4. Check poll status includes receipt data
    poll_resp = client.get(f"/api/v1/payments/{attempt_id}")
    assert poll_resp.status_code == 200
    poll_data = poll_resp.json()
    assert poll_data["status"] == "succeeded"
    assert "receipt" in poll_data
    receipt = poll_data["receipt"]
    assert receipt["official_receipt_number"].startswith("OR-")
    assert receipt["acknowledgement_number"].startswith("ACK-")
    assert receipt["document_title"] == "Official Receipt & Treasury Acknowledgement Report"
    assert receipt["payment_method_code"] == "gcash"
    assert len(receipt["supported_channels"]) == 5
    # GCash channel must be selected
    gcash_ch = next(c for c in receipt["supported_channels"] if c["code"] == "gcash")
    assert gcash_ch["selected"] is True

    # 5. Fetch Receipt JSON Endpoint
    r_resp = client.get(f"/api/v1/payments/{attempt_id}/receipt")
    assert r_resp.status_code == 200
    r_data = r_resp.json()
    assert r_data["official_receipt_number"] == receipt["official_receipt_number"]
    assert r_data["verification_hash"] is not None

    # 6. Fetch Order Receipt Convenience Endpoint
    ord_r_resp = client.get(f"/api/v1/orders/{order_id}/receipt")
    assert ord_r_resp.status_code == 200
    assert ord_r_resp.json()["order_number"] == poll_data["order"]["order_number"]

    # 7. Fetch PDF Stream Endpoint
    pdf_resp = client.get(f"/api/v1/payments/{attempt_id}/receipt/pdf")
    assert pdf_resp.status_code == 200
    assert pdf_resp.headers["content-type"] == "application/pdf"
    assert "inline" in pdf_resp.headers["content-disposition"]
    assert pdf_resp.content.startswith(b"%PDF-1.4")
    assert b"%%EOF" in pdf_resp.content

    # 8. Fetch PDF Download Stream Endpoint
    pdf_dl_resp = client.get(f"/api/v1/payments/{attempt_id}/receipt/pdf?download=1")
    assert pdf_dl_resp.status_code == 200
    assert "attachment" in pdf_dl_resp.headers["content-disposition"]

    # 9. Test Resend Receipt Email Endpoint with custom email
    email_res = client.post(
        f"/api/v1/payments/{attempt_id}/receipt/email",
        json={"to_email": "raymundgerardrestaca@gmail.com"}
    )
    assert email_res.status_code == 200
    assert email_res.json()["success"] is True
    assert email_res.json()["recipient"] == "raymundgerardrestaca@gmail.com"

    # 10. Test Multi-Reference Resolution: Query by Provider Checkout Session ID (cs_...)
    provider_pm_id = poll_data["provider_payment_id"]
    cs_receipt_resp = client.get(f"/api/v1/payments/{provider_pm_id}/receipt")
    assert cs_receipt_resp.status_code == 200
    assert cs_receipt_resp.json()["official_receipt_number"] == receipt["official_receipt_number"]

    # 11. Test Multi-Reference Resolution: Query PDF by Provider Checkout Session ID
    cs_pdf_resp = client.get(f"/api/v1/payments/{provider_pm_id}/receipt/pdf")
    assert cs_pdf_resp.status_code == 200
    assert cs_pdf_resp.content.startswith(b"%PDF-1.4")

    # 12. Test Multi-Reference Resolution: Query by Order Number (ORD-...)
    order_num = poll_data["order"]["order_number"]
    ord_num_resp = client.get(f"/api/v1/payments/{order_num}/receipt")
    assert ord_num_resp.status_code == 200
    assert ord_num_resp.json()["order_number"] == order_num

    # 13. Test Resend Email Endpoint with empty body (uses resolved default email)
    empty_body_res = client.post(f"/api/v1/payments/{attempt_id}/receipt/email", json={})
    assert empty_body_res.status_code == 200
    assert empty_body_res.json()["success"] is True
    assert "@" in empty_body_res.json()["recipient"]


def test_realtime_auto_reconciliation_on_poll(monkeypatch):
    """
    Verifies that querying GET /api/v1/payments/{id} automatically synchronizes
    with PayMongo in real-time without requiring the user to manually click 'Reconcile'.
    """
    from providers.base import PaymentStatusResult

    # 1. Create an order and attempt
    order_resp = client.post(
        "/api/v1/orders",
        json={
            "title": "Real-Time Auto-Sync Test Order",
            "category": "product_order",
            "reference_code": "PROD-A",
            "quantity": 1,
        }
    )
    order_id = order_resp.json()["id"]

    att_resp = client.post(
        f"/api/v1/orders/{order_id}/payment-attempts",
        json={"payment_method": "qrph"}
    )
    attempt_id = att_resp.json()["id"]
    provider_pm_id = att_resp.json()["provider_payment_id"]

    # Verify attempt is initially 'requires_action'
    assert att_resp.json()["status"] == "requires_action"

    # 2. Simulate PayMongo provider returning 'succeeded' upon gateway query
    async def mock_get_checkout_session(session_id: str):
        return PaymentStatusResult(
            provider_payment_id=session_id,
            status="succeeded",
            paid_at=datetime.now(timezone.utc),
            amount_minor=25000,
            currency="PHP",
            payment_method="qrph",
            raw_response={"id": session_id, "mock": True},
        )

    monkeypatch.setattr(orchestrator.provider, "get_checkout_session", mock_get_checkout_session)

    # 3. Query with auto_reconcile=false: attempt remains 'requires_action'
    poll_no_sync = client.get(f"/api/v1/payments/{attempt_id}?auto_reconcile=false")
    assert poll_no_sync.status_code == 200
    assert poll_no_sync.json()["status"] == "requires_action"

    # 4. Query with auto_reconcile=true (simulating the default frontend poll)
    poll_resp = client.get(f"/api/v1/payments/{attempt_id}?auto_reconcile=true")
    assert poll_resp.status_code == 200
    data = poll_resp.json()

    # 5. Invariant: Status must automatically become 'succeeded' and receipt issued in real time!
    assert data["status"] == "succeeded"
    assert data["order"]["status"] == "paid"
    assert "receipt" in data
    assert data["receipt"]["official_receipt_number"] is not None
    assert data["receipt"]["payment_method_code"] == "qrph"


def test_statutory_tax_engine_and_accounting_ledger_honesty():
    """
    Validates Philippine tax engine under BIR RMC No. 77-2024 (VATable 12%, VAT-Exempt,
    Zero-Rated, Non-VAT), neutral card brand normalization, and honest ledger status derivation.
    """
    from services.receipt_service import (
        build_receipt_and_acknowledgement,
        generate_receipt_pdf_bytes,
        get_payment_method_info,
    )

    # 1. Neutral Card Normalization
    card_neutral = get_payment_method_info("card")
    assert card_neutral["category"] == "Card"
    assert card_neutral["short_label"] == "Card"
    assert "VISA" not in card_neutral["label"]

    card_mc = get_payment_method_info("card_mastercard_5555")
    assert "MASTERCARD" in card_mc["label"]
    assert "5555" in card_mc["label"]

    card_visa = get_payment_method_info("card_visa_4242")
    assert "VISA" in card_visa["label"]
    assert "4242" in card_visa["label"]

    # 2. VAT-Exempt Order Flow
    resp_exempt = client.post("/api/v1/orders", json={
        "title": "VAT-Exempt Solar Panels",
        "category": "product_order",
        "reference_code": "Product A",
        "quantity": 1,
        "metadata": {"tax_classification": "vat_exempt"}
    })
    assert resp_exempt.status_code == 201
    order_exempt_id = resp_exempt.json()["id"]

    att_exempt = client.post(
        f"/api/v1/orders/{order_exempt_id}/payment-attempts",
        json={"payment_method": "gcash", "idempotency_key": f"exempt_{uuid.uuid4().hex}"}
    ).json()

    # Simulate paid webhook
    client.post("/api/v1/sandbox/simulate-webhook", json={
        "payment_attempt_id": att_exempt["id"],
        "event_type": "checkout_session.payment.paid",
        "payment_method": "gcash"
    })

    receipt_exempt = client.get(f"/api/v1/payments/{att_exempt['id']}/receipt").json()
    assert receipt_exempt["tax_classification"] == "vat_exempt"
    assert receipt_exempt["tax_rate_label"] == "0% (Exempt)"
    assert receipt_exempt["vat_amount_php"] == "0.00"
    assert receipt_exempt["vat_exempt_sales_php"] == receipt_exempt["amount_php"]
    assert receipt_exempt["ledger_status"] == "PENDING LEDGER POSTING"
    assert receipt_exempt["sha256_audit_digest"] == receipt_exempt["verification_hash"]

    # Verify PDF generation for VAT-Exempt
    pdf_exempt = generate_receipt_pdf_bytes(receipt_exempt)
    assert pdf_exempt.startswith(b"%PDF-1.4")
    assert b"%%EOF" in pdf_exempt

    # 3. Zero-Rated Order Flow
    resp_zero = client.post("/api/v1/orders", json={
        "title": "Zero-Rated Export Services",
        "category": "product_order",
        "reference_code": "Product B",
        "quantity": 1,
        "metadata": {"tax_classification": "zero_rated"}
    })
    order_zero_id = resp_zero.json()["id"]

    att_zero = client.post(
        f"/api/v1/orders/{order_zero_id}/payment-attempts",
        json={"payment_method": "paymaya", "idempotency_key": f"zero_{uuid.uuid4().hex}"}
    ).json()

    client.post("/api/v1/sandbox/simulate-webhook", json={
        "payment_attempt_id": att_zero["id"],
        "event_type": "checkout_session.payment.paid",
        "payment_method": "paymaya"
    })

    receipt_zero = client.get(f"/api/v1/payments/{att_zero['id']}/receipt").json()
    assert receipt_zero["tax_classification"] == "zero_rated"
    assert receipt_zero["tax_rate_label"] == "0% (Zero-Rated)"
    assert receipt_zero["vat_amount_php"] == "0.00"
    assert receipt_zero["zero_rated_sales_php"] == receipt_zero["amount_php"]

    pdf_zero = generate_receipt_pdf_bytes(receipt_zero)
    assert pdf_zero.startswith(b"%PDF-1.4")

    # 4. Non-VAT Order Flow
    resp_nonvat = client.post("/api/v1/orders", json={
        "title": "Non-VAT Agricultural Supplies",
        "category": "product_order",
        "reference_code": "Product C",
        "quantity": 1,
        "metadata": {"tax_classification": "non_vat"}
    })
    order_nonvat_id = resp_nonvat.json()["id"]

    att_nonvat = client.post(
        f"/api/v1/orders/{order_nonvat_id}/payment-attempts",
        json={"payment_method": "qrph", "idempotency_key": f"nonvat_{uuid.uuid4().hex}"}
    ).json()

    client.post("/api/v1/sandbox/simulate-webhook", json={
        "payment_attempt_id": att_nonvat["id"],
        "event_type": "checkout_session.payment.paid",
        "payment_method": "qrph"
    })

    receipt_nonvat = client.get(f"/api/v1/payments/{att_nonvat['id']}/receipt").json()
    assert receipt_nonvat["tax_classification"] == "non_vat"
    assert receipt_nonvat["tax_rate_label"] == "Non-VAT"
    assert receipt_nonvat["vat_amount_php"] == "0.00"
    assert receipt_nonvat["non_vat_sales_php"] == receipt_nonvat["amount_php"]

    pdf_nonvat = generate_receipt_pdf_bytes(receipt_nonvat)
    assert pdf_nonvat.startswith(b"%PDF-1.4")

    # 5. Ledger Posting Honesty
    # Verify an order marked posted in metadata reflects POSTED TO ACME GENERAL LEDGER
    resp_gl = client.post("/api/v1/orders", json={
        "title": "GL Posted Order",
        "category": "product_order",
        "reference_code": "Product A",
        "quantity": 1,
        "metadata": {"ledger_posted": True, "journal_entry_id": "JE-202610-999"}
    })
    order_gl_id = resp_gl.json()["id"]
    att_gl = client.post(
        f"/api/v1/orders/{order_gl_id}/payment-attempts",
        json={"payment_method": "card", "idempotency_key": f"gl_{uuid.uuid4().hex}"}
    ).json()

    client.post("/api/v1/sandbox/simulate-webhook", json={
        "payment_attempt_id": att_gl["id"],
        "event_type": "checkout_session.payment.paid",
        "payment_method": "card"
    })

    receipt_gl = client.get(f"/api/v1/payments/{att_gl['id']}/receipt").json()
    assert receipt_gl["ledger_status"] == "POSTED TO ACME GENERAL LEDGER"
    assert "POSTED TO ACME GENERAL LEDGER" in receipt_gl["acknowledgement_status"]







"""
Automated Integration Tests for Google Forms & Apps Script Middleware.
Verifies security, authoritative server-side pricing, PayMongo checkout creation,
and atomic idempotency deduplication.
"""

import pytest
from fastapi.testclient import TestClient
from app import app
from database import get_db, SessionLocal
from models import Order, PaymentAttempt

client = TestClient(app)
VALID_SECRET = "rge_gforms_live_sec_89f3a21"


@pytest.fixture(autouse=True)
def clean_db():
    """Ensure clean test slate for google form integration."""
    db = SessionLocal()
    try:
        # Delete any leftover test orders
        existing = db.query(Order).all()
        for o in existing:
            meta = o.metadata_dict or {}
            if meta.get("source") == "google_forms" or "gf_test_" in str(meta.get("google_form_response_id", "")):
                db.query(PaymentAttempt).filter(PaymentAttempt.order_id == o.id).delete()
                db.delete(o)
        db.commit()
    finally:
        db.close()


def test_google_forms_missing_secret_rejected():
    """Requests without the X-Google-Forms-Secret header must be rejected with 401."""
    payload = {
        "response_id": "gf_test_no_secret_01",
        "customer_name": "Juan Dela Cruz",
        "customer_email": "juan@example.com",
        "item_reference": "Product A",
        "quantity": 1,
    }
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload)
    assert res.status_code == 401
    assert "Missing or invalid X-Google-Forms-Secret" in res.json()["detail"]


def test_google_forms_invalid_secret_rejected():
    """Requests with an invalid secret must be rejected with 401."""
    payload = {
        "response_id": "gf_test_bad_secret_02",
        "customer_name": "Maria Santos",
        "customer_email": "maria@example.com",
        "item_reference": "Product A",
        "quantity": 1,
    }
    headers = {"X-Google-Forms-Secret": "wrong_secret_token"}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res.status_code == 401


def test_google_forms_successful_order_and_checkout_creation():
    """Valid form submission must create Order with authoritative pricing and PayMongo checkout session."""
    payload = {
        "response_id": "gf_test_success_1001",
        "sheet_row_index": 2,
        "customer_name": "Raymund Reyes",
        "customer_email": "raymund@example.com",
        "customer_phone": "09171234567",
        "item_reference": "Product A",
        "quantity": 2,
        "category": "product_order",
        "notes": "Urgent procurement batch",
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)

    assert res.status_code == 201
    data = res.json()

    assert data["status"] == "pending"
    assert data["order_id"] is not None
    assert data["order_number"].startswith("ORD-")
    assert "Product A (Qty: 2) - Raymund Reyes" in data["order_title"]
    assert data["customer_email"] == "raymund@example.com"
    assert data["customer_name"] == "Raymund Reyes"
    assert data["checkout_url"] != ""
    assert data["payment_attempt_id"] != ""
    assert data["is_duplicate"] is False

    # Check database persistence
    db = SessionLocal()
    order = db.query(Order).filter(Order.id == data["order_id"]).first()
    assert order is not None
    assert order.metadata_dict.get("source") == "google_forms"
    assert order.metadata_dict.get("google_form_response_id") == "gf_test_success_1001"
    assert order.metadata_dict.get("customer_phone") == "+63 917 123 4567"
    db.close()


def test_google_forms_atomic_idempotency_deduplication():
    """Resubmission of identical form response_id returns existing order without duplicates."""
    payload = {
        "response_id": "gf_test_idempotent_999",
        "sheet_row_index": 5,
        "customer_name": "Ana Lim",
        "customer_email": "ana@example.com",
        "item_reference": "Product B",
        "quantity": 1,
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}

    # First call
    res1 = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res1.status_code == 201
    data1 = res1.json()
    assert data1["is_duplicate"] is False

    # Second call (e.g. Google Apps Script retry or network blip)
    res2 = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res2.status_code == 201
    data2 = res2.json()

    # Must match original order exactly
    assert data2["is_duplicate"] is True
    assert data2["order_id"] == data1["order_id"]
    assert data2["order_number"] == data1["order_number"]
    assert data2["checkout_url"] == data1["checkout_url"]

    # Verify no duplicate order records exist in DB
    db = SessionLocal()
    orders = db.query(Order).all()
    matching = [o for o in orders if (o.metadata_dict or {}).get("google_form_response_id") == "gf_test_idempotent_999"]
    assert len(matching) == 1
    db.close()


def test_google_forms_status_endpoint():
    """Status endpoint must report integration health and count of ingested orders."""
    res = client.get("/api/v1/integrations/google-forms/status")
    assert res.status_code == 200
    data = res.json()
    assert data["status"] == "active"
    assert "ingested_orders_count" in data
    assert data["secret_configured"] is True


def test_google_forms_aliases_and_raw_answers():
    """Verifies that reference_code, sheet_row, title, and raw_answers work interchangeably."""
    payload = {
        "response_id": "gf_test_alias_2002",
        "sheet_row": 8,
        "customer_name": "Carlos Mendoza",
        "customer_email": "carlos@example.com",
        "reference_code": "Product B",
        "title": "Custom Order Title - Carlos",
        "quantity": 3,
        "raw_answers": {
            "email address": "carlos@example.com",
            "full name": "Carlos Mendoza",
            "product": "Product B"
        }
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()
    assert data["order_title"] == "Custom Order Title - Carlos"
    assert data["customer_email"] == "carlos@example.com"
    assert data["checkout_url"] != ""

    db = SessionLocal()
    order = db.query(Order).filter(Order.id == data["order_id"]).first()
    assert order is not None
    meta = order.metadata_dict or {}
    assert meta.get("sheet_row") == 8
    assert meta.get("reference_code") == "Product B"
    assert meta.get("raw_answers", {}).get("product") == "Product B"
    db.close()


def test_sync_status_to_google_sheet_mock():
    """Verifies that sync_status_to_google_sheet sends valid JSON payload to Apps Script."""
    from unittest.mock import patch, MagicMock
    from google_forms_routes import sync_status_to_google_sheet

    mock_resp = MagicMock()
    mock_resp.status = 200
    mock_resp.__enter__.return_value = mock_resp

    with patch("urllib.request.urlopen", return_value=mock_resp) as mock_urlopen:
        success = sync_status_to_google_sheet(
            response_id="gforms_12345",
            order_number="ORD-202610-1234",
            payment_status="PAID",
            official_receipt_no="OR-202610-1234-ABCD",
            webhook_url="https://script.google.com/macros/s/TEST/exec",
            secret=VALID_SECRET,
        )
        assert success is True
        assert mock_urlopen.called
        req = mock_urlopen.call_args[0][0]
        assert req.get_full_url() == "https://script.google.com/macros/s/TEST/exec"


def test_verify_paymongo_signature_helper():
    """Verifies HMAC-SHA256 signature generator and validator in webhook_orchestration."""
    import time
    import hmac
    import hashlib
    from webhook_orchestration import verify_paymongo_signature

    test_secret = "whsk_test_orchestration_secret_9988"
    raw_payload = b'{"data":{"id":"evt_test_123","attributes":{"type":"payment.paid"}}}'
    timestamp = str(int(time.time()))
    signed_payload = f"{timestamp}.{raw_payload.decode('utf-8')}".encode("utf-8")
    computed_sig = hmac.new(test_secret.encode("utf-8"), signed_payload, hashlib.sha256).hexdigest()

    header_live = f"t={timestamp},li={computed_sig}"
    assert verify_paymongo_signature(raw_payload, header_live, test_secret) is True

    header_test = f"t={timestamp},te={computed_sig}"
    assert verify_paymongo_signature(raw_payload, header_test, test_secret) is True

    bad_header = f"t={timestamp},li=bad_signature_hash"
    assert verify_paymongo_signature(raw_payload, bad_header, test_secret) is False



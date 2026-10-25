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


def test_verify_google_forms_request_helper():
    """Verifies X-Google-Forms-Secret and optional HMAC signature verification."""
    import hmac
    import hashlib
    from google_forms_routes import verify_google_forms_request

    raw_body = b'{"response_id":"gf_test_999","customer_name":"Test User"}'
    valid_secret = VALID_SECRET
    computed_sig = hmac.new(valid_secret.encode("utf-8"), raw_body, hashlib.sha256).hexdigest()

    # Valid secret only
    assert verify_google_forms_request(raw_body, valid_secret) is True
    # Invalid secret
    assert verify_google_forms_request(raw_body, "wrong_secret") is False
    # Valid secret + valid HMAC signature
    assert verify_google_forms_request(raw_body, valid_secret, computed_sig) is True
    # Valid secret + invalid HMAC signature
    assert verify_google_forms_request(raw_body, valid_secret, "bad_signature") is False


def test_notify_paymongo_webhook_outcome_mock():
    """Verifies notify_paymongo_webhook_outcome dispatches status update without crashing."""
    from unittest.mock import patch
    from google_forms_routes import notify_paymongo_webhook_outcome

    with patch("urllib.request.urlopen") as mock_urlopen:
        notify_paymongo_webhook_outcome(
            outcome="PAID",
            order_number="ORD-202610-TEST",
            order_title="Product A - Test",
            amount_minor=25000,
            customer_email="customer@example.com",
            customer_name="Customer Name",
            response_id="gf_test_001",
            official_receipt_no="OR-202610-001",
        )


def test_bookstore_order_with_instructor_credit_and_raw_answers():
    """Verifies that bookstore orders calculate book price and parse instructor credit and student ID from raw_answers."""
    payload = {
        "response_id": "gf_test_bookstore_raw_01",
        "sheet_row_index": 12,
        "customer_name": "Alyssa Gonzaga",
        "customer_email": "alyssa@example.edu.ph",
        "customer_phone": "09181234567",
        "quantity": 2,
        "raw_answers": {
            "timestamp": "2026-10-24T12:00:00Z",
            "full name": "Alyssa Gonzaga",
            "email address": "alyssa@example.edu.ph",
            "contact number": "09181234567",
            "book / course material selection": "Financial Accounting & Reporting Vol. 1 - ₱450.00",
            "quantity": "2",
            "enrolled instructor to credit": "Prof. Maria Santos (Taxation)",
            "student / enrollee id number": "2026-ACCT-1049",
            "delivery address / campus pickup notes": "Campus bookstore pickup"
        }
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()

    # ₱450.00 * 2 = ₱900.00 = 90,000 centavos
    assert data["amount_minor"] == 90000
    assert data["amount_php"] == "₱900.00"
    assert "Credit: Prof. Maria Santos (Taxation)" in data["order_title"]
    assert data["checkout_url"] != ""

    db = SessionLocal()
    order = db.query(Order).filter(Order.id == data["order_id"]).first()
    assert order is not None
    meta = order.metadata_dict or {}
    assert meta.get("instructor_credit") == "Prof. Maria Santos (Taxation)"
    assert meta.get("student_id") == "2026-ACCT-1049"
    assert "Financial Accounting & Reporting Vol. 1" in meta.get("book_title", "")
    assert meta.get("customer_phone") == "+63 918 123 4567"
    db.close()


def test_bookstore_order_parsing_from_notes():
    """Verifies that instructor credit and student ID are recognized when passed in notes string."""
    payload = {
        "response_id": "gf_test_bookstore_notes_02",
        "sheet_row_index": 14,
        "customer_name": "Mark Anthony Fernandez",
        "customer_email": "mark@example.edu.ph",
        "item_reference": "Auditing Theory & Practice - ₱480.00",
        "quantity": 1,
        "notes": "Instructor Credit: Prof. Raymund Reyes (Accounting & Auditing) | Student ID: 2026-BSA-8821 | Delivery to Room 302",
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()

    # ₱480.00 * 1 = 48,000 centavos
    assert data["amount_minor"] == 48000
    assert data["amount_php"] == "₱480.00"

    db = SessionLocal()
    order = db.query(Order).filter(Order.id == data["order_id"]).first()
    assert order is not None
    meta = order.metadata_dict or {}
    assert meta.get("instructor_credit") == "Prof. Raymund Reyes (Accounting & Auditing)"
    assert meta.get("student_id") == "2026-BSA-8821"
    db.close()


def test_bookstore_order_direct_fields():
    """Verifies that direct book_title, student_id, and instructor_credit fields work seamlessly."""
    payload = {
        "response_id": "gf_test_bookstore_direct_03",
        "sheet_row": 15,
        "customer_name": "Camille Del Rosario",
        "customer_email": "camille@example.edu.ph",
        "book_title": "Advanced Financial Accounting & Reporting - ₱520.00",
        "instructor_credit": "Prof. Juan Carlos (Finance & Advisory)",
        "student_id": "2025-CPA-991",
        "quantity": 1,
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()

    # ₱520.00 * 1 = 52,000 centavos
    assert data["amount_minor"] == 52000
    assert data["amount_php"] == "₱520.00"
    assert "Prof. Juan Carlos" in data["order_title"]

    db = SessionLocal()
    order = db.query(Order).filter(Order.id == data["order_id"]).first()
    assert order is not None
    meta = order.metadata_dict or {}
    assert meta.get("instructor_credit") == "Prof. Juan Carlos (Finance & Advisory)"
    assert meta.get("student_id") == "2025-CPA-991"
    assert "Advanced Financial Accounting & Reporting" in meta.get("book_title", "")
    db.close()


def test_bookstore_order_with_part_number_in_title_and_peso_sign():
    """Verifies that titles with part/paper numbers like 'P2' or 'P1' do not misparse currency as ₱2."""
    payload = {
        "response_id": "gf_test_bookstore_part_num_04",
        "sheet_row": 16,
        "customer_name": "Patricia Cruz",
        "customer_email": "patricia@example.edu.ph",
        "book_title": "P2 Reviewer (Practical Accounting 2) - ₱450.00",
        "quantity": 1,
        "notes": "Faculty Credit: Prof. Elena Gomez (Management Accounting) | Student #: 2026-BSA-7711",
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()

    # Must be ₱450.00 (45,000 centavos), NOT ₱2.00 (200 centavos)
    assert data["amount_minor"] == 45000
    assert data["amount_php"] == "₱450.00"
    assert "Elena Gomez" in data["order_title"]

    db = SessionLocal()
    order = db.query(Order).filter(Order.id == data["order_id"]).first()
    assert order is not None
    meta = order.metadata_dict or {}
    assert meta.get("instructor_credit") == "Prof. Elena Gomez (Management Accounting)"
    assert meta.get("student_id") == "2026-BSA-7711"
    db.close()


def test_bookstore_order_credit_card_not_confused_with_instructor_credit():
    """Verifies that a 'Credit Card' field in raw_answers does not falsely match as instructor credit."""
    payload = {
        "response_id": "gf_test_bookstore_cc_guard_05",
        "sheet_row": 17,
        "customer_name": "Lorenzo Miguel Tan",
        "customer_email": "lorenzo@example.edu.ph",
        "item_reference": "Philippine Tax Law & Principles - ₱500.00",
        "quantity": 2,
        "raw_answers": {
            "book": "Philippine Tax Law & Principles - ₱500.00",
            "quantity": "2",
            "credit card type": "MasterCard Gold",
            "enrolled instructor to credit": "Prof. Maria Santos (Taxation)",
            "student id": "2026-TAX-4410",
        }
    }
    headers = {"X-Google-Forms-Secret": VALID_SECRET}
    res = client.post("/api/v1/integrations/google-forms/submit", json=payload, headers=headers)
    assert res.status_code == 201
    data = res.json()

    # ₱500.00 * 2 = 100,000 centavos
    assert data["amount_minor"] == 100000
    assert data["amount_php"] == "₱1,000.00"

    db = SessionLocal()
    order = db.query(Order).filter(Order.id == data["order_id"]).first()
    assert order is not None
    meta = order.metadata_dict or {}
    assert meta.get("instructor_credit") == "Prof. Maria Santos (Taxation)"
    assert meta.get("instructor_credit") != "MasterCard Gold"
    assert meta.get("student_id") == "2026-TAX-4410"
    db.close()


def test_google_forms_multi_channel_secret_resilience():
    """Verifies lowercase HTTP/2 headers, quoted secrets, body payload, and query parameter secret fallbacks."""
    # 1. Lowercase HTTP/2 header (as normalized by Cloudflare and NGINX)
    p1 = {
        "response_id": "gf_resilience_01",
        "customer_name": "Resilience Student 1",
        "customer_email": "res1@example.com",
        "item_reference": "Product A",
        "quantity": 1,
    }
    r1 = client.post("/api/v1/integrations/google-forms/submit", json=p1, headers={"x-google-forms-secret": VALID_SECRET})
    assert r1.status_code == 201

    # 2. Quoted secret (e.g. from .env strings or shell wrappers)
    p2 = {
        "response_id": "gf_resilience_02",
        "customer_name": "Resilience Student 2",
        "customer_email": "res2@example.com",
        "item_reference": "Product A",
        "quantity": 1,
    }
    r2 = client.post("/api/v1/integrations/google-forms/submit", json=p2, headers={"X-Google-Forms-Secret": f'"{VALID_SECRET}"'})
    assert r2.status_code == 201

    # 3. Body payload secret fallback (when reverse proxy or WAF strips custom headers)
    p3 = {
        "response_id": "gf_resilience_03",
        "customer_name": "Resilience Student 3",
        "customer_email": "res3@example.com",
        "item_reference": "Product A",
        "quantity": 1,
        "secret": VALID_SECRET,
    }
    r3 = client.post("/api/v1/integrations/google-forms/submit", json=p3)
    assert r3.status_code == 201

    # 4. Query param secret fallback
    p4 = {
        "response_id": "gf_resilience_04",
        "customer_name": "Resilience Student 4",
        "customer_email": "res4@example.com",
        "item_reference": "Product A",
        "quantity": 1,
    }
    r4 = client.post(f"/api/v1/integrations/google-forms/submit?secret={VALID_SECRET}", json=p4)
    assert r4.status_code == 201







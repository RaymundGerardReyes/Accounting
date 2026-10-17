"""
Backend/test_integrations.py
Automated tests for Live PayMongo and Google Email SMTP integration service,
diagnostic health probes, and REST test endpoints.
"""

import os
import smtplib
from unittest.mock import patch, MagicMock
import pytest
from fastapi.testclient import TestClient

from app import app
from services.integration_service import IntegrationService
from services.email_service import EmailService

client = TestClient(app)


def test_paymongo_probe_simulator_mode():
    svc = IntegrationService()
    # Force empty key to verify simulator mode
    with patch.dict(os.environ, {"PAYMONGO_SECRET_KEY": ""}):
        probe = svc.probe_paymongo()
        assert probe["operational_state"] == "simulator"
        assert probe["mode"] == "sandbox"
        assert probe["is_live"] is False
        assert "Simulator" in probe["status"]


def test_paymongo_probe_live_key_success():
    svc = IntegrationService()
    mock_response = MagicMock()
    mock_response.status_code = 200
    mock_response.json.return_value = {"data": []}

    with patch("httpx.Client.get", return_value=mock_response):
        probe = svc.probe_paymongo(custom_secret_key="sk_live_test_sample_key_12345")
        assert probe["operational_state"] == "connected"
        assert probe["mode"] == "live"
        assert probe["is_live"] is True
        assert probe["details"]["masked_key"] == "sk_live•••••••2345"


def test_paymongo_probe_unauthorized_key():
    svc = IntegrationService()
    mock_response = MagicMock()
    mock_response.status_code = 401
    mock_response.text = '{"errors":[{"code":"parameter_unauthorized"}]}'

    with patch("httpx.Client.get", return_value=mock_response):
        probe = svc.probe_paymongo(custom_secret_key="sk_test_invalid_sample_key")
        assert probe["operational_state"] == "error"
        assert probe["status"] == "Unauthorized Key"


def test_smtp_probe_unconfigured():
    svc = IntegrationService()
    with patch.dict(os.environ, {"SMTP_USER": "", "SMTP_APP_PASSWORD": ""}):
        probe = svc.probe_smtp(custom_user="", custom_password="")
        assert probe["operational_state"] == "unconfigured"
        assert probe["status"] == "Unconfigured"


def test_smtp_probe_connected():
    email_svc = EmailService()
    mock_smtp = MagicMock()

    with patch("smtplib.SMTP", return_value=mock_smtp):
        res = email_svc.test_connection(
            host="smtp.gmail.com",
            port=587,
            user="treasury@acme.com",
            password="xxxx xxxx xxxx xxxx",
            use_tls=True,
        )
        assert res["success"] is True
        assert res["status"] == "connected"
        assert res["latency_ms"] >= 0


def test_smtp_probe_auth_error():
    email_svc = EmailService()
    mock_smtp = MagicMock()
    mock_smtp.login.side_effect = smtplib.SMTPAuthenticationError(535, b"Authentication Failed")

    with patch("smtplib.SMTP", return_value=mock_smtp):
        res = email_svc.test_connection(
            host="smtp.gmail.com",
            port=587,
            user="treasury@acme.com",
            password="wrong_password",
            use_tls=True,
        )
        assert res["success"] is False
        assert res["status"] == "authentication_failed"
        assert "App Password" in res["message"]


def test_email_template_rendering():
    email_svc = EmailService()
    receipt_html = email_svc.render_payment_receipt_html(
        order_number="ORD-202610-0999",
        title="Cost Allocation Quarterly Assessment",
        amount_php="15,450.00",
        payment_method="gcash",
        paid_at="2026-10-17 18:30:00 UTC",
        reference_id="att_test_123",
    )
    assert "ORD-202610-0999" in receipt_html
    assert "₱15,450.00" in receipt_html
    assert "GCASH" in receipt_html


def test_api_integration_status_endpoint():
    res = client.get("/api/v1/integrations/status")
    assert res.status_code == 200
    data = res.json()
    assert data["success"] is True
    integrations = data["integrations"]
    assert len(integrations) == 4
    names = [i["name"] for i in integrations]
    assert "PayMongo Payment Gateway" in names
    assert "Database" in names
    assert "Email Notifications" in names
    assert "API Access" in names


def test_api_paymongo_test_endpoint():
    res = client.post(
        "/api/v1/integrations/paymongo/test",
        json={"secret_key": "", "webhook_secret": "whsk_test_secret_123"},
    )
    assert res.status_code == 200
    data = res.json()
    assert data["success"] is True
    assert data["probe"]["operational_state"] == "simulator"
    assert data["webhook_check"]["signature_verified"] is True
    assert "t=" in data["webhook_check"]["simulated_header"]


def test_api_email_test_endpoint_unconfigured():
    res = client.post(
        "/api/v1/integrations/email/test",
        json={"user": "", "password": "", "send_test_email": False},
    )
    assert res.status_code == 200
    data = res.json()
    assert data["success"] is False
    assert data["probe"]["operational_state"] == "unconfigured"


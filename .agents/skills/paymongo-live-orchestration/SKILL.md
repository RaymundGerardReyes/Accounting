---
name: paymongo-live-orchestration
description: >-
  Runbook and reference for PayMongo Checkout Sessions, HMAC-SHA256 webhook
  verification, outbox fulfillment, and reconciliation in E:\Accounting.
---

# PayMongo Live Orchestration Skill

## 1. Environment Variables (`E:\Accounting\Backend\.env`)
- `PAYMONGO_SECRET_KEY`: `sk_test_...` or `sk_live_...` (if omitted, Backend automatically uses the Local Sandbox Simulator).
- `PAYMONGO_WEBHOOK_SECRET`: `whsk_...` for verifying `Paymongo-Signature` headers.
- `DATABASE_URL`: PostgreSQL URL (`postgresql+psycopg2://...`) or omit for automatic `sqlite:///./accounting_payments.db`.

## 2. Webhook Signature Verification (`Paymongo-Signature`)
PayMongo sends `Paymongo-Signature: t=<timestamp>,te=<test_signature>,li=<live_signature>`.
- Construct signed payload: `f"{timestamp}.{raw_body.decode('utf-8')}"`
- Compute expected HMAC: `hmac.new(secret.encode(), signed_payload.encode(), hashlib.sha256).hexdigest()`
- Compare with `te` (test mode) or `li` (live mode) using `hmac.compare_digest`.

## 3. Testing & Reconciliation Commands
- Run backend orchestration test suite: `pytest test_orchestration.py` in `E:\Accounting\Backend`
- Trigger manual reconciliation: `POST http://localhost:5000/api/v1/admin/payments/{payment_id}/reconcile`
- Trigger simulated signed webhook: `POST http://localhost:5000/api/v1/sandbox/simulate-webhook`

## 4. Live API Credential Diagnostics & Probes
- Always use `GET https://api.paymongo.com/v1/payments?limit=1` with HTTP Basic Auth (`<secret_key>:`) to probe secret key validity and measure round-trip latency.
- Do NOT probe `GET /v1/checkout_sessions` or `GET /v1/checkout_sessions?limit=1` because PayMongo does not expose a collection listing endpoint for checkout sessions and rejects the request with HTTP 400 (`parameter_format_invalid`).
- An HTTP 401 response indicates an invalid or unauthorized secret key; an HTTP 400 response indicates a valid key rejected due to an unsupported route or query parameter syntax.


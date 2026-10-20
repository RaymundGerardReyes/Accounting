---
name: paymongo-live-orchestration
description: >-
  Runbook and reference for PayMongo Checkout Sessions, HMAC-SHA256 webhook
  verification, outbox fulfillment, and reconciliation in E:\Accounting.
---

# PayMongo Live Orchestration Skill

## 1. Environment Variables (`E:\Accounting\.env`)
- Authoritative root environment configuration: `E:\Accounting\.env` (template: `E:\Accounting\.env.example`).
- Subdirectory `.env` files (`Backend/.env`, `Frontend/.env.local`) are strictly deprecated.
- `PAYMONGO_SECRET_KEY`: `sk_test_...` or `sk_live_...` (if omitted, Backend automatically uses the Local Sandbox Simulator).
- `PAYMONGO_WEBHOOK_SECRET`: `whsk_...` for verifying `Paymongo-Signature` headers.
- `DATABASE_URL`: PostgreSQL URL (`postgresql+psycopg2://...`) or omit for automatic `sqlite:///./accounting_payments.db`.
- `BACKEND_INTERNAL_URL`: `http://localhost:5000` (used by Next.js route proxy).

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

## 5. Failed & Expired Session Orchestration & Recovery
- **Webhook Events**:
  - Payment Failures: `payment.failed`, `checkout_session.payment.failed`, `payment_intent.payment_failed`, `source.failed` transition attempts to `failed` and populate `failure_code` and `failure_message`.
  - Expirations: `checkout_session.expired` transitions attempts to `expired` with code `SESSION_EXPIRED`.
  - Cancellations: `payment.canceled`, `checkout_session.cancelled`, `payment_intent.cancelled` transition attempts to `cancelled` with code `SESSION_CANCELLED`.
- **Reconciliation Priority**: Check `payments` for any `paid` entry first. If not paid, evaluate top-level `attributes.status` for `expired` or `cancelled` before fallback to older failed card payments.
- **Terminal State Guard**: Reconciling attempts in `succeeded`, `failed`, `expired`, or `cancelled` immediately returns `already_<status>` without redundant network calls.
- **Order Recovery Lifecycle**: When attempts fail or expire, parent order status remains `pending`. The UI displays `Retry Payment` and `/payment/result/:id` offers `Start New Checkout`, allowing customers to retry seamlessly.
- **Outbox Retries**: Call `POST /api/v1/admin/fulfillment-outbox/process?retry_failed=true` to reprocess failed fulfillment events.



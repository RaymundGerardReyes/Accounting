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

## 6. Authoritative Gateway Session Invalidation & Testing
- **Remote Gateway Expiration API**:
  - To cancel or expire an active checkout session on PayMongo's hosted servers, call `POST https://api.paymongo.com/v1/checkout_sessions/{provider_payment_id}/expire` with HTTP Basic Auth (`<secret_key>:`).
  - Calling this endpoint immediately revokes the public payment URL (`https://pm.link/...`), blocking customer payment attempts via QR Ph, Card, or E-Wallets with a session expired notice.
  - If the session was already settled, PayMongo returns HTTP 400 with `session_already_paid`. Catch this and enforce Terminal State Immutability.
- **Dead URL Revocation**:
  - Whenever an attempt transitions to `failed`, `expired`, or `cancelled`, always clear `attempt.checkout_url = None` in the database to prevent stale client redirection.
- **Terminal State Immutability Guard**:
  - Attempts with `status === "succeeded"` or orders with `status === "paid"` MUST NEVER be modified to `expired` or `failed`.
  - Return HTTP 400 with `Cannot expire payment attempt: Order '...' is already settled and paid.`
- **Webhook Simulation Auto-Invalidation**:
  - When simulating `checkout_session.expired` on an attempt with an active PayMongo session (`cs_...`), automatically invoke `expire_checkout_session()` to synchronize remote gateway state.

## 7. Heroku Multi-Buildpack Monorepo Deployment & Production Supervisor
- **Multi-Buildpack Root Configuration**:
  - Buildpack 1: `heroku/nodejs` (detects root `package.json`, installs Node >=18, runs `heroku-postbuild: cd Frontend && npm install --include=dev && npm run build`).
  - Buildpack 2: `heroku/python` (detects root `requirements.txt` & `runtime.txt`, installs `-r Backend/requirements.txt` + `gunicorn`).
- **Dyno Supervisor (`scripts/start_production.py`)**:
  - Heroku web dynos assign a single dynamic `$PORT`. Next.js binds to `0.0.0.0:$PORT` while FastAPI binds to internal loopback `127.0.0.1:5000` (or 5001 if `$PORT=5000`).
  - `scripts/start_production.py` injects `BACKEND_INTERNAL_URL="http://127.0.0.1:<port>"` into the environment so Next.js App Router reverse-proxies `/api/*` requests with zero network latency.
  - Handles `SIGTERM`/`SIGINT` for graceful shutdown of both child processes.
- **PostgreSQL URL Scheme Normalization**:
  - Heroku Postgres add-ons export `DATABASE_URL=postgres://...`. In `Backend/database.py`, always normalize `postgres://` to `postgresql://` for SQLAlchemy 2.0.

## 8. Multi-Channel Payment Method Selection & `payment_method_types` Array Mapping
- **Channel Specificity**:
  - When initiating a checkout session, never hardcode a static array of payment methods. Map the user's selected channel dynamically via `resolve_paymongo_payment_method_types(payment_method)`.
  - Canonical PayMongo Checkout Sessions API types:
    - `card`: `["card"]` (opens Credit / Debit Card form directly)
    - `gcash_maya`: `["gcash", "paymaya"]` (opens GCash & Maya e-wallet authorization)
    - `dob`: `["dob"]` (opens Direct Online Banking for BPI & UnionBank)
    - `qrph`: `["qrph"]` (opens National QR Ph standard)
    - `hosted_checkout`: `["card", "gcash", "paymaya", "dob", "qrph"]` (renders full multi-channel suite with Card & E-Wallets leading so customers are never defaulted exclusively to QR Ph)
- **Simulator Parity**:
  - In `Frontend/app/payment/sandbox/[attemptId]/page.tsx`, ensure `initialMethod` handles compound keys like `gcash_maya` (mapping to `"gcash"`) and renders a prominent badge displaying the channel selected at checkout.

## 9. Polyglot Monorepo `.gitignore` & Runtime Engine Invariants
- **Preserve Frontend Source Directories**:
  - Never use unanchored `lib/` or `lib64/` rules in a polyglot monorepo `.gitignore`.
  - Python virtual environment libraries must be anchored to root (`/lib/`, `/lib64/`), and frontend source directories must be explicitly whitelisted:
    ```gitignore
    /lib/
    /lib64/
    !Frontend/lib/
    !Frontend/lib/**
    ```
  - Always verify with `git ls-files Frontend/lib/` and `git check-ignore -v Frontend/lib/payment-api.ts` before committing.
- **Node.js Engine Pinning**:
  - In root `package.json`, pin `engines.node` to an active LTS release (e.g. `"20.x"`) to satisfy Heroku buildpack recommendations and eliminate wide-range resolution warnings.

## 10. Dyno Supervisor Single-Process & Database Resiliency Invariants
- **Suppress Heroku `WEB_CONCURRENCY` in Monorepo Supervisors**:
  - The internal FastAPI backend on loopback (`127.0.0.1:5000`) must always run as a single process:
    - Pass `--workers 1` explicitly in the uvicorn command line.
    - Set `env["WEB_CONCURRENCY"] = "1"` in the subprocess environment.
    - Set `stdin=subprocess.DEVNULL` to isolate subprocesses from non-interactive terminal signals.
- **Heroku PostgreSQL SSL & Local SQLite Fallback**:
  - Always enforce `connect_args={"sslmode": "require"}` for remote `postgresql://` connections on Heroku.
  - Implement an immediate fallback to local SQLite (`sqlite:///accounting_payments.db`) if primary PostgreSQL connection validation fails, guaranteeing that the web dyno always passes its readiness probe.

## 11. High-Concurrency Intake via Serverless Google Forms & Apps Script Buffer
- **Serverless Form Middleware**:
  - Route external high-traffic intake (e.g. 1,000 to 5,000 concurrent users) through Google Forms and linked Google Sheets.
  - Google Forms buffers sudden traffic surges globally with zero server infrastructure cost or dyno exhaustion.
- **Google Apps Script Orchestration (`onFormSubmit`)**:
  - Bind an `onFormSubmit(e)` event trigger in Google Sheets Apps Script.
  - Dispatch requests to backend endpoint `POST /api/v1/integrations/google-forms/submit` authenticated with `X-Google-Forms-Secret`.
  - Pass form response ID to enforce atomic idempotency (`google_forms:<response_id>`).
- **PayMongo Checkout Delivery & Live Sheet Sync**:
  - The backend computes authoritative centavo pricing server-side and returns the PayMongo `checkout_url`.
  - Apps Script writes `Order Number` and `PayMongo Checkout URL` back to the Google Sheet row and sends a branded email to the user with the payment link.
  - Live PayMongo webhooks (`checkout_session.payment.paid`) update backend state, triggering instant synchronization in `payments-hub.tsx`.








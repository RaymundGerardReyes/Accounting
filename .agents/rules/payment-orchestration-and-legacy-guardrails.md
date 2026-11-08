---
description: Strict architectural guardrails for E:\Accounting legacy compatibility and live payment orchestration
---

# Accounting & Payment Orchestration Guardrails

1. **Legacy API & UI Preservation**:
   - Keep all legacy Cost Analysis endpoints (`/api/dashboard`, `/api/department/{slug}`, `/api/products`, `/api/variance`, `/api/configuration`) operational on port `5000`.
   - Maintain `₱` (PHP) currency formatting across all Frontend components (`Dashboard`, `DepartmentDetail`, `ProductLineAnalysis`, `VarianceAnalysis`, `Configuration`, `PaymentsHub`).

2. **Payment Orchestration Invariants**:
   - **Frontend Initiates, Backend Orchestrates, Gateway Confirms**: Never mark an order as paid based on a browser redirect (`/payment/return`). Only signed webhooks (`/api/v1/webhooks/paymongo`) or backend reconciliation (`POST /api/v1/admin/payments/{id}/reconcile`) may transition a payment to `succeeded`.
   - **Authoritative Server-Side Pricing**: Always calculate `amount_minor` (in PHP centavos, `BIGINT > 0`) on the backend from trusted product or department records.
   - **Terminal State Immutability**: All terminal states (`succeeded`, `failed`, `expired`, `cancelled`) are strictly immutable. Out-of-order webhooks, late declines, or reconciliation attempts on terminal records are idempotent no-ops and must never mutate state.
   - **Comprehensive Failure & Expiration Event Mapping**: PayMongo webhook handlers must accept all payment failure events (`payment.failed`, `checkout_session.payment.failed`, `payment_intent.payment_failed`, `source.failed`), expiration events (`checkout_session.expired`), and cancellation events (`payment.canceled`, `checkout_session.cancelled`, `payment_intent.cancelled`), populating structured failure codes (`SESSION_EXPIRED`, `CARD_DECLINED`, etc.) and failure messages.
   - **Session Precedence in Reconciliation**: When reconciling against PayMongo's checkout session API, check for any successful payment first (`paid`). If none succeeded, prioritize top-level session status (`expired` / `cancelled`) over older failed card attempts.
   - **Parent Order Recovery**: An order whose latest attempt failed, expired, or was cancelled must remain in `pending` status so the customer can retry with a new payment attempt. The UI must present a recovery action ("Retry Payment" / "Start New Checkout").
   - **Timeout Safety**: Never auto-retry a charge on a gateway timeout; mark the attempt as `unknown` and reconcile first.
   - **Transactional Outbox Resilience**: Outbox processors must support explicit retries of failed entries (`?retry_failed=true`), ensuring asynchronous fulfillment and customer receipt delivery succeed even after temporary email or file I/O outages.

3. **Monorepo Environment Configuration Invariants**:
   - **Unified Single Source of Truth**: The single authoritative environment file is `E:\Accounting\.env` (repository root), initialized from `E:\Accounting\.env.example`.
   - **Prohibition of Subsystem `.env` Files**: Placing `.env` files in subdirectories (`E:\Accounting\Backend\.env`, `E:\Accounting\Frontend\.env.local`, etc.) is strictly deprecated and prohibited to eliminate split-brain configuration risk.
   - **Zero Secrets in Client Bundle**: Server secrets (`PAYMONGO_SECRET_KEY`, `PAYMONGO_WEBHOOK_SECRET`, `SMTP_APP_PASSWORD`) must NEVER be prefixed with `NEXT_PUBLIC_`.
   - **Automatic Local Fallback**: Omitting PayMongo gateway keys in `E:\Accounting\.env` must cleanly activate the local sandbox simulator without runtime crashes.

4. **Heroku Monorepo Multi-Buildpack & Dyno Supervision Guardrails**:
   - **Multi-Buildpack Root Configuration**: Maintain root `package.json`, `requirements.txt`, `runtime.txt`, and `Procfile` so Heroku's `heroku/nodejs` and `heroku/python` buildpacks detect codebase roots cleanly.
   - **Single Dyno Port Supervisor**: On Heroku, bind Next.js to Heroku's assigned `$PORT` (`0.0.0.0:$PORT`) and FastAPI to internal loopback (`127.0.0.1:5000` or `5001` if collision), supervised via `scripts/start_production.py`.
   - **Internal Reverse Proxy**: `scripts/start_production.py` injects `BACKEND_INTERNAL_URL` so Next.js App Router reverse-proxies all `/api/*` traffic locally with zero hop latency.
   - **Postgres Dialect Compatibility**: Always normalize `postgres://` to `postgresql://` in `database.py` for SQLAlchemy 2.0 when running against Heroku Postgres.

5. **SignalR Real-Time Synchronization & Server-Authoritative Expiration Guardrails**:
   - **Backend as Authoritative Payment Authority**: The .NET backend is the sole authority on payment state machine transitions (`Pending -> RequiresAction -> Succeeded / Expired / Failed / Cancelled`). Next.js must never independently mark an attempt as expired or paid; client timer reaching zero triggers an authoritative reconciliation query (`GET /api/v1/payments/{id}?auto_reconcile=true`), not self-mutation.
   - **Remote Gateway Expiration API & Background Worker**: Implement a `BackgroundService` (`PaymentExpirationWorker`) running on a 15-second bounded loop. Query overdue non-terminal attempts (`WHERE status = 'Pending' AND expires_at <= NOW()`, bounded to 50 items/cycle). Inspect PayMongo status first (`Reconcile-Before-Expire`); preserve paid sessions, defer in-flight sessions, and explicitly invoke `POST https://api.paymongo.com/v1/checkout_sessions/{id}/expire` using Basic Auth (`<secret_key>:`) to invalidate the hosted PayMongo link (`pm.link/...`). Handle HTTP 400 (`already_paid` / `session_already_paid`) gracefully by enforcing terminal state immutability.
   - **SignalR Real-Time Status Notification**: Host a `PaymentHub` at `/hubs/payment` mapping callers to group `payment:{transactionId}`. Inject `IPaymentStatusNotifier` into both the PayMongo webhook processor (`ProcessWebhookEventHandler`) and background expiration worker (`PaymentExpirationWorker`). Broadcast `PaymentStatusChanged` with `{ transactionId, status, occurredAt }` immediately upon terminal transition to stop client countdowns instantly with 0ms latency.
   - **Graceful Polling Fallback**: Frontend SignalR hooks (`usePaymentSignalR`) must configure automatic reconnect (`[0, 2000, 5000, 10000, 30000]`) and fall back to periodic polling (12s interval) whenever disconnected.

6. **Remote Gateway Reconciliation Cooldown & SignalR Event Notification**:
   - **Per-Attempt Gateway Cooldown Guard**: `PollAndReconcileHandler` must maintain an in-memory sliding cooldown (e.g. 3 to 4 seconds) per `ProviderPaymentId`. If an incoming query with `auto_reconcile=true` arrives within the cooldown window of a prior gateway check, the handler must return the current persisted database record in sub-millisecond time rather than dispatching a redundant remote HTTP request to PayMongo.
   - **Terminal State Fast-Path**: If `PaymentAttemptStateMachine.IsTerminal(attempt.Status)` is true, remote reconciliation must be completely bypassed, returning database state in <1ms.
   - **Real-Time Push on Reconciliation**: When manual or background polling reconciliation transitions an attempt to `Succeeded` or `Expired`, the handler must notify `IPaymentStatusNotifier` to immediately synchronize all open browser sessions and unmount client countdowns.

7. **PayMongo Checkout Session Status Priority Order & Active Reconciliation**:
   - **Authoritative Priority Evaluation (`EvaluateCheckoutSessionStatus`)**: Checkout session responses from PayMongo (`cs_...`) must be parsed with full DTO fidelity (`payment_intent.last_payment_error`, nested `payments[]`, `metadata`). Status must be resolved in strict precedence order:
     1. *Succeeded/Paid*: `paymentStatus == "paid"` OR `intentStatus == "succeeded"` OR `sessionStatus == "paid"` OR `paid_at.HasValue`.
     2. *Failed*: `paymentStatus == "failed"` OR `paymentIntent.last_payment_error is not null`. Immediately call `ExpireCheckoutSessionAsync` to lock the session, transition attempt to `Failed`, and push SignalR notification.
     3. *Expired*: `sessionStatus == "expired"` OR `intentStatus == "cancelled"` OR `expired_at <= now`.
     4. *Processing/Active*: Preserve active countdown until terminal transition or expiration deadline.
   - **Mid-Session Reconciliation (Active Polling & 15s Worker)**: `PaymentExpirationWorker` must actively reconcile all non-terminal attempts against PayMongo every 15 seconds without waiting for the full 30-minute deadline (`attempt.ExpiresAt <= nowUtc`), halting frontend countdowns immediately on mid-session failure or external expiration.






8. **Multi-Key Correlation, Gateway Failure Locking, and Tab Return Synchronization**:
   - **Multi-Key Webhook Correlation**: In PayMongo webhook payloads, failure events (`payment.failed`) provide a payment resource ID (`pay_...`) rather than the checkout session ID (`cs_...`). The webhook processor (`ProcessWebhookEventHandler`) must correlate attempts across 5 cascading keys:
     1. `metadata.payment_attempt_id` (direct Guid/string lookup)
     2. `resourceId` (`cs_...` or `pay_...`)
     3. `checkout_session_id` extracted from nested payment attributes
     4. `metadata.idempotency_key`
     5. `metadata.order_id` (resolving latest attempt)
   - **Immediate Gateway Invalidation on Decline**: When an attempt transitions to `Failed` via webhook or live reconciliation, the backend must immediately invoke `_paymentGateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId)` if it holds a checkout session ID (`cs_...`). This locks the hosted PayMongo link (`pm.link/...`), preventing double-submits or unauthorized retries on compromised sessions.
   - **Locked Checkout URL Nulling**: `PaymentMappings.ToDto` must evaluate payable state and return `CheckoutUrl = isPayable ? attempt.CheckoutUrl : null`. When an attempt is `succeeded`, `failed`, or `expired`, the checkout URL is securely wiped from all API responses.
   - **Tab-Focus & Window Reload Synchronization**: Frontend checkout guards (`usePayMongoRealtimeSync` / `PayMongoCheckoutGuard`) must listen to `visibilitychange` (`document.visibilityState === "visible"`) and `focus` events to immediately query backend reconciliation when the customer switches back from the PayMongo payment tab. In addition, clock skew must be calculated via `serverClockOffsetMs = serverTime - Date.now()` to ensure visual countdown accuracy regardless of local client time discrepancies.

## 11. Inviolable Gateway Secondary Resource & Source Lifecycle Verification Invariant
- **No Stubbing of Domain State Inspectors**: Performance, caching, or latency optimizations MUST NEVER stub out, disable, or return synthetic `null` for secondary resource status queries (e.g., `TryInspectUnderlyingSourceAsync` querying `GET /v1/sources/{src_id}` or `payment_intents/{pi_id}`).
- **Composite State Incompleteness**: Third-party checkout sessions (such as PayMongo Checkout Sessions) frequently report `cs_status=active` and `pi_status=awaiting_next_action` even after an attached e-wallet source (`src_...`) has expired, failed, or been canceled by the user.
- **Mandatory Terminal Resolution**: When `awaiting_next_action` is encountered and an underlying source token (`src_...`) is attached, the system must inspect the Source resource to detect terminal states (`expired`, `failed`, `cancelled`) and immediately transition the local `PaymentAttempt` to its corresponding terminal state (`Expired` / `Failed`).
- **Latency Protection Without Feature Elimination**: Latency goals must be achieved via concurrency, rate-limit cooldowns, and non-blocking background workers—NEVER by removing necessary gateway validation calls.

## 12. Dual-Naming Contract Parity & Resilient Notification Dispatch Invariant
- **Contract Aliasing**: Any API endpoint receiving email notification overrides must support both `email` and `to_email` JSON properties using explicit `[JsonPropertyName]` or custom property binding to prevent frontend/backend serialization drift.
- **Permissive Optional Bodies**: Minimal API endpoints that accept optional request payloads (e.g. `POST .../receipt/email`) must allow empty bodies, null payloads, and empty JSON objects (`{}`) without throwing 400 model binding errors (`[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]`).
- **Graceful Recipient Resolution**: When no override email is supplied in the request body, the backend must cascade from:
  1. `request.Email` / `request.ToEmail`
  2. `receipt.CustomerEmail` (persisted receipt record)
  3. Associated `PaymentAttempt.CustomerEmail` or `Order.CustomerEmail` (via `IReceiptSourceResolver`)
  4. Environment-configured default (`DEFAULT_RECEIPT_EMAIL` or fallback)
  If no email is discovered across all sources, return a descriptive error (`Receipts.MissingRecipientEmail`) with clear details indicating the missing recipient rather than a silent failure.


# Backend Payment Services & Providers Guide

This guide details the payment business logic, state machines, PayMongo gateway provider implementation, and webhook signature verification algorithms.

---

## 1. `Backend/services/payment_orchestrator.py`
- **Path**: [`Backend/services/payment_orchestrator.py`](file:///e:/Accounting/Backend/services/payment_orchestrator.py)
- **Primary Role**: The core transaction orchestrator managing authoritative pricing, payment lifecycle transitions, deduplication, and outbox fulfillment.

### Core Functions & Methods

#### `create_order(db, title, category, reference_code, amount_minor, quantity, user_id, metadata)`
- **Authoritative Server Pricing**:
  - Rejects or overrides unverified client-provided prices.
  - If `category == "product_order"`: Looks up unit cost in `Backend/data/product_data.json`. Computes `amount_minor = int(unit_price * quantity * 100)`. Falls back to ₱100.00 default if reference code is unmatched.
  - If `category == "department_invoice"`: Matches department budget in `Backend/data/configuration.json` and calculates a 10% monthly settlement allocation (`int((dept["budget"] / 10) * 100)` centavos).
- **Sequential Identifier**: Formats unique order numbers such as `ORD-202510-0001` or `INV-202510-0001`.

#### `initiate_payment_attempt(db, order_id, idempotency_key, payment_method, success_url, cancel_url)`
- **Preconditions**:
  - Rejects attempts on paid orders (`Order '...' is already fully paid`).
  - Rejects attempts on cancelled orders (`Order '...' has been cancelled and cannot accept payment attempts`).
- **Idempotency Check**:
  - Queries `PaymentAttempt` for matching `(provider, idempotency_key)`.
  - If an existing attempt is associated with a different `order_id`, raises `ValueError("Idempotency key '...' is already in use for another order")`.
  - If associated with the same order, returns the existing attempt without calling the gateway.
- **Gateway Dispatch**:
  - Creates the attempt in `pending` status.
  - Calls `provider.create_checkout_session()`.
  - Updates attempt with `provider_payment_id` (`cs_...`), `checkout_url`, and transitions status to `requires_action`.
  - Catches network/gateway exceptions, marks status as `unknown`, and stores `failure_message`.

#### `process_webhook(db, raw_body, signature_header)`
- **HMAC Signature Verification**:
  - Verifies the signature using constant-time comparison. Raises `PermissionError` (HTTP 401) on invalid signatures.
- **Event Deduplication**:
  - Checks if `(provider, provider_event_id)` already exists in `webhook_events`.
  - Returns `{"received": True, "duplicate": True}` immediately if already seen.
- **Monotonic State Transitions**:
  - Resolves target `PaymentAttempt` and `Order` by `provider_payment_id` or metadata `payment_attempt_id` / `order_id`.
  - When `checkout_session.payment.paid` or `payment.paid`:
    - If attempt is already `succeeded`, operates idempotently (no-op).
    - If another attempt already marked the order `paid`, marks attempt as `duplicate_paid_review` and sets `paid_at`.
    - Otherwise, transitions attempt to `succeeded`, sets `paid_at`, marks order `paid`, and queues a `FulfillmentOutbox` entry.
  - When `payment.failed` or `checkout_session.expired`:
    - Discards event if attempt is in a terminal state (`succeeded`, `failed`, `cancelled`, `expired`).
    - Otherwise, sets attempt to `failed` or `expired`.
- **Fulfillment Trigger**:
  - Immediately invokes `process_fulfillment_outbox(db)`.

#### `process_fulfillment_outbox(db)`
- Queries pending outbox entries (`status == "pending"`).
- Prepends an audit log into `Backend/data/configuration.json` (`auditLogs` array):
  ```json
  {
      "id": 1,
      "action": "Payment received: ₱1,250.00 for ORD-202510-0001 (Batch Production Run)",
      "user": "PayMongo Gateway",
      "time": "Just now",
      "severity": "success"
  }
  ```
- Marks outbox entry `status = "completed"` with `processed_at` timestamp.

#### `reconcile_payment_attempt(db, attempt_id)` & `reconcile_all_unresolved(db)`
- Queries the payment provider for authoritative session status via `get_checkout_session()`.
- Transitions local attempts and commits outbox records if settled remotely.
- Never reverts attempts already in `succeeded` status.

---

## 2. `Backend/providers/base.py`
- **Path**: [`Backend/providers/base.py`](file:///e:/Accounting/Backend/providers/base.py)
- **Primary Role**: Abstract interface definitions for payment gateway adapters.

### Data Classes & Methods
- `CheckoutSessionResult`: Encapsulates `provider_payment_id`, `checkout_url`, `status`, and `raw_response`.
- `PaymentStatusResult`: Encapsulates `provider_payment_id`, `status`, `paid_at`, `amount_minor`, `currency`, `failure_code`, `failure_message`, and `raw_response`.
- `PaymentProvider(ABC)`:
  - `create_checkout_session()`: Async abstract method for initiating hosted checkout sessions.
  - `get_checkout_session()`: Async abstract method for querying session settlement status.
  - `verify_webhook_signature()`: Abstract method for validating webhook authentication headers.

---

## 3. `Backend/providers/paymongo.py`
- **Path**: [`Backend/providers/paymongo.py`](file:///e:/Accounting/Backend/providers/paymongo.py)
- **Primary Role**: PayMongo Hosted Checkout Sessions adapter with dual live API and local sandbox simulation modes.

### Operation Modes
1. **Live PayMongo Mode** (`PAYMONGO_SECRET_KEY` configured):
   - Authenticates via HTTP Basic Auth (`Authorization: Basic base64(secret_key:)`).
   - Dispatches requests to `https://api.paymongo.com/v1/checkout_sessions`.
   - Supports Philippine payment rails: `qrph`, `gcash`, `paymaya`, `dob` (Direct Online Banking), `card`.
2. **Local Sandbox Simulator Mode** (`PAYMONGO_SECRET_KEY` missing):
   - Automatically generates mock sessions (`cs_sim_<uuid>`).
   - Routes checkouts to local runner URL: `http://localhost:3000/payment/sandbox/{attemptId}?session_id={simId}&order_id={orderId}`.
   - Allows complete end-to-end testing without external network dependencies.

### Webhook Cryptographic Verification Algorithm
- PayMongo dispatches signatures in the header:
  `Paymongo-Signature: t=<timestamp>,te=<test_mode_hash>,li=<live_mode_hash>`
- Verification steps:
  1. Parse header components by splitting on commas and `=` delimiters.
  2. Extract `timestamp = parts["t"]`.
  3. Construct the signed payload string: `f"{timestamp}.".encode("utf-8") + raw_body`.
  4. Compute HMAC-SHA256 digest using `PAYMONGO_WEBHOOK_SECRET`.
  5. Compare expected digest against `te` (test mode) or `li` (live mode) using `hmac.compare_digest` to prevent timing side-channel analysis.
- `sign_simulated_payload(raw_body, is_live)`:
  - Generates a valid `Paymongo-Signature` header for unit testing and local sandbox webhook simulations.

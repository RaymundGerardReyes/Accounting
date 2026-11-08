# PayMongo Webhook Operations Runbook & Diagnostic Guide

**Target Audience**: DevOps Engineers, Backend Engineers, QA Engineers, and Finance Operations Specialists.  
**Scope**: Inbound webhook delivery diagnostics, environment sandbox separation, URL route resilience, event subscription catalog, and dashboard configuration procedures for `Rge.Accounting`.

---

## 1. Architecture Overview

`Rge.Accounting` utilizes PayMongo's asynchronous webhook mechanism as the primary authoritative driver for real-time payment settlement, session lifecycle management, and BIR Official Receipt generation.

```
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│                               PAYMONGO CLOUD PLATFORM                                     │
│                                                                                           │
│  Customer Pays (Card / GCash / Maya / QRPh)                                              │
│         │                                                                                 │
│         ▼                                                                                 │
│  [Event Dispatcher] (Test Sandbox OR Live Production Sandbox)                            │
│         │                                                                                 │
│         │  POST https://<domain>/api/v1/webhooks/paymongo                                 │
│         │  Header: Paymongo-Signature: t=167...,te=...,li=...                             │
│         ▼                                                                                 │
└─────────┼─────────────────────────────────────────────────────────────────────────────────┘
          │
          ▼
┌───────────────────────────────────────────────────────────────────────────────────────────┐
│                               RGE.ACCOUNTING BACKEND                                      │
│                                                                                           │
│  1. DuplicateSlashNormalizationMiddleware                                                 │
│     Collapses consecutive slashes in Request.Path (e.g. "//api/..." -> "/api/...")        │
│                                                                                           │
│  2. Minimal API Endpoint Routing                                                          │
│     Matches /api/v1/webhooks/paymongo (or aliases: /api/payments/webhook,                 │
│     /webhooks/paymongo)                                                                   │
│                                                                                           │
│  3. PayMongoSignatureVerifier & HmacVerifier                                              │
│     Computes HMAC-SHA256 over "{timestamp}.{rawBody}" with Webhook Secret                 │
│     Constant-time byte comparison + 5-minute replay window tolerance                      │
│                                                                                           │
│  4. Fast-Path Ingestion & Multi-Key Correlation Cascade                                   │
│     Correlates cs_..., pay_..., src_..., pi_..., or Order Number to PaymentAttempt        │
│                                                                                           │
│  5. Atomic Ingestion & State Transition                                                   │
│     Updates PaymentAttempt & Order status                                                 │
│     Enqueues OrderPaidIntegrationEvent into Transactional Outbox                          │
│     Emits real-time SignalR notifications to Client UI                                    │
│                                                                                           │
│  6. OutboxWorker Fulfillment                                                              │
│     Generates BIR Official Receipt with SHA-256 audit digest                              │
│     Dispatches branded digital receipt via Google SMTP                                    │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Root Cause Diagnostics: "No event deliveries found for this webhook"

When inspecting a webhook in the **PayMongo Dashboard $\rightarrow$ Developers $\rightarrow$ Webhooks**, the UI may display:

> **"No event deliveries found for this webhook."**

This indicates that PayMongo's delivery workers have never dispatched an HTTP request to this webhook record, or that dispatched attempts were suppressed due to auto-disablement. Follow this diagnostic decision tree to identify the root cause:

### Root Cause 1: Test Mode vs. Live Mode Sandbox Isolation (Most Common)

PayMongo strictly isolates accounts into two independent sandboxes:

| Parameter | Test Mode | Live Mode |
|---|---|---|
| **Secret API Key** | `sk_test_...` | `sk_live_...` |
| **Public API Key** | `pk_test_...` | `pk_live_...` |
| **Webhook Secret** | `whsk_test_...` (or hex string) | `whsk_live_...` (or hex string) |
| **Event Dispatch Scope** | Only sends to webhooks created in **Test Mode** | Only sends to webhooks created in **Live Mode** |
| **Dashboard Toggle** | Banner: *"Viewing test data"* | Banner: *"Viewing live data"* |

#### The Failure Mechanism:
1. An administrator creates a webhook while the PayMongo dashboard is toggled to **Live Mode**.
2. A developer tests the checkout flow using `PAYMONGO_SECRET_KEY=sk_test_...` in `.env`.
3. PayMongo processes the test payment and dispatches events **strictly within the Test sandbox**.
4. The Live webhook endpoint is completely bypassed. Delivery count remains `0`.
5. The administrator views the Live webhook details and sees: *"No event deliveries found for this webhook."*

#### Diagnostic Steps:
1. Verify the environment toggle in the PayMongo dashboard (bottom-left sidebar or header).
2. Check `E:\Accounting\.env`:
   - If `PAYMONGO_SECRET_KEY` starts with `sk_test_`, your application is operating in Test Mode. You must configure and inspect webhooks under **"Viewing test data"**.
   - If `PAYMONGO_SECRET_KEY` starts with `sk_live_`, your application is operating in Live Mode. You must configure and inspect webhooks under **"Viewing live data"**.

---

### Root Cause 2: Double-Slash Endpoint URL (`.com//api/v1/webhooks/paymongo`) & Auto-Disablement

#### The Failure Mechanism:
1. When registering the webhook URL, merchants frequently paste their root domain with a trailing slash (`https://rge-accounting.com/`) and append the documented endpoint path (`/api/v1/webhooks/paymongo`), creating:
   `https://rge-accounting.com//api/v1/webhooks/paymongo`
2. PayMongo's webhook sender transmits the request line verbatim:
   `POST //api/v1/webhooks/paymongo HTTP/1.1`
3. In standard ASP.NET Core Kestrel, `EndpointRoutingMiddleware` tokenizes the path by `/`. Because the path begins with `//`, segment 0 is evaluated as an empty string `""`.
4. Endpoint routing fails to match the route template `/api/v1/webhooks/paymongo` and returns **HTTP 404 Not Found**.
5. PayMongo receives HTTP 404 and schedules retries with exponential backoff.
6. Once retry attempts are exhausted, PayMongo flags the webhook as **Disabled** (`status: "disabled"`).
7. Once disabled, PayMongo completely suppresses all future delivery attempts. Subsequent transactions will not generate delivery logs!

#### Diagnostic Steps:
1. Inspect the registered URL in the PayMongo Dashboard. Look for consecutive slashes (e.g. `.com//api`).
2. Check the Webhook status badge. If it shows **Disabled**, retries have been exhausted.
3. Check application logs for:
   `[PAYMONGO-WEBHOOK-REJECTED] Path=//api/v1/webhooks/paymongo` or 404 responses from Kestrel.

---

### Root Cause 3: API Key & Webhook Secret Environment Mismatch

#### The Failure Mechanism:
- The backend is configured with a Live Secret Key (`sk_live_...`) but configured with a Test Webhook Secret (`whsk_test_...`), or vice versa.
- When PayMongo dispatches a live webhook, it signs the payload with the live secret key and populates `li=<live_signature>` in the `Paymongo-Signature` header.
- The backend calculates the expected HMAC-SHA256 signature using the configured `whsk_test_...` key.
- The computed hash does not match `candidateBytes`.
- The backend logs:
  `[PAYMONGO-WEBHOOK-REJECTED] Path=/api/v1/webhooks/paymongo | Reason=InvalidSignature`
  and returns **HTTP 400 Bad Request**.
- PayMongo interprets HTTP 400 as a delivery failure, initiates backoff, and eventually disables the webhook.

---

### Root Cause 4: Missing or Unsubscribed Event Types

#### The Failure Mechanism:
- If a webhook was created subscribing only to `payment.paid`, but checkout was performed using Hosted Checkout Sessions (`checkout_session.payment.paid`), PayMongo will **not** dispatch `checkout_session.payment.paid` to that webhook.
- Deliveries table shows 0 events because no subscribed events were triggered.

---

## 3. Required PayMongo Webhook Event Subscriptions & Domain Mapping

To ensure 100% automated payment fulfillment and session state synchronization, the webhook **MUST** be subscribed to the following events:

| # | Event String | Channel / Scope | Domain Entity & State Transition | Side Effects & Business Impact |
|---|---|---|---|---|
| 1 | `checkout_session.payment.paid` | Hosted Checkout (Cards, GCash, Maya, QRPh, DOB) | `PaymentAttempt` $\rightarrow$ `Succeeded`<br>`Order` $\rightarrow$ `Paid` | **Primary Settlement**: Enqueues `OrderPaidIntegrationEvent` into transactional outbox; OutboxWorker generates BIR Official Receipt with SHA-256 audit digest; emails PDF receipt to customer; broadcasts SignalR `succeeded` status to UI. |
| 2 | `payment.paid` | Direct Payments & Captured Sources | `PaymentAttempt` $\rightarrow$ `Succeeded`<br>`Order` $\rightarrow$ `Paid` | **Direct Charge Settlement**: Correlates via `source.id`, `checkout_session_id`, external order number, or metadata; marks attempt succeeded and enqueues outbox message. Idempotent guard prevents duplicate fulfillment. |
| 3 | `checkout_session.expired` | Hosted Checkout Sessions | `PaymentAttempt` $\rightarrow$ `Expired` | **Session Expiration**: Clears `attempt.CheckoutUrl`; calls PayMongo remote API `POST /v1/checkout_sessions/{id}/expire` to invalidate public payment link; broadcasts SignalR `expired` event. |
| 4 | `qrph.expired` | National QR Ph Standard | `PaymentAttempt` $\rightarrow$ `Expired` | **QR Ph Expiration**: Marks attempt `Expired`, clears checkout URL, locks remote session, notifies client UI via SignalR. |
| 5 | `qr_code.expired` | Dynamic QR Codes (Legacy Fallback) | `PaymentAttempt` $\rightarrow$ `Expired` | **QR Code Expiration**: Fallback compatibility for legacy QR payloads; transitions attempt to `Expired`. |
| 6 | `payment.failed` | Card Declines, 3DS Failures, Insufficient Funds | `PaymentAttempt` $\rightarrow$ `Failed` | **Payment Failure**: Stores `failed_code` and `failed_message`; locks remote checkout session; pushes SignalR `failed` event. Parent order remains `Pending` to allow customer retry. |
| 7 | `checkout_session.payment.failed` | Hosted Checkout Card Failures | `PaymentAttempt` $\rightarrow$ `Failed` | **Session Payment Failure**: Captures individual card attempt declines within a hosted session. |
| 8 | `source.chargeable` | E-Wallets (GCash / Maya Sources API) | `PaymentAttempt` $\rightarrow$ `Processing` | **E-Wallet Authorization**: Customer approved authorization on e-wallet portal; updates state and broadcasts SignalR `processing` state. |

---

## 4. Operational Runbook: Configuring Webhooks in PayMongo Dashboard

Follow this step-by-step procedure when deploying to new environments or reconfiguring existing webhooks:

### Step 1: Select Active Environment Mode
1. Open the [PayMongo Dashboard](https://dashboard.paymongo.com/).
2. Look at the mode indicator (bottom of left navigation bar or top header):
   - For **Development / Staging / Local QA**: Switch toggle to **"Viewing test data"**.
   - For **Production**: Switch toggle to **"Viewing live data"**.
   - ⚠️ *Note: Webhooks created in Test Mode do NOT receive Live transactions, and vice versa.*

### Step 2: Navigate to Webhook Settings
1. In the left navigation sidebar, expand **Developers**.
2. Click on **Webhooks**.

### Step 3: Enter the Webhook Endpoint URL
1. Click **"Create Webhook"** (or **"Add Webhook"**).
2. Enter the **Webhook URL**:
   - **Production (Canonical)**: `https://<your-domain>/api/v1/webhooks/paymongo`
   - **Supported Aliases**: `https://<your-domain>/api/payments/webhook` or `https://<your-domain>/webhooks/paymongo`
   - ⚠️ **Double Slash Precaution**: Check the text field carefully. Ensure there is only a single slash between the domain and path:
     - ❌ `https://acme-finance.com//api/v1/webhooks/paymongo`
     - ✅ `https://acme-finance.com/api/v1/webhooks/paymongo`
   - **Local Testing**: PayMongo cannot send webhooks to `http://localhost`. Run `ngrok http 5080` and enter your HTTPS forwarding URL:
     `https://<unique-subdomain>.ngrok-free.app/api/v1/webhooks/paymongo`

### Step 4: Select Event Subscriptions
Check the boxes for all 7 required events:
- [x] `checkout_session.payment.paid`
- [x] `payment.paid`
- [x] `payment.failed`
- [x] `checkout_session.expired`
- [x] `qrph.expired`
- [x] `qr_code.expired`
- [x] `source.chargeable`

Click **Save** or **Create Webhook**.

### Step 5: Extract Webhook Signing Secret Key
1. In the newly created webhook's details page, locate the **Signing Secret** / **Webhook Secret** card.
2. Click **Click to reveal** and copy the secret key.
   - Format: `whsk_test_...` (for test mode) or `whsk_live_...` (for live mode).

### Step 6: Configure Application Environment Variables
Set the secrets in your environment configuration (`E:\Accounting\.env` or Heroku Config Vars):

#### Test Environment:
```env
PAYMONGO_SECRET_KEY=PLACEHOLDER_PAYMONGO_TEST_SECRET_KEY
PAYMONGO_PUBLIC_KEY=PLACEHOLDER_PAYMONGO_TEST_PUBLIC_KEY
PAYMONGO_WEBHOOK_SECRET=PLACEHOLDER_PAYMONGO_TEST_WEBHOOK_SECRET
```

#### Production Environment:
```env
PAYMONGO_SECRET_KEY=PLACEHOLDER_PAYMONGO_LIVE_SECRET_KEY
PAYMONGO_PUBLIC_KEY=PLACEHOLDER_PAYMONGO_LIVE_PUBLIC_KEY
PAYMONGO_WEBHOOK_SECRET=PLACEHOLDER_PAYMONGO_LIVE_WEBHOOK_SECRET
```

> **Warning**: Never mix a `sk_test_...` key with a `whsk_live_...` secret, or an `sk_live_...` key with a `whsk_test_...` secret!

### Step 7: Delivery Health Verification
1. Initiate a test checkout payment.
2. Return to **PayMongo Dashboard $\rightarrow$ Developers $\rightarrow$ Webhooks $\rightarrow$ [Select Webhook]**.
3. Under **Event Deliveries**, verify the recent delivery record:
   - **HTTP 200 OK**: Request was received, signature verified, and successfully processed.
   - **HTTP 400 Bad Request**: Check signature secret matching (`PAYMONGO_WEBHOOK_SECRET`) or replay window timestamp skew.
   - **HTTP 404 Not Found**: Endpoint route mismatch. Ensure `DuplicateSlashNormalizationMiddleware` is deployed.
4. If the webhook status is **Disabled**, click **"Enable Webhook"** to reset the retry counter and re-activate event deliveries.

---

## 5. Automated Setup Utility API

For programmatic configuration during CI/CD or container provisioning, `Rge.Accounting` provides an administrative setup endpoint:

```http
POST /api/v1/admin/webhooks/paymongo/setup HTTP/1.1
Host: <your-domain>
```

### What It Does:
1. Automatically discovers the host and protocol from `X-Forwarded-Proto` and `X-Forwarded-Host` headers.
2. Queries PayMongo API `GET /v1/webhooks` with configured `PAYMONGO_SECRET_KEY`.
3. If an existing webhook matches the current domain:
   - If disabled, automatically calls `POST /v1/webhooks/{id}/enable` to restore active delivery.
4. If no webhook exists for the domain:
   - Calls `POST /v1/webhooks` to create a new webhook pointing to `/api/v1/webhooks/paymongo`.
   - Returns the newly created `secret_key` in the JSON response so it can be added to `.env`.

---

## 6. Troubleshooting Matrix

| Symptom / Error | Root Cause | Remediation Procedure |
|---|---|---|
| **"No event deliveries found for this webhook"** | Dashboard is viewing Live mode, but transactions were generated with `sk_test_...` | Switch dashboard toggle to "Viewing test data" or update application `.env` to live keys. |
| **Delivery status shows HTTP 404** | Registered URL contains double slash `//api/...` or incorrect path | Ensure `DuplicateSlashNormalizationMiddleware` is active in `Program.cs`. Update URL in PayMongo dashboard to canonical `/api/v1/webhooks/paymongo`. |
| **Delivery status shows HTTP 400 (`InvalidSignature`)** | `PAYMONGO_WEBHOOK_SECRET` does not match the webhook signing secret for this environment | Copy `whsk_...` from the PayMongo dashboard webhook details page and update `PAYMONGO_WEBHOOK_SECRET` in `.env`. Restart backend. |
| **Delivery status shows HTTP 400 (`timestamp expired`)** | Server clock skew exceeds 300 seconds (5 minutes) | Synchronize server clock via NTP (`w32tm /resync` on Windows or `ntpdate` / `timedatectl` on Linux). |
| **Webhook marked "Disabled"** | PayMongo exhausted retry backoff after multiple 4xx/5xx responses | Fix the root cause (URL or secret), then click **"Enable Webhook"** in the PayMongo dashboard or call `POST /api/v1/admin/webhooks/paymongo/setup`. |
| **Webhook delivers HTTP 200, but order status remains Pending** | Outbox worker stopped or failed to deliver `OrderPaidIntegrationEvent` | Follow `docs/runbooks/webhook-outbox-replay.md` to inspect `fulfillment_outbox` and reset stalled events. |
| **SignalR client does not update** | Event correlation failed to resolve local `PaymentAttempt` | Inspect logs for `[PAYMONGO-WEBHOOK-ACK-200] ... status=ignored`. Verify that checkout session metadata includes `payment_attempt_id` or that provider session ID matches `attempt.provider_payment_id`. |

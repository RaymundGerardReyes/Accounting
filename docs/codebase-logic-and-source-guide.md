# Acme Cost Accounting & Live Payment Orchestration Platform
## Comprehensive Architecture, Logic Workflow & Source Code Guide

---

## Executive Summary

This document provides an exhaustive, authoritative technical guide to the **Acme Cost Accounting & Live Payment Orchestration Platform** (`E:\Accounting`). It covers the dual operational domains of the codebase:
1. **Legacy Manufacturing Cost Analysis**: Department cost allocation, volume-based/activity-based distribution, unit cost trends, and budget-vs-actual variance reporting.
2. **Modern Payment Gateway Orchestration**: Server-authoritative order management, PayMongo Hosted Checkout Sessions (QR Ph, GCash, Maya, Cards, Online Banking), cryptographic HMAC-SHA256 webhook lifecycle management, durable fulfillment outbox workers, and an interactive local sandbox simulator.

---

## 1. /goal — Architectural Mission & Core Invariants

The platform bridges legacy enterprise cost accounting with real-time financial transaction orchestration. The primary goals are:

- **Authoritative Server Pricing**: Eliminate client-side price tampering by resolving product costs and department settlement allocations exclusively on the server from validated master records.
- **Strict Gateway State Machine**: Enforce monotonic transitions where payments never revert once resolved to terminal states (`succeeded`, `failed`, `cancelled`, `expired`).
- **Cryptographic Webhook Ingress**: Validate every incoming gateway event using HMAC-SHA256 signatures with secret keys and reject forged or altered payloads.
- **Idempotency & Replay Protection**: Prevent double-charges and duplicate fulfillment records via unique constraints on `(provider, idempotency_key)` and `(provider, provider_event_id)`.
- **Durable Fulfillment Outbox**: Guarantee that database state transitions commit atomically before asynchronous fulfillment tasks or audit trail updates are processed.
- **100% Backward Compatibility**: Preserve all legacy REST endpoints (`/api/dashboard`, `/api/department/{slug}`, `/api/products`, `/api/variance`, `/api/configuration`) and UI views without breaking existing dependencies.
- **Localization & Accounting Standards**: Maintain Philippine Peso (`₱` / PHP) formatting, UTF-8 Byte Order Mark (`\uFEFF`) CSV exports for Excel compatibility, and the accounting variance invariant:
  $$\text{Cost Variance} = \text{Budget (Allocated)} - \text{Actual}$$

---

## 2. /learn — Core Concepts & Theoretical Foundations

### 2.1 Cost Accounting & Allocation Models
In manufacturing management, indirect overhead costs must be allocated to production departments and individual product lines. The system supports four models implemented in `Frontend/lib/cost-allocation.ts`:
- **Direct Cost Allocation**: Overhead is allocated in proportion to the direct cost incurred by each department:
  $$\text{Allocated Cost}_i = \left(\frac{\text{Direct Cost}_i}{\sum \text{Direct Cost}}\right) \times \text{Total Cost}$$
- **Volume-Based Allocation**: Allocates cost based on physical production units:
  $$\text{Allocated Cost}_i = \left(\frac{\text{Volume}_i}{\sum \text{Volume}}\right) \times \text{Total Cost}$$
- **Machine-Hours Allocation**: Allocates equipment depreciation, power, and maintenance based on recorded machine operation hours:
  $$\text{Allocated Cost}_i = \left(\frac{\text{Machine Hours}_i}{\sum \text{Machine Hours}}\right) \times \text{Total Cost}$$
- **Activity-Based Costing (ABC)**: Breaks down manufacturing into discrete cost pools (setup, quality inspection, material handling) with cost driver rates:
  $$\text{Cost per Driver Unit} = \frac{\text{Activity Cost}}{\text{Total Driver Volume}}$$
  $$\text{Department Cost} = \sum (\text{Driver Rate} \times \text{Department Activity Weight})$$

### 2.2 Budget Variance Sign Convention
Per standard managerial accounting:
- **Favorable ($\ge 0$)**: Actual expenditure was less than or equal to budget. Represents cost savings. Rendered in neutral/foreground styling.
- **Unfavorable ($< 0$)**: Actual expenditure exceeded budget. Represents an overrun. Rendered in high-visibility accent (`text-accent` / red).

### 2.3 PayMongo Hosted Checkout & Webhook Lifecycle
PayMongo utilizes hosted checkout sessions (`cs_...`) to abstract Philippine payment channels:
- **E-Wallets**: GCash, Maya
- **National QR**: QR Ph (interoperable BSP standard)
- **Direct Online Banking (DOB)**: BPI, UnionBank
- **Card Schemes**: Visa, Mastercard

#### Signature Verification Algorithm
The PayMongo webhook header format is:
```http
Paymongo-Signature: t=<timestamp>,te=<test_signature>,li=<live_signature>
```
The server validates the payload by generating an HMAC-SHA256 digest:
$$\text{Digest} = \text{HMAC-SHA256}\left(\text{key} = \text{webhook\_secret},\, \text{message} = \text{timestamp} \,\|\, "." \,\|\, \text{raw\_body}\right)$$
The computed digest is compared against `te` (test mode) or `li` (live mode) using constant-time comparison (`hmac.compare_digest`) to prevent timing side-channel attacks.

### 2.4 State Machine & Monotonicity
Payment attempts traverse a finite state machine:
```
                [Initiated]
                     │
                     ▼
                 [pending]
                     │
          ┌──────────┴──────────┐
          ▼                     ▼
  [requires_action]        [unknown] (network timeout)
          │                     │
     ┌────┴────────────┐        │
     ▼                 ▼        │
[processing]       [failed]     │
     │                 ▲        │
     ├───────────┐     │        │
     ▼           ▼     │        │
[succeeded]  [expired] ┼────────┘
```
**The Monotonic Terminal Invariant**: Once an attempt enters `succeeded`, no subsequent webhook (such as a delayed `payment.failed` or `checkout_session.expired`) can downgrade or alter the state.

---

## 3. /plan — End-to-End Logic Workflows

### Workflow A: Legacy Dashboard & Cost Analytics Flow
```
User navigates to Dashboard
  ├──> Frontend (components/dashboard.tsx)
  │      └──> fetch('/api/dashboard')
  │             └──> Next.js Proxy Handler (app/api/[...path]/route.ts)
  │                    └──> FastAPI Backend (app.py: get_dashboard_data)
  │                           └──> read_json_file("dashboard_data.json")
  │                                  └──> Returns JSON payload
  └──> Recharts components render Cost Trends, Department Pie/Bar, Metrics Cards
```

### Workflow B: Order Creation with Authoritative Pricing
```
Admin creates Order in Payments Hub
  ├──> Frontend: createOrder({ title, category, reference_code, quantity })
  ├──> Proxy Handler forwards POST /api/v1/orders to FastAPI
  ├──> PaymentOrchestrator.create_order():
  │      ├──> If category == 'product_order':
  │      │      Reads product_data.json -> finds matching unitCost -> amount_minor = unitCost * quantity * 100
  │      ├──> If category == 'department_invoice':
  │      │      Reads configuration.json -> matches department budget -> calculates 10% monthly settlement
  │      └──> Generates Order record with status = 'pending' and order_number (e.g., ORD-202510-0001)
  └──> Returns 201 Created with persisted Order object
```

### Workflow C: Payment Attempt Initiation & Gateway Handoff
```
User clicks "Pay via PayMongo"
  ├──> CheckoutButton creates client idempotency key: `idemp_${orderId}_${Date.now()}`
  ├──> Calls POST /api/v1/orders/{orderId}/payment-attempts
  ├──> PaymentOrchestrator.initiate_payment_attempt():
  │      ├──> Verifies Order status != 'paid' and != 'cancelled'
  │      ├──> Checks existing attempt by idempotency_key (returns existing attempt if found)
  │      ├──> Inserts PaymentAttempt record with status = 'pending'
  │      ├──> Calls PayMongoProvider.create_checkout_session()
  │      │      ├──> Live Mode: POST https://api.paymongo.com/v1/checkout_sessions
  │      │      └──> Sandbox Mode: Generates cs_sim_... session and local sandbox URL
  │      ├──> Updates PaymentAttempt with provider_payment_id, checkout_url, status = 'requires_action'
  │      └──> Commits transaction
  ├──> Frontend validates origin via isApprovedGatewayOrigin(checkout_url)
  └──> Redirects browser to Checkout URL
```

### Workflow D: Webhook Ingress, Deduplication & Fulfillment Outbox
```
PayMongo Gateway dispatches HTTP POST /api/v1/webhooks/paymongo
  ├──> FastAPI receives raw_body bytes and Paymongo-Signature header
  ├──> PaymentOrchestrator.process_webhook():
  │      ├──> Step 1: verify_webhook_signature() -> rejects with 401 if invalid
  │      ├──> Step 2: Extracts provider_event_id and checks WebhookEvent table
  │      │      └──> If duplicate -> returns { received: true, duplicate: true }
  │      ├──> Step 3: Inserts WebhookEvent record (processing_status = 'received')
  │      ├──> Step 4: Resolves PaymentAttempt and Order by metadata / provider_payment_id
  │      ├──> Step 5: Applies state transition rules:
  │      │      ├──> If event == 'checkout_session.payment.paid':
  │      │      │      ├──> If attempt already 'succeeded' -> no-op
  │      │      │      ├──> If order already 'paid' by another attempt -> 'duplicate_paid_review'
  │      │      │      └──> Else: attempt.status = 'succeeded', order.status = 'paid'
  │      │      │             Inserts FulfillmentOutbox record (status = 'pending')
  │      │      ├──> If event == 'payment.failed':
  │      │      │      └──> If attempt not in terminal state -> attempt.status = 'failed'
  │      │      └──> If event == 'checkout_session.expired':
  │      │             └──> If attempt not in terminal state -> attempt.status = 'expired'
  │      ├──> Step 6: WebhookEvent.processing_status = 'processed'
  │      ├──> Step 7: Commits database transaction
  │      └──> Step 8: Calls process_fulfillment_outbox():
  │             ├──> Reads pending outbox entries
  │             ├──> Prepends payment audit record into configuration.json ("auditLogs")
  │             ├──> Sets FulfillmentOutbox.status = 'completed'
  │             └──> Commits database transaction
  └──> Returns 200 OK to PayMongo gateway
```

### Workflow E: Browser Return & Client-Side Verification Loop
```
User completes payment at gateway -> redirected to /payment/return?order_id=...
  ├──> /payment/return resolves order's latest payment attempt ID
  ├──> Redirects to /payment/result/[paymentId]
  ├──> PaymentResultPage starts polling GET /api/v1/payments/{paymentId}
  │      ├──> Continues polling while status is non-terminal (up to 30 attempts, 60s)
  │      └──> Once terminal ('succeeded'): Displays success confirmation & receipt
  └──> Fallback Action: User clicks "Reconcile Status Now"
         └──> Calls POST /api/v1/admin/payments/{paymentId}/reconcile
                └──> Queries PayMongo directly, commits state, and updates outbox
```

---

## 4. /grill-me — Critical Self-Critique & Edge-Case Analysis

| Vulnerability / Edge Case | Risk Level | Architectural Mitigation in Codebase |
|---|---|---|
| **Client-side Redirect Trust** | Fatal Security Flaw | Client redirects to `/payment/return` **never** mark an order as paid. Only signed webhooks or backend reconciliation can transition payments to `succeeded`. |
| **Tampered Price in Order Submission** | Critical Financial Bug | `create_order()` validates inputs against backend master records (`product_data.json` or `configuration.json`), ignoring unverified client price payloads. |
| **Out-of-Order Webhook Delivery** | State Corruption | A delayed `payment.failed` arriving after `checkout_session.payment.paid` is discarded because `succeeded` is an immutable terminal state. |
| **Webhook Replay Attacks** | Duplicate Fulfillment | Unique constraint on `webhook_events(provider, provider_event_id)` immediately catches duplicate events and responds with `{ duplicate: true }`. |
| **Multiple Concurrent Attempts for Same Order** | Double Charging | If attempt #1 succeeds, attempt #2 receiving a late payment transition is placed into `duplicate_paid_review` rather than silently double-crediting. |
| **Gateway Timeout on Session Creation** | Ghost Transaction | Network exceptions during session creation transition the attempt to `unknown` rather than auto-retrying, requiring manual or scheduled reconciliation. |
| **Non-ASCII CSV Mojibake in Excel** | Data Corruption | All CSV downloads (`variance-analysis.tsx`) prepend the UTF-8 BOM (`\uFEFF`), preventing Windows CP-1252 misinterpretation of `₱`. |
| **Dev Server Adapter Latency (Windows)** | UX Degradation | Dev script binds explicitly to `localhost` (`next dev -H localhost`) to bypass 9.8-second Windows APIPA adapter fallback lookups. |

---

## 5. /teamwork-preview — Subsystem Interoperability Matrix

```
┌────────────────────────────────────────────────────────────────────────┐
│                          Next.js 15 Frontend                           │
│  ┌────────────────────┐   ┌───────────────────┐   ┌─────────────────┐  │
│  │ Dashboard & Charts │   │ Payments Hub & UI │   │ Sandbox Runner  │  │
│  └─────────┬──────────┘   └─────────┬─────────┘   └────────┬────────┘  │
│            │                        │                      │           │
│            ▼                        ▼                      ▼           │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │          Next.js Route Proxy (/app/api/[...path]/route.ts)       │  │
│  └──────────────────────────────────┬───────────────────────────────┘  │
└─────────────────────────────────────┼──────────────────────────────────┘
                                      │ HTTP Proxied Requests (Port 5000)
                                      ▼
┌────────────────────────────────────────────────────────────────────────┐
│                           FastAPI 2.0 Backend                          │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ app.py (CORS, Lifespan, Legacy Routes & Payment v1 Routes)       │  │
│  └──────────────┬──────────────────────────────────┬────────────────┘  │
│                 │                                  │                   │
│                 ▼                                  ▼                   │
│  ┌──────────────────────────────┐   ┌───────────────────────────────┐  │
│  │ Legacy Flat Files (data/*.json)│  │ PaymentOrchestrator (services)│  │
│  └──────────────────────────────┘   └──────────────┬────────────────┘  │
│                                                    │                   │
│         ┌──────────────────────────────────────────┴────────┐          │
│         ▼                                                   ▼          │
│  ┌──────────────────────────────┐           ┌───────────────────────┐  │
│  │ PayMongoProvider (providers) │           │ SQLAlchemy 2.0 Engine │  │
│  │  - Live HTTPS api.paymongo   │           │  - Orders Table       │  │
│  │  - Local Sandbox Simulator   │           │  - PaymentAttempts    │  │
│  │  - HMAC-SHA256 Signer/Verify │           │  - WebhookEvents      │  │
│  └──────────────┬───────────────┘           │  - FulfillmentOutbox  │  │
│                 │                           └──────────────┬────────┘  │
└─────────────────┼──────────────────────────────────────────┼───────────┘
                  │                                          │
                  ▼                                          ▼
     ┌─────────────────────────┐               ┌─────────────────────────┐
     │ PayMongo Hosted Gateway │               │ SQLite / PostgreSQL DB  │
     └─────────────────────────┘               └─────────────────────────┘
```

---

## 6. /boost — Enterprise Production Hardening & Recommendations

1. **PostgreSQL Migration**: Swap SQLite for production PostgreSQL by defining `DATABASE_URL=postgresql+psycopg2://user:pass@host:5432/accounting_db` in `Backend/.env`. The schema in `Backend/schema.sql` is already fully normalized with indexes and foreign keys.
2. **Background Celery / APScheduler Outbox Worker**: Move `process_fulfillment_outbox()` and `reconcile_all_unresolved()` to an asynchronous background worker (e.g. Celery with Redis or APScheduler) running every 60 seconds.
3. **Database-Backed Master Configuration**: Migrate `configuration.json` and department flat files into PostgreSQL tables to support atomic ACID updates across concurrent administration sessions.
4. **Content Security Policy (CSP)**: Enforce a strict CSP header in `next.config.mjs` restricting frame-ancestors, connect-src to `api.paymongo.com` and `localhost:5000`.

---

## 7. Exhaustive Source Code Breakdown & Modular Guide Index

In addition to this consolidated technical manual, dedicated standalone source guides are provided in [`docs/source-guides/`](file:///e:/Accounting/docs/source-guides/):

| Guide Document | Target Scope & Source Files Covered |
|---|---|
| [**Backend Core Services Guide**](file:///e:/Accounting/docs/source-guides/backend-core.md) | `app.py`, `database.py`, `models.py`, `run.py`, `schema.sql` |
| [**Backend Payment Services Guide**](file:///e:/Accounting/docs/source-guides/backend-payment-services.md) | `services/payment_orchestrator.py`, `providers/base.py`, `providers/paymongo.py` |
| [**Backend Master Data & Flat Files Guide**](file:///e:/Accounting/docs/source-guides/backend-data-and-seed.md) | `data/configuration.json`, `data/dashboard_data.json`, `data/department_detail_data.json`, `data/product_data.json`, `data/variance_data.json` |
| [**Frontend Architecture & Routes Guide**](file:///e:/Accounting/docs/source-guides/frontend-architecture-and-pages.md) | `app/layout.tsx`, `app/globals.css`, `app/page.tsx`, `app/api/[...path]/route.ts`, `app/checkout/page.tsx`, `app/payment/return/page.tsx`, `app/payment/result/[paymentId]/page.tsx`, `app/payment/sandbox/[attemptId]/page.tsx` |
| [**Frontend Components & UI Hub Guide**](file:///e:/Accounting/docs/source-guides/frontend-components-hub.md) | `dashboard.tsx`, `department-detail.tsx`, `product-line-analysis.tsx`, `variance-analysis.tsx`, `configuration.tsx`, `payments-hub.tsx`, `checkout-button.tsx`, `sidebar.tsx`, `theme-provider.tsx`, `ui/*` |
| [**Frontend Libraries & Custom Hooks Guide**](file:///e:/Accounting/docs/source-guides/frontend-libraries-and-hooks.md) | `lib/cost-allocation.ts`, `lib/payment-api.ts`, `lib/design-system.ts`, `lib/utils.ts`, `hooks/use-mobile.ts`, `hooks/use-toast.ts` |
| [**Testing, Git Automation & Governance Guide**](file:///e:/Accounting/docs/source-guides/testing-and-automation.md) | `Backend/test_orchestration.py`, `scripts/git_workflow.py`, `.agents/rules/*`, `.agents/skills/*` |

---

### 7.1 Backend Source Files

#### `Backend/app.py`
- **Location**: [app.py](file:///e:/Accounting/Backend/app.py)
- **Role**: FastAPI primary application server entrypoint.
- **Key Symbols**:
  - `lifespan(app: FastAPI)`: Initializes database tables and inserts seed records via `init_db()`.
  - `read_json_file(filename)` / `write_json_file(filename, data)`: Thread-safe file I/O for legacy data files.
  - Legacy Endpoints:
    - `GET /api/dashboard`: Serves high-level cost trends, metrics, and department breakdowns.
    - `GET /api/department/{department_name}`: Normalizes slugs and returns detailed department cost breakdowns.
    - `GET /api/products`: Returns product line unit cost, volume, margins, and profitability.
    - `GET /api/variance`: Returns category-level budget vs. actual variance metrics.
    - `GET /api/configuration`: Returns company settings, department lists, and user access records.
    - `PUT /api/configuration`: Persists updates to general configuration.
    - `POST /api/configuration/departments`: Persists modified department structures.
    - `POST /api/configuration/users`: Persists modified user rosters.
  - Payment Orchestration Endpoints:
    - `GET /api/v1/orders`: Returns list of orders with latest payment attempt attached.
    - `POST /api/v1/orders`: Creates a new order using authoritative pricing.
    - `GET /api/v1/orders/{order_id}`: Retrieves single order with full attempt history.
    - `POST /api/v1/orders/{order_id}/payment-attempts`: Creates an idempotent payment attempt.
    - `GET /api/v1/payments/{payment_id}`: Polling endpoint for payment attempt state.
    - `POST /api/v1/webhooks/paymongo`: Secure webhook ingress with HMAC-SHA256 signature verification.
    - `POST /api/v1/admin/payments/{payment_id}/reconcile`: Manual single attempt reconciliation.
    - `POST /api/v1/admin/payments/reconcile-all`: Bulk reconciliation for unresolved attempts.
    - `GET /api/v1/admin/webhook-events`: Audit log of recorded gateway events.
    - `GET /api/v1/admin/fulfillment-outbox`: Outbox table monitor.
    - `POST /api/v1/sandbox/simulate-webhook`: Interactive local webhook simulator generating valid HMAC signatures.

#### `Backend/database.py`
- **Location**: [database.py](file:///e:/Accounting/Backend/database.py)
- **Role**: SQLAlchemy 2.0 database engine and session management.
- **Key Symbols**:
  - `engine`: Configured with pooling (`pool_pre_ping=True`) and SQLite connection argument overrides (`check_same_thread=False`).
  - `SessionLocal`: Session factory.
  - `Base`: Declarative model base.
  - `get_db()`: Dependency yielding database sessions with automatic closure.

#### `Backend/models.py`
- **Location**: [models.py](file:///e:/Accounting/Backend/models.py)
- **Role**: Relational database schema models.
- **Key Classes**:
  - `Order`: Represents a customer product order or department settlement invoice. Contains `amount_minor` (centavos), `status` (`pending`, `paid`, `cancelled`), and relationships to payment attempts and fulfillment outbox.
  - `PaymentAttempt`: Tracks gateway payment attempts. Enforces unique constraint on `(provider, idempotency_key)` to guarantee idempotency. Statuses: `pending`, `requires_action`, `processing`, `succeeded`, `failed`, `cancelled`, `expired`, `unknown`, `duplicate_paid_review`.
  - `WebhookEvent`: Deduplication record for gateway events. Enforces unique constraint on `(provider, provider_event_id)`.
  - `FulfillmentOutbox`: Durable post-payment processing queue item.
  - `init_db()`: Creates schema tables and seeds initial production orders and invoices.

#### `Backend/run.py` & `Backend/run`
- **Location**: [run.py](file:///e:/Accounting/Backend/run.py) & [run](file:///e:/Accounting/Backend/run)
- **Role**: Command runner CLI for developer productivity.
- **Key Commands**:
  - `python run.py server`: Runs Uvicorn FastAPI server on port 5000 with live reload.
  - `python run.py model`: Migrates tables and seeds initial database records.
  - `python run.py test`: Runs automated pytest test suite (`test_orchestration.py`).

#### `Backend/schema.sql`
- **Location**: [schema.sql](file:///e:/Accounting/Backend/schema.sql)
- **Role**: Production PostgreSQL DDL schema with SQLite local development compatibility.
- **Key Constraints**:
  - `CHECK (amount_minor > 0)` monetary sanity check.
  - `uq_payment_attempts_provider_idempotency` composite unique constraint.
  - `uq_webhook_events_provider_event` composite unique constraint.
  - Cascading deletes on foreign keys (`ON DELETE CASCADE`).

#### `Backend/services/payment_orchestrator.py`
- **Location**: [payment_orchestrator.py](file:///e:/Accounting/Backend/services/payment_orchestrator.py)
- **Role**: Central payment business logic and state machine engine.
- **Key Symbols**:
  - `VALID_TRANSITIONS`: Dictionary defining permitted state changes, preventing terminal state overwrites.
  - `create_order()`: Authoritative pricing calculation from `product_data.json` or `configuration.json`.
  - `initiate_payment_attempt()`: Validates order state, checks idempotency key, creates record, and invokes payment provider.
  - `process_webhook()`: Performs HMAC verification, deduplication, state transitions, and creates outbox records.
  - `process_fulfillment_outbox()`: Processes pending outbox items and records audit entries in `configuration.json`.
  - `reconcile_payment_attempt()` / `reconcile_all_unresolved()`: Queries gateway for settlement status and commits updates, recording `paid_at` timestamps for both `succeeded` and `duplicate_paid_review` transitions.

#### `Backend/providers/base.py` & `Backend/providers/paymongo.py`
- **Location**: [base.py](file:///e:/Accounting/Backend/providers/base.py) & [paymongo.py](file:///e:/Accounting/Backend/providers/paymongo.py)
- **Role**: Payment gateway abstraction and PayMongo adapter implementation.
- **Key Symbols**:
  - `PaymentProvider`: Abstract base class with `create_checkout_session()`, `get_checkout_session()`, and `verify_webhook_signature()`.
  - `PayMongoProvider`:
    - Reads `PAYMONGO_SECRET_KEY` and `PAYMONGO_WEBHOOK_SECRET`.
    - If keys are missing, operates in Local Sandbox Simulator mode.
    - In live mode, communicates with `https://api.paymongo.com/v1/checkout_sessions` via `httpx.AsyncClient`.
    - `verify_webhook_signature()`: Parses `Paymongo-Signature` header (`t=...,te=...,li=...`), computes HMAC-SHA256 of `timestamp + "." + raw_body`, and compares in constant time.
    - `sign_simulated_payload()`: Generates valid test signatures for automated testing and sandbox runner.

#### `Backend/data/*.json`
- **Location**: `Backend/data/`
- **Files**:
  - `configuration.json`: General company settings, department budgets, user rosters, and fulfillment audit trail.
  - `dashboard_data.json`: 12-month cost time-series and department expense distributions.
  - `department_detail_data.json`: Slug-indexed category cost breakdowns and monthly curves.
  - `product_data.json`: Product line catalog with authoritative unit costs and volumes.
  - `variance_data.json`: Category budget vs. actual cost comparisons and root causes.

#### `Backend/test_orchestration.py`
- **Location**: [test_orchestration.py](file:///e:/Accounting/Backend/test_orchestration.py)
- **Role**: Pytest test suite containing 15 automated verification tests.
- **Key Tests**:
  - `test_legacy_dashboard()`: Validates `/api/dashboard` schema.
  - `test_legacy_departments_no_404()`: Validates all department routes.
  - `test_legacy_products_and_variance()`: Validates product and variance endpoints.
  - `test_legacy_configuration_get_and_put()`: Validates config read and persistence.
  - `test_order_creation_server_side_pricing()`: Verifies authoritative pricing calculation.
  - `test_payment_attempt_and_idempotency()`: Validates idempotency key handling.
  - `test_webhook_hmac_verification_and_state_transitions()`: Verifies forged signature rejection (401), valid webhook transition, deduplication, and late failure rejection.
  - `test_reconciliation_and_outbox()`: Verifies manual reconciliation and outbox listing.
  - `test_get_order_detail_includes_latest_attempt()`: Validates attempt embedding.
  - `test_idempotency_conflict_across_different_orders()`: Rejects reusing idempotency keys across different orders.
  - `test_payment_attempt_on_cancelled_order_rejected()`: Prevents charging cancelled orders.
  - `test_department_invoice_pricing_lookup()`: Tests department budget calculations.
  - `test_terminal_state_immutability_expired_and_failed()`: Validates state machine terminal immutability.
  - `test_simulate_webhook_with_order_id_fallback()`: Tests sandbox simulator fallback resolution.
  - `test_legacy_configuration_users_persistence()`: Verifies user roster modifications.

---

### 7.2 Frontend Source Files

#### `Frontend/app/layout.tsx` & `Frontend/app/globals.css`
- **Location**: [layout.tsx](file:///e:/Accounting/Frontend/app/layout.tsx) & [globals.css](file:///e:/Accounting/Frontend/app/globals.css)
- **Role**: Root application scaffolding, Geist Google font loading, and OKLCH color token definitions.

#### `Frontend/app/page.tsx`
- **Location**: [page.tsx](file:///e:/Accounting/Frontend/app/page.tsx)
- **Role**: Root application layout and tab navigation router.
- **Key Features**: Dynamic loading (`next/dynamic`) of heavy views (`PaymentsHub`, `DepartmentDetail`, `ProductLineAnalysis`, `VarianceAnalysis`, `Configuration`) to ensure sub-second cold starts.

#### `Frontend/app/api/[...path]/route.ts`
- **Location**: [route.ts](file:///e:/Accounting/Frontend/app/api/%5B...path%5D/route.ts)
- **Role**: Next.js App Router catch-all reverse proxy handler.
- **Key Features**: Forwards client requests (`GET`, `POST`, `PUT`, `DELETE`, `PATCH`) to FastAPI backend at `http://localhost:5000/api/...`, stripping conflicting host headers to prevent FastAPI routing errors.

#### `Frontend/components/sidebar.tsx`
- **Location**: [sidebar.tsx](file:///e:/Accounting/Frontend/components/sidebar.tsx)
- **Role**: Main accessible navigation sidebar.
- **Key Features**: WAI-ARIA tab semantics, keyboard navigation support, dynamic active state styling with brand accent indicator.

#### `Frontend/components/dashboard.tsx`
- **Location**: [dashboard.tsx](file:///e:/Accounting/Frontend/components/dashboard.tsx)
- **Role**: High-level manufacturing cost overview dashboard.
- **Key Features**:
  - Recharts Area/Bar/Pie visualizations for monthly cost trends and department allocations.
  - Department metric summary cards with click-to-drill-down callbacks.
  - Formatted currency in Philippine Peso (`₱`).

#### `Frontend/components/department-detail.tsx`
- **Location**: [department-detail.tsx](file:///e:/Accounting/Frontend/components/department-detail.tsx)
- **Role**: Single department granular cost analysis view.
- **Key Features**: Category cost breakdown (Labor, Materials, Overhead, Utilities), monthly spending trends vs. budget, variance status indicators, and back-navigation.

#### `Frontend/components/product-line-analysis.tsx`
- **Location**: [product-line-analysis.tsx](file:///e:/Accounting/Frontend/components/product-line-analysis.tsx)
- **Role**: Product profitability and unit cost analyzer.
- **Key Features**: Margin analysis, volume vs. cost scatter distribution, revenue/profit calculations, and status badges (`growing`, `stable`, `declining`).

#### `Frontend/components/variance-analysis.tsx`
- **Location**: [variance-analysis.tsx](file:///e:/Accounting/Frontend/components/variance-analysis.tsx)
- **Role**: Budget vs. Actual cost variance monitor.
- **Key Features**:
  - Adheres strictly to $\text{Variance} = \text{Budget} - \text{Actual}$.
  - Favorable variances in neutral text; unfavorable overruns in red accent.
  - Export to CSV with UTF-8 BOM (`\uFEFF`) to prevent Excel Mojibake.

#### `Frontend/components/configuration.tsx`
- **Location**: [configuration.tsx](file:///e:/Accounting/Frontend/components/configuration.tsx)
- **Role**: Master settings and system administration hub.
- **Key Features**:
  - General Settings: Company name, fiscal year, reporting currency, decimal precision.
  - Department Management: Modal for adding, editing, and deleting cost centers and budgets.
  - User Access: Role-based user roster management.
  - Audit Logs: Displays real-time gateway and administrative activity records.

#### `Frontend/components/payments-hub.tsx`
- **Location**: [payments-hub.tsx](file:///e:/Accounting/Frontend/components/payments-hub.tsx)
- **Role**: Live payment operations console.
- **Key Features**:
  - Orders table with search and status filtering (`all`, `pending`, `paid`, `cancelled`).
  - "New Order / Invoice" modal with automatic price previews.
  - Webhooks inspection tab showing received gateway events and raw JSON payloads.
  - Outbox monitor tab showing asynchronous fulfillment task statuses.
  - Batch reconciliation trigger button (`reconcileAllPayments`).

#### `Frontend/components/checkout-button.tsx`
- **Location**: [checkout-button.tsx](file:///e:/Accounting/Frontend/components/checkout-button.tsx)
- **Role**: Reusable checkout trigger component.
- **Key Features**:
  - Generates client-side idempotency keys.
  - Calls backend to initiate PayMongo session.
  - Validates gateway origin against approved whitelist (`isApprovedGatewayOrigin`).
  - Redirects browser to PayMongo hosted checkout URL or local sandbox.

#### `Frontend/app/checkout/page.tsx`
- **Location**: [page.tsx](file:///e:/Accounting/Frontend/app/checkout/page.tsx)
- **Role**: Order checkout review and channel selection page.
- **Key Features**: Displays order line items, allows selecting preferred channel (QR Ph, GCash, Maya, Card), and handles cancellation banners.

#### `Frontend/app/payment/sandbox/[attemptId]/page.tsx`
- **Location**: [page.tsx](file:///e:/Accounting/Frontend/app/payment/sandbox/%5BattemptId%5D/page.tsx)
- **Role**: Interactive local sandbox simulator runner.
- **Key Features**:
  - Simulates payment approval/failure for all supported channels without live credentials.
  - Supports testing webhook arrival order ("before redirect" vs. "after redirect").
  - Dispatches HMAC-signed webhook simulations directly to the backend.

#### `Frontend/app/payment/return/page.tsx`
- **Location**: [page.tsx](file:///e:/Accounting/Frontend/app/payment/return/page.tsx)
- **Role**: Post-gateway return redirect landing page.
- **Key Features**: Safely resolves the payment attempt ID from query parameters without mutating order state, immediately forwarding the user to the verification result page.

#### `Frontend/app/payment/result/[paymentId]/page.tsx`
- **Location**: [page.tsx](file:///e:/Accounting/Frontend/app/payment/result/%5BpaymentId%5D/page.tsx)
- **Role**: Payment settlement verification and receipt display page.
- **Key Features**:
  - Active polling loop (up to 30 attempts, 60s) checking payment attempt status.
  - Displays printable transaction receipt when `succeeded`.
  - Manual "Reconcile Status Now" action for ambiguous or delayed transactions.

#### `Frontend/lib/payment-api.ts`
- **Location**: [payment-api.ts](file:///e:/Accounting/Frontend/lib/payment-api.ts)
- **Role**: Typed payment API client and security validation utilities.
- **Key Symbols**:
  - Types: `Order`, `PaymentAttempt`, `WebhookEventRecord`, `FulfillmentOutboxRecord`.
  - `isApprovedGatewayOrigin(url)`: Origin security validator.
  - `formatPhpFromMinor(amountMinor)`: Centavo-to-PHP string formatter (`₱1,234.56`).
  - API Methods: `fetchOrders()`, `fetchOrder()`, `createOrder()`, `createPaymentAttempt()`, `pollPaymentStatus()`, `reconcilePayment()`, `reconcileAllPayments()`, `fetchWebhookEvents()`, `fetchFulfillmentOutbox()`, `simulateWebhook()`.

#### `Frontend/lib/cost-allocation.ts`
- **Location**: [cost-allocation.ts](file:///e:/Accounting/Frontend/lib/cost-allocation.ts)
- **Role**: Mathematical cost allocation and variance calculation engine.
- **Key Methods**:
  - `directCostAllocation()`: Direct cost proportional distribution.
  - `volumeBasedAllocation()`: Production volume proportional distribution.
  - `machineHoursAllocation()`: Equipment hour proportional distribution.
  - `activityBasedAllocation()`: Activity driver and weight calculation.
  - `calculateVariance()`: Budget vs. actual variance computation strictly enforcing $\text{Variance} = \text{Allocated (Budget)} - \text{Actual}$ (with $> 0$ as favorable and $< 0$ as unfavorable).
  - `calculateProductCosts()`: Cost per unit, gross margin, and profit calculation.

#### `Frontend/lib/design-system.ts` & `Frontend/lib/utils.ts`
- **Location**: [design-system.ts](file:///e:/Accounting/Frontend/lib/design-system.ts) & [utils.ts](file:///e:/Accounting/Frontend/lib/utils.ts)
- **Role**: Design token definitions and Tailwind `cn()` utility combiner.

#### `Frontend/hooks/use-mobile.ts` & `Frontend/hooks/use-toast.ts`
- **Location**: [use-mobile.ts](file:///e:/Accounting/Frontend/hooks/use-mobile.ts) & [use-toast.ts](file:///e:/Accounting/Frontend/hooks/use-toast.ts)
- **Role**: Responsive viewport media query detection and toast notification management.

#### `Frontend/components/theme-provider.tsx` & `Frontend/next.config.mjs`
- **Location**: [theme-provider.tsx](file:///e:/Accounting/Frontend/components/theme-provider.tsx) & [next.config.mjs](file:///e:/Accounting/Frontend/next.config.mjs)
- **Role**: Next.js theme management and bundler configuration.

---

### 7.3 Automation & Governance

#### `scripts/git_workflow.py`
- **Location**: [git_workflow.py](file:///e:/Accounting/scripts/git_workflow.py)
- **Role**: Repository Git automation script.
- **Key Features**: Enforces Conventional Commits 1.0.0, functional scope validation (`repo`, `backend`, `frontend`, `test`, `docs`, `agents`, `versioning`), and Semantic Versioning 2.0.0 release tagging (`vX.Y.Z`).

#### Architectural Rulebooks (`.agents/rules/*`)
- `accounting-invariants-and-csv-encoding.md`: Mandates UTF-8 BOM (`\uFEFF`) for CSV downloads, variance sign convention ($\text{Budget} - \text{Actual}$), raw numeric cells in spreadsheet exports, and Next.js `-H localhost` binding.
- `payment-orchestration-and-legacy-guardrails.md`: Enforces backend-orchestrated pricing, terminal state monotonicity, timeout handling, and legacy API route preservation.
- `git-workflow-and-commit-conventions.md`: Strict rules for Git staging and commit messages.



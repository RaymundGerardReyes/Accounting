# Backend Core Services Guide

This guide details the core backend runtime, API server, database abstraction, data models, and DDL schema for the Acme Cost Accounting & Payment Orchestration platform.

---

## 1. `Backend/app.py`
- **Path**: [`Backend/app.py`](file:///e:/Accounting/Backend/app.py)
- **Framework**: FastAPI 2.0.0, ASGI, Pydantic v2, Uvicorn
- **Primary Role**: Entrypoint API application serving legacy accounting endpoints, payment orchestration endpoints, and webhook receivers.

### Core Architecture & Invariants
1. **Lifespan Context Manager (`lifespan`)**:
   - Executes during FastAPI startup.
   - Invokes `init_db()` from [`Backend/models.py`](file:///e:/Accounting/Backend/models.py) to guarantee tables and default seed records exist before accepting traffic.
2. **CORS Middleware**:
   - Configured with `allow_origins=["http://localhost:3000", "http://127.0.0.1:3000", "*"]` to permit Next.js App Router reverse-proxy requests and browser clients.
3. **Legacy Flat-File Storage Handlers**:
   - `read_json_file(filename)`: Reads JSON data from `Backend/data/`. Returns `None` if the file is missing.
   - `write_json_file(filename, data)`: Atomically dumps modified configuration or department data with 4-space indentation.

### Core Route Handlers
- **Legacy Cost Accounting Endpoints**:
  - `GET /api/dashboard`: Returns aggregate cost trends, department metrics, and variance summaries.
  - `GET /api/department/{department_name}`: Normalizes slugs (`department_name.lower().replace(" ", "-")`) and returns granular cost breakdown and monthly spending.
  - `GET /api/products`: Returns product catalog, volume metrics, unit costs, and profit margins.
  - `GET /api/variance`: Returns category-level budget vs. actual cost comparisons.
  - `GET /api/configuration`: Retrieves general company configuration, cost centers, and user rosters.
  - `PUT /api/configuration`: Updates top-level settings (company name, fiscal year, reporting currency).
  - `POST /api/configuration/departments`: Overwrites department cost center definitions and budgets.
  - `POST /api/configuration/users`: Overwrites user rosters and administrative permissions.
- **Payment Orchestration Endpoints (`/api/v1/*`)**:
  - `GET /api/v1/orders`: Lists customer orders and department invoices, dynamically attaching `latest_attempt`. Supports `?status=` filter.
  - `POST /api/v1/orders`: Creates an order using server-authoritative pricing (Pydantic model `CreateOrderRequest`).
  - `GET /api/v1/orders/{order_id}`: Retrieves an order with complete payment attempt history.
  - `POST /api/v1/orders/{order_id}/payment-attempts`: Creates an idempotent payment attempt (`CreatePaymentAttemptRequest` or `Idempotency-Key` header).
  - `GET /api/v1/payments/{payment_id}`: Returns the current payment attempt status and associated parent order.
  - `POST /api/v1/webhooks/paymongo`: Webhook ingress verifying the `Paymongo-Signature` header and triggering state transitions.
  - `POST /api/v1/admin/payments/{payment_id}/reconcile`: Queries PayMongo for external settlement status and resolves local attempt state.
  - `POST /api/v1/admin/payments/reconcile-all`: Bulk reconciles all non-terminal attempts (`pending`, `requires_action`, `processing`, `unknown`).
  - `GET /api/v1/admin/webhook-events`: Lists recorded webhook events for audit inspection.
  - `GET /api/v1/admin/fulfillment-outbox`: Lists post-payment fulfillment tasks.
  - `POST /api/v1/sandbox/simulate-webhook`: Generates valid HMAC-SHA256 signed PayMongo webhooks for local sandbox testing.

---

## 2. `Backend/database.py`
- **Path**: [`Backend/database.py`](file:///e:/Accounting/Backend/database.py)
- **Engine**: SQLAlchemy 2.0 ORM
- **Primary Role**: Database engine configuration, connection pooling, and request session lifecycle management.

### Key Features
- **Dynamic Database URL**:
  - Reads `DATABASE_URL` from environment variables.
  - Defaults to SQLite (`sqlite:///accounting_payments.db`) when `DATABASE_URL` is unset.
- **SQLite Concurrency Settings**:
  - Sets `connect_args={"check_same_thread": False}` to allow FastAPI's multi-threaded worker pool to share SQLite connections safely.
- **Connection Health**:
  - Configures `pool_pre_ping=True` to eliminate stale or dropped database connections.
- **Session Dependency (`get_db`)**:
  - Standard FastAPI generator dependency yielding a scoped `SessionLocal()` instance and ensuring `db.close()` executes cleanly in a `finally` block.

---

## 3. `Backend/models.py`
- **Path**: [`Backend/models.py`](file:///e:/Accounting/Backend/models.py)
- **Primary Role**: SQLAlchemy declarative models representing the payment orchestration relational schema.

### Models & Schema Design
1. **`Order`**:
   - Represents a production order or department settlement invoice.
   - Fields: `id` (UUID), `user_id`, `order_number` (unique, indexed), `title`, `category` (`product_order` | `department_invoice`), `reference_code`, `amount_minor` (centavos, `> 0`), `currency` (`PHP`), `status` (`pending` | `paid` | `cancelled`), `metadata_json`, timestamps.
   - Relationships: One-to-many with `PaymentAttempt` and `FulfillmentOutbox` with cascading deletion.
2. **`PaymentAttempt`**:
   - Tracks individual gateway checkout attempts.
   - Fields: `id`, `order_id` (foreign key), `provider` (`paymongo`), `payment_method`, `provider_payment_id` (`cs_...`), `amount_minor`, `currency`, `status`, `idempotency_key`, `checkout_url`, `failure_code`, `failure_message`, `expires_at`, `paid_at`, timestamps.
   - Invariants: Unique constraint on `(provider, idempotency_key)`.
   - Allowed Statuses: `pending`, `requires_action`, `processing`, `succeeded`, `failed`, `cancelled`, `expired`, `unknown`, `duplicate_paid_review`.
3. **`WebhookEvent`**:
   - Stores raw incoming webhook payloads for idempotency and audit logs.
   - Fields: `id`, `provider`, `provider_event_id` (unique indexed constraint), `event_type`, `processing_status` (`received` | `processed` | `ignored` | `failed`), `received_at`, `processed_at`, `payload`.
4. **`FulfillmentOutbox`**:
   - Implements the Transactional Outbox pattern.
   - Fields: `id`, `order_id`, `payment_attempt_id`, `event_type` (`order.paid`), `status` (`pending` | `completed` | `failed`), timestamps.
5. **Database Initialization (`init_db`)**:
   - Creates all tables via `Base.metadata.create_all(bind=engine)`.
   - Populates initial production seed orders (`ORD-2025-001`, `ORD-2025-002`) and department settlement invoices (`INV-MFG-2025-01`, `INV-ASY-2025-01`, `INV-QA-2025-01`) if empty.

---

## 4. `Backend/run.py` & `Backend/run`
- **Path**: [`Backend/run.py`](file:///e:/Accounting/Backend/run.py)
- **Primary Role**: CLI developer command runner.

### Commands Supported
- `python run.py server`: Starts the FastAPI uvicorn server on port 5000 with auto-reload.
- `python run.py model`: Initializes the database tables, applies seed data, and prints row count diagnostics.
- `python run.py test`: Invokes pytest against `test_orchestration.py` with verbose flag.
- `python run.py --help`: Displays the CLI manual.

---

## 5. `Backend/schema.sql`
- **Path**: [`Backend/schema.sql`](file:///e:/Accounting/Backend/schema.sql)
- **Primary Role**: Production PostgreSQL DDL definition (also compatible with SQLite).

### Key Features
- **Integrity Constraints**:
  - `CHECK (amount_minor > 0)` prevents zero or negative monetary transactions.
  - `CONSTRAINT uq_payment_attempts_provider_idempotency UNIQUE (provider, idempotency_key)` guarantees idempotency at the database engine level.
  - `CONSTRAINT uq_webhook_events_provider_event UNIQUE (provider, provider_event_id)` enforces webhook deduplication.
- **Indexes**:
  - B-tree indexes on `(user_id, status)`, `order_number`, `order_id`, `provider_payment_id`, `status`, and `provider_event_id` for fast OLTP query execution.

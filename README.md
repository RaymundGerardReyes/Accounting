# Accounting & Cost Analysis Automation Platform

A modern manufacturing cost analysis and live payment orchestration system built with **FastAPI** (Python) on the backend and **Next.js 15 / React 19** (TypeScript) on the frontend, integrated with **PayMongo Hosted Checkout Sessions** and signed webhook lifecycle management.

---

## 🌟 Key Features

- **Manufacturing Cost Analysis**: Interactive dashboards for department cost allocation, volume-based/activity-based distribution, unit cost trends, and variance reporting in Philippine Peso (`₱`).
- **Live Payment Gateway Orchestration**:
  - Secure server-side order calculation and payment attempt initialization.
  - PayMongo Hosted Checkout Sessions supporting **QR Ph**, **GCash**, **Maya**, **Online Banking (BPI/UnionBank)**, and **Credit/Debit Cards**.
  - Asynchronous webhook processing with **HMAC-SHA256 signature verification** (`Paymongo-Signature: t=...,te=...,li=...`).
  - Durable fulfillment outbox worker and automated reconciliation for uncertain transactions.
  - Built-in **Local Sandbox Simulator** with an interactive testing runner for out-of-the-box local development without requiring live credentials.
- **CPA-Audited Finance Operations**:
  - Configurable financial policy engine with versioning, PHP 15,000 monthly limit, and author != approver enforcement.
  - Cash advance lifecycle with 6-person separation of duties (Maker → Verifier → Bank Approver → Remittance Evidence Verifier).
  - Decoupled monthly expense reporting with oldest-first advance settlement (disallowed amounts stay as employee receivables).
  - Purchase-to-Pay (P2P) with independent vendor verification, purchase requests, accounts payable with BIR 12% VAT / 1% & 2% EWT calculation, and duplicate invoice protection.
  - Append-only cryptographic SHA-256 hash-chained audit trail (`audit_events`) and formula-safe UTF-8 BOM CSV exports.
- **Accessibility & Design System**: WAI-ARIA tab navigation, form label associations, screen-reader landmarks, and contrast-compliant UI powered by Tailwind CSS v4 and Radix UI.
- **Automated Git Workflow**: Conventional Commits 1.0.0 enforcement, scoped staging, and Semantic Versioning 2.0.0 release tagging automation.

---

## 🏗️ Architecture

```
Accounting/
├── .agents/                 # Workspace rules and skills for AI pair programming
│   ├── rules/               # Invariants for payment safety, finance CPA controls, and Git conventions
│   └── skills/              # On-demand runbooks (PayMongo orchestration, Git automation)
├── Backend/                 # Python 3.10+ FastAPI backend
│   ├── data/                # Master configuration and seed data
│   ├── providers/           # Payment gateway adapters (PayMongo v1 client + local simulator)
│   ├── services/            # Payment orchestrator, policy engine, finance workflows, reporting
│   ├── app.py               # FastAPI application with OpenAPI docs (/docs) and routes
│   ├── database.py          # SQLAlchemy 2.0 engine (PostgreSQL + SQLite fallback)
│   ├── models.py            # Orders, PaymentAttempts, WebhookEvents, FulfillmentOutbox
│   ├── finance_models.py    # Declarative models for Advances, Policies, AP, Vendors, Audit
│   ├── finance_routes.py    # FastAPI router for /api/v1/finance/*
│   ├── schema.sql           # Production PostgreSQL DDL schema
│   ├── requirements.txt     # Python dependencies
│   ├── test_orchestration.py# Automated pytest suite for PayMongo (15 passing tests)
│   └── test_finance_operations.py # Automated pytest suite for Finance Operations (9 passing tests)
├── Frontend/                # Next.js 15 (App Router) + React 19 + TypeScript
│   ├── app/                 # Pages: /checkout, /payment/return, /payment/result, /payment/sandbox
│   ├── components/          # Dashboard, Finance Operations Hub, Payments Hub, Cost Analysis
│   ├── hooks/ & lib/        # Finance API client, payment API client, cost allocation engine
│   └── next.config.mjs      # Next.js config with /api/* proxy rewrite to port 5000
├── docs/                    # Technical documentation & Git workflow guide
└── scripts/                 # CLI automation helpers (git_workflow.py)
```

---

## 🚀 Quick Start

### 1. Backend Setup
```powershell
cd Backend
# Create virtual environment (optional if already created)
python -m venv venv
.\venv\Scripts\Activate.ps1

# Install dependencies
pip install -r requirements.txt

# Run automated tests (all 24 passing)
python -m pytest test_finance_operations.py test_orchestration.py -v

# Start the server on port 5000
python -m uvicorn app:app --host 0.0.0.0 --port 5000 --reload
```
Open **[http://localhost:5000/docs](http://localhost:5000/docs)** for interactive Swagger UI documentation.

### 2. Frontend Setup
```powershell
cd Frontend
# Install dependencies
npm install

# Start Next.js development server
npm run dev
```
Open **[http://localhost:3000](http://localhost:3000)** to view the Cost Analysis, Finance Operations, and Payments Hub.

---

## 💳 PayMongo Environment Configuration

To connect live or test PayMongo accounts, create `Backend/.env`:
```ini
PAYMONGO_SECRET_KEY=sk_test_... # or sk_live_...
PAYMONGO_WEBHOOK_SECRET=whsk_...
# Optional production PostgreSQL connection:
# DATABASE_URL=postgresql+psycopg2://user:password@localhost:5432/accounting_db
```
*Note: If omitted, the backend automatically uses the built-in Local Sandbox Simulator.*

---

## 📜 Documentation & Workflow Guides

- **Architecture & Source Code Guide**: See [docs/codebase-logic-and-source-guide.md](docs/codebase-logic-and-source-guide.md) for the complete breakdown of frontend and backend logic workflows, core functions, and state machines.
- **Dedicated Source Code Guides**: Detailed per-subsystem guides covering every source file in the repository:
  - [Backend Finance Operations Guide](docs/source-guides/backend-finance-operations.md) (`finance_models.py`, `finance_routes.py`, `services/policy_engine.py`, `services/finance_workflows.py`, `services/finance_reporting.py`, `services/finance_common.py`, `test_finance_operations.py`)
  - [Frontend Finance Operations Guide](docs/source-guides/frontend-finance-operations.md) (`finance-operations-hub.tsx`, `finance-api.ts`)
  - [Backend Core Services Guide](docs/source-guides/backend-core.md) (`app.py`, `database.py`, `models.py`, `run.py`, `schema.sql`)
  - [Backend Payment Services Guide](docs/source-guides/backend-payment-services.md) (`payment_orchestrator.py`, `providers/base.py`, `providers/paymongo.py`)
  - [Backend Master Data & Flat Files Guide](docs/source-guides/backend-data-and-seed.md) (`configuration.json`, `dashboard_data.json`, `department_detail_data.json`, `product_data.json`, `variance_data.json`)
  - [Frontend Architecture & Routes Guide](docs/source-guides/frontend-architecture-and-pages.md) (`app/layout.tsx`, `app/globals.css`, `app/page.tsx`, `app/api/[...path]/route.ts`, checkout, return, result, sandbox)
  - [Frontend Components & UI Hub Guide](docs/source-guides/frontend-components-hub.md) (`dashboard.tsx`, `department-detail.tsx`, `product-line-analysis.tsx`, `variance-analysis.tsx`, `configuration.tsx`, `payments-hub.tsx`, `finance-operations-hub.tsx`, `checkout-button.tsx`, `sidebar.tsx`, `theme-provider.tsx`, `ui/*`)
  - [Frontend Libraries & Custom Hooks Guide](docs/source-guides/frontend-libraries-and-hooks.md) (`cost-allocation.ts`, `finance-api.ts`, `payment-api.ts`, `design-system.ts`, `utils.ts`, `use-mobile.ts`, `use-toast.ts`)
  - [Testing, Git Automation & Governance Guide](docs/source-guides/testing-and-automation.md) (`test_orchestration.py`, `test_finance_operations.py`, `git_workflow.py`, `.agents/rules/*`, `.agents/skills/*`)
- **Git Contribution & SemVer Workflow**: This repository strictly follows [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/) and [Semantic Versioning 2.0.0](https://semver.org/). See [docs/git-commit-workflow.md](docs/git-commit-workflow.md) for full details and staging recipes.

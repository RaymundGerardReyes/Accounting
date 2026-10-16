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
- **Accessibility & Design System**: WAI-ARIA tab navigation, form label associations, screen-reader landmarks, and contrast-compliant UI powered by Tailwind CSS v4 and Radix UI.
- **Automated Git Workflow**: Conventional Commits 1.0.0 enforcement, scoped staging, and Semantic Versioning 2.0.0 release tagging automation.

---

## 🏗️ Architecture

```
Accounting/
├── .agents/                 # Workspace rules and skills for AI pair programming
│   ├── rules/               # Invariants for payment safety, legacy compatibility, and Git conventions
│   └── skills/              # On-demand runbooks (PayMongo orchestration, Git automation)
├── Backend/                 # Python 3.10+ FastAPI backend
│   ├── data/                # Master configuration and seed data
│   ├── providers/           # Payment gateway adapters (PayMongo v1 client + local simulator)
│   ├── services/            # Payment orchestration, idempotency, and fulfillment outbox
│   ├── app.py               # FastAPI application with OpenAPI docs (/docs) and legacy routes
│   ├── database.py          # SQLAlchemy 2.0 engine (PostgreSQL + SQLite fallback)
│   ├── models.py            # Orders, PaymentAttempts, WebhookEvents, FulfillmentOutbox
│   ├── schema.sql           # Production PostgreSQL DDL schema
│   ├── requirements.txt     # Python dependencies
│   └── test_orchestration.py# Automated pytest suite (14 passing tests)
├── Frontend/                # Next.js 15 (App Router) + React 19 + TypeScript
│   ├── app/                 # Pages: /checkout, /payment/return, /payment/result, /payment/sandbox
│   ├── components/          # Dashboard, Payments Hub, Cost Analysis, and UI components
│   ├── hooks/ & lib/        # Payment API client, cost allocation engine, utilities
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

# Run automated tests
python -m pytest test_orchestration.py -v

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
Open **[http://localhost:3000](http://localhost:3000)** to view the Cost Analysis and Payments Hub.

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

## 📜 Git Contribution & SemVer Workflow

This repository strictly follows [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/) and [Semantic Versioning 2.0.0](https://semver.org/).
See [docs/git-commit-workflow.md](docs/git-commit-workflow.md) for full details and staging recipes.

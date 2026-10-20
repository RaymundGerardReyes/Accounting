#!/usr/bin/env python3
"""
Backend Command Runner
Convenient CLI for starting the FastAPI server, initializing database models,
and running test suites.

Usage:
    python app.py                  Start FastAPI server on port 5000 (standard)
    python run.py                  Start FastAPI server on port 5000
    python run.py server           Start FastAPI server on port 5000
    python run.py app.py           Start FastAPI server on port 5000
    python run.py model            Initialize DB models & display schema/counts
    python run.py test             Run automated pytest test suite
"""

import sys
import os
from pathlib import Path
from dotenv import load_dotenv

# Load authoritative monorepo environment configuration (E:\Accounting\.env)
_root_env = Path(__file__).resolve().parent.parent / ".env"
if _root_env.exists():
    load_dotenv(_root_env)

def main():
    args = sys.argv[1:]
    cmd = args[0].lower() if args else "server"

    if cmd in ("server", "app", "app.py", "run", "start"):
        import uvicorn
        print("[*] Starting Acme Cost Accounting & PayMongo Orchestrator on http://localhost:5000 ...")
        print("[*] Interactive OpenAPI Documentation available at http://localhost:5000/docs")
        uvicorn.run("app:app", host="0.0.0.0", port=5000, reload=True)

    elif cmd in ("model", "models", "db", "init-db", "init_db"):
        from models import init_db, Order, PaymentAttempt, WebhookEvent, FulfillmentOutbox
        from database import SessionLocal
        print("[*] Initializing SQLAlchemy database schema and seeds...")
        init_db()
        db = SessionLocal()
        try:
            orders_count = db.query(Order).count()
            attempts_count = db.query(PaymentAttempt).count()
            webhooks_count = db.query(WebhookEvent).count()
            outbox_count = db.query(FulfillmentOutbox).count()
            print("[OK] Database ready!")
            print(f"   - Orders: {orders_count}")
            print(f"   - Payment Attempts: {attempts_count}")
            print(f"   - Webhook Events: {webhooks_count}")
            print(f"   - Outbox Records: {outbox_count}")
        finally:
            db.close()

    elif cmd in ("test", "tests", "pytest"):
        import pytest
        print("[*] Running backend test suite...")
        sys.exit(pytest.main(["test_orchestration.py", "-v"]))

    elif cmd in ("--help", "-h", "help"):
        print(__doc__)

    else:
        print(f"Unknown command: '{cmd}'")
        print(__doc__)
        sys.exit(1)

if __name__ == "__main__":
    main()


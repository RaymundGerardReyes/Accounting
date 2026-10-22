import os
from pathlib import Path
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker, declarative_base
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent

# Ensure authoritative monorepo environment variables are loaded (E:\Accounting\.env)
_root_env = BASE_DIR.parent / ".env"
if _root_env.exists():
    load_dotenv(_root_env)

# Check for DATABASE_URL environment variable, falling back to local SQLite
DATABASE_URL = os.getenv("DATABASE_URL")
if DATABASE_URL and DATABASE_URL.startswith("postgres://"):
    # Heroku PostgreSQL sets postgres:// which SQLAlchemy 2.0 requires as postgresql://
    DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)

if not DATABASE_URL:
    db_path = BASE_DIR / "accounting_payments.db"
    DATABASE_URL = f"sqlite:///{db_path}"

# SQLite and PostgreSQL specific connect args
connect_args = {}
if DATABASE_URL.startswith("sqlite"):
    connect_args = {"check_same_thread": False}
elif DATABASE_URL.startswith("postgresql"):
    # Enforce sslmode=require for Heroku PostgreSQL if not already present
    if "sslmode=" not in DATABASE_URL and not any(h in DATABASE_URL for h in ("localhost", "127.0.0.1")):
        connect_args = {"sslmode": "require"}

try:
    engine = create_engine(
        DATABASE_URL,
        connect_args=connect_args,
        pool_pre_ping=True
    )
    # Validate connection immediately
    with engine.connect() as conn:
        pass
except Exception as e:
    print(f"[WARNING] Primary database connection failed ({e}). Falling back to local SQLite.", flush=True)
    db_path = BASE_DIR / "accounting_payments.db"
    DATABASE_URL = f"sqlite:///{db_path}"
    connect_args = {"check_same_thread": False}
    engine = create_engine(
        DATABASE_URL,
        connect_args=connect_args,
        pool_pre_ping=True
    )

SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
Base = declarative_base()

def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()


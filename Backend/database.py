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
if not DATABASE_URL:
    db_path = BASE_DIR / "accounting_payments.db"
    DATABASE_URL = f"sqlite:///{db_path}"

# SQLite specific connect args
connect_args = {}
if DATABASE_URL.startswith("sqlite"):
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


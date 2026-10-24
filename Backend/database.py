import os
import re
import time
from pathlib import Path
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker, declarative_base
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent

# Ensure authoritative monorepo environment variables are loaded (E:\Accounting\.env)
_root_env = BASE_DIR.parent / ".env"
if _root_env.exists():
    load_dotenv(_root_env)

# Detect runtime environment
IS_PRODUCTION = bool(os.getenv("DYNO") or os.getenv("HEROKU_APP_NAME") or os.getenv("APP_DOMAIN"))
IS_TESTING = bool(os.getenv("PYTEST_CURRENT_TEST") or os.getenv("TESTING"))

# Check which PostgreSQL driver is available in the Python runtime
has_psycopg3 = False
has_psycopg2 = False
try:
    import psycopg  # noqa: F401
    has_psycopg3 = True
except ImportError:
    pass

try:
    import psycopg2  # noqa: F401
    has_psycopg2 = True
except ImportError:
    pass

# Determine preferred driver (prefer psycopg2 if available, else psycopg3)
preferred_driver = "postgresql+psycopg2://" if has_psycopg2 else ("postgresql+psycopg://" if has_psycopg3 else "postgresql://")

# Check for DATABASE_URL environment variable
DATABASE_URL = os.getenv("DATABASE_URL", "").strip()

# Normalize PostgreSQL driver in DATABASE_URL
if DATABASE_URL.startswith("postgres://"):
    # Heroku PostgreSQL sets postgres:// which SQLAlchemy 2.0 requires as postgresql+psycopg2://
    DATABASE_URL = DATABASE_URL.replace("postgres://", preferred_driver, 1)
elif DATABASE_URL.startswith("postgresql://"):
    # Bare postgresql:// -> convert explicitly to available driver
    DATABASE_URL = DATABASE_URL.replace("postgresql://", preferred_driver, 1)
elif DATABASE_URL.startswith("postgresql+psycopg://") and not has_psycopg3 and has_psycopg2:
    # URL specified psycopg (v3) but only psycopg2 is installed -> seamlessly fallback to psycopg2
    DATABASE_URL = DATABASE_URL.replace("postgresql+psycopg://", "postgresql+psycopg2://", 1)
elif DATABASE_URL.startswith("postgresql+psycopg2://") and not has_psycopg2 and has_psycopg3:
    # URL specified psycopg2 but only psycopg3 is installed -> seamlessly fallback to psycopg3
    DATABASE_URL = DATABASE_URL.replace("postgresql+psycopg2://", "postgresql+psycopg://", 1)

# Automatically rewrite IPv6-only Supabase Direct Host (db.<ref>.supabase.co) to IPv4 Supavisor Pooler
# Heroku dynos and many IPv4 networks fail with "Network is unreachable" against db.<ref>.supabase.co
_supa_direct_match = re.search(r"://([^:@]+)(:[^@]+)?@db\.([a-z0-9]+)\.supabase\.co(?::(\d+))?/", DATABASE_URL)
if _supa_direct_match:
    _db_user = _supa_direct_match.group(1)
    _db_pass_part = _supa_direct_match.group(2) or ""
    _proj_ref = _supa_direct_match.group(3)
    _db_port = _supa_direct_match.group(4) or "5432"
    _pooler_host = os.getenv("SUPABASE_POOLER_HOST", "aws-0-ap-northeast-1.pooler.supabase.com")
    _pooler_user = _db_user if f".{_proj_ref}" in _db_user else f"{_db_user}.{_proj_ref}"
    DATABASE_URL = DATABASE_URL.replace(
        _supa_direct_match.group(0),
        f"://{_pooler_user}{_db_pass_part}@{_pooler_host}:{_db_port}/",
        1
    )
    print(f"[DB] Rewrote IPv6 Supabase direct host (db.{_proj_ref}.supabase.co) to IPv4 Pooler ({_pooler_host}:{_db_port}).", flush=True)

is_postgres = DATABASE_URL.startswith("postgresql")

# In production or when PostgreSQL is explicitly configured, never default to SQLite
if not DATABASE_URL:
    if IS_PRODUCTION:
        raise RuntimeError("FATAL: DATABASE_URL environment variable is required in production. SQLite is strictly disallowed.")
    db_path = BASE_DIR / "accounting_payments.db"
    DATABASE_URL = f"sqlite:///{db_path}"

connect_args = {}
engine_kwargs = {"pool_pre_ping": True}

if DATABASE_URL.startswith("sqlite"):
    connect_args["check_same_thread"] = False
elif is_postgres:
    # Enforce sslmode=require for Cloud PostgreSQL if not already present in URL or query
    if not any(h in DATABASE_URL for h in ("localhost", "127.0.0.1")):
        if "sslmode=" not in DATABASE_URL:
            connect_args["sslmode"] = "require"
        # Prevent local Windows %APPDATA%/postgresql/root.crt from causing 'certificate verify failed' under sslmode=require
        if "sslrootcert=" not in DATABASE_URL and os.getenv("PGSSLMODE", "").lower() not in ("verify-ca", "verify-full"):
            connect_args["sslrootcert"] = "nonexistent"
    engine_kwargs.update({
        "pool_size": 5,
        "max_overflow": 10,
        "pool_recycle": 300,
        "pool_timeout": 30,
    })

engine = None
max_retries = 5 if is_postgres else 1

for attempt in range(1, max_retries + 1):
    try:
        engine = create_engine(
            DATABASE_URL,
            connect_args=connect_args,
            **engine_kwargs
        )
        with engine.connect() as conn:
            pass
        if is_postgres:
            print(f"[DB] Successfully connected to remote PostgreSQL on attempt {attempt}.", flush=True)
        break
    except Exception as e:
        if is_postgres:
            print(f"[DB ERROR] Connection attempt {attempt}/{max_retries} failed ({e}).", flush=True)
            if attempt < max_retries:
                time.sleep(attempt * 1.5)
                continue
            # If all retries failed and in production or PostgreSQL was explicitly configured: FAIL FAST
            if IS_PRODUCTION or not IS_TESTING:
                raise RuntimeError(
                    f"FATAL: Failed to connect to remote PostgreSQL database after {max_retries} attempts: {e}. "
                    "SQLite fallback is disabled to prevent data loss in cloud deployment."
                ) from e

        # Only allow SQLite fallback during local development testing when not in production
        if not IS_PRODUCTION:
            print(f"[DEV WARNING] Primary database connection failed ({e}). Falling back to local SQLite.", flush=True)
            db_path = BASE_DIR / "accounting_payments.db"
            DATABASE_URL = f"sqlite:///{db_path}"
            connect_args = {"check_same_thread": False}
            engine = create_engine(
                DATABASE_URL,
                connect_args=connect_args,
                pool_pre_ping=True
            )
            break

SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
Base = declarative_base()

def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()

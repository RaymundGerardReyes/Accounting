---
description: Architectural and transaction safety guardrails for cloud PostgreSQL, Supabase connection pooling, and SQLAlchemy session hygiene in E:\Accounting
---

# PostgreSQL Resilience & Transaction Hygiene Guardrails

1. **Cloud PostgreSQL & IPv4 Pooler Routing**:
   - **IPv4 Pooler Requirement**: Heroku dynos and standard cloud containers run on an IPv4-only network stack. Supabase direct database hostnames (`db.<project_ref>.supabase.co`) resolve exclusively via IPv6 `AAAA` records and will fail with `Network is unreachable`.
   - **Supavisor Routing**: All remote PostgreSQL connections on Heroku must use the IPv4-compatible Supabase Supavisor Session Pooler (`aws-0-ap-northeast-1.pooler.supabase.com:5432` with username `postgres.<project_ref>`).
   - **Automatic Boot Rewriting**: `Backend/database.py` must maintain an automatic rewriter that translates any legacy `db.<ref>.supabase.co` URL to the IPv4 pooler and enforces `sslmode=require`.

2. **PL/pgSQL String Handling Invariants**:
   - **Prohibition of `chr(0)`**: PostgreSQL `TEXT` and `VARCHAR` data types store null-terminated C-strings and strictly forbid NUL bytes (`\0`). Never evaluate `chr(0)` in PL/pgSQL stored functions (e.g. `replace(input, chr(0), '')`), as it raises `psycopg2.errors.ProgramLimitExceeded: null character not permitted`.
   - **Sanitization Safe Patterns**: Use standard `btrim(input)` and regex replacements for formula/command injection neutralization without `chr(0)`.

3. **SQLAlchemy Session & Transaction Hygiene**:
   - **Mandatory `db.rollback()` on Caught Exceptions**: Whenever raw SQL, stored functions, or tentative queries are executed within a `try: ... except Exception:` block in a SQLAlchemy session, the `except` handler MUST call `db.rollback()`.
   - **Prevention of `InFailedSqlTransaction`**: In PostgreSQL, any statement failure aborts the entire transaction block. Without `db.rollback()`, any subsequent query (such as a fallback ORM read) immediately crashes with `psycopg2.errors.InFailedSqlTransaction: current transaction is aborted, commands ignored until end of transaction block`.
   - **Foreign-Key Flush Ordering**: In initial database seeders (`seed_finance_db()`), enforce explicit `db.flush()` between parent records (`FinancialPolicy`), version child records (`PolicyVersion`), and grandchildren rules (`PolicyRule`) to ensure deterministic topological insertion order in PostgreSQL.


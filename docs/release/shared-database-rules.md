# Shared Database Architectural Rules (Dual-Line Operation)

## 1. Dual-Line Database Reality

During the migration period from Python (Line B) to .NET 10 (Line A), both application backends connect to the **same PostgreSQL 16 database**.
- **Line A (.NET 10)**: Uses DbUp forward-only SQL migrations located in `Backend.DotNet/db/migrations/`.
- **Line B (Python)**: Uses Alembic / raw SQLAlchemy models located in `Backend/models.py`.

```
    ┌───────────────────────────┐         ┌───────────────────────────┐
    │   Line A (.NET 10 Host)   │         │  Line B (Python FastAPI)  │
    └─────────────┬─────────────┘         └─────────────┬─────────────┘
                  │                                     │
                  │   Shared PostgreSQL 16 Production   │
                  └───► ┌───────────────────────────┐ ◄─┘
                        │ - orders                  │
                        │ - payment_attempts        │
                        │ - webhook_events          │
                        │ - fulfillment_outbox      │
                        │ - audit_trail             │
                        │ - 5 Stored Functions      │
                        └───────────────────────────┘
```

Because both lines read and write concurrently to shared state, any uncoordinated destructive schema change in either line will trigger immediate production downtime.

---

## 2. The Four Cardinal Rules of Shared Schema

### Rule 1: Additive-Only Migrations (No Destructive DDL)
Until Line A reaches full `v1.0.0` parity and Line B is formally retired:
- **FORBIDDEN**: `DROP COLUMN`, `DROP TABLE`, `DROP CONSTRAINT`, `DROP FUNCTION`.
- **FORBIDDEN**: Renaming tables or columns in-place (`ALTER TABLE ... RENAME ...`).
- **FORBIDDEN**: Narrowing column data types (e.g. `TEXT` $\rightarrow$ `VARCHAR(50)`).
- **FORBIDDEN**: Adding non-nullable columns without default values (`ALTER TABLE ... ADD COLUMN ... NOT NULL` without `DEFAULT`).

### Rule 2: Two-Phase Column Renames & Expansions
When a column name or structure must evolve:
1. **Phase 1 (Expand)**: Add the new column as `NULLABLE` (or with a deterministic default).
2. **Phase 2 (Dual-Write)**: Both lines write to both old and new columns.
3. **Phase 3 (Backfill)**: Run a background migration backfilling historical data.
4. **Phase 4 (Migrate Readers)**: Switch all queries and API serialization to the new column.
5. **Phase 5 (Contract)**: Drop the old column **ONLY AFTER** Line B is decommissioned and all consumers are confirmed on the new contract.

### Rule 3: Shared Core Tables
The following tables are strictly protected by the dual-line migration gate:
- `orders`
- `payment_attempts`
- `webhook_events`
- `fulfillment_outbox`
- `audit_trail`
- `schema_migrations`

Columns required by Python SQLAlchemy models (`models.py`, `finance_models.py`) must never be altered in a way that breaks ORM attribute mapping.

### Rule 4: Stored Function Backward Compatibility
The 5 PostgreSQL stored functions:
1. `fn_sanitize_input(text)`
2. `fn_next_sequence_number(varchar, date)`
3. `fn_append_audit_event(...)`
4. `fn_authenticate_user(varchar, varchar)`
5. `fn_oauth_state(varchar, varchar, varchar, interval)`

Must maintain parameter signature and return type stability. Any signature extension must provide overloaded versions rather than modifying the existing definition in-place.

---

## 3. Enforcement in CI/CD

All migration PRs are automatically inspected by [`.github/workflows/ci-migration-compat.yml`](file:///e:/Accounting/.github/workflows/ci-migration-compat.yml).
Any DDL script containing forbidden keywords on shared tables immediately fails CI.


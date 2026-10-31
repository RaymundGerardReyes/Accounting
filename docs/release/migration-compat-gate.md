# Migration Compatibility Gate Specification

## 1. Objective

The **Migration Compatibility Gate** guarantees that schema changes introduced on either `migrating-C#` or `legacy-python` do not break the counterpart line sharing the same PostgreSQL database.

---

## 2. Gate Verification Triggers

1. **Pull Requests**: Any PR touching `db/`, `Backend/**/Migrations/**`, or `Backend/models.py`.
2. **Release Pipelines**: Pre-deployment validation step in `cd-release-dotnet.yml` and `cd-release-python.yml`.

---

## 3. Automated Validation Criteria

The migration gate executes an automated AST and regex-based DDL scanner that enforces:

| Check | Failure Condition | Severity |
|---|---|---|
| **Destructive Columns** | Contains `DROP COLUMN` on any shared table | **FATAL (Blocks CI)** |
| **Destructive Tables** | Contains `DROP TABLE` on any shared table | **FATAL (Blocks CI)** |
| **Destructive Renames** | Contains `RENAME COLUMN` or `RENAME TABLE` | **FATAL (Blocks CI)** |
| **Unsafe NOT NULL** | `ADD COLUMN ... NOT NULL` without `DEFAULT` | **FATAL (Blocks CI)** |
| **Type Shrinkage** | `ALTER COLUMN ... TYPE` with narrower width | **FATAL (Blocks CI)** |
| **Stored Function Replacement** | `DROP FUNCTION` or breaking signature change | **FATAL (Blocks CI)** |

---

## 4. Dual-Line Compatibility Matrix

| Shared Entity | Python Line Read / Write | .NET Line Read / Write | Invariant |
|---|---|---|---|
| `orders` | SQLAlchemy `Order` | ADO.NET / Dapper `Order` | Fields `id`, `amount`, `status`, `created_at` immutable |
| `payment_attempts` | SQLAlchemy `PaymentAttempt` | ADO.NET / Dapper `PaymentAttempt` | State machine transitions strictly monotonic |
| `webhook_events` | Raw JSON payload storage | System.Text.Json payload storage | Schema agnostic JSONB |
| `fulfillment_outbox`| Polled by Celery / Background | Polled by `OutboxWorker` | `processed_at`, `retry_count` lockstep |
| `audit_trail` | Written via `fn_append_audit_event` | Written via `fn_append_audit_event` | Hash chain tamper-evident |

---

## 5. Escalation & Exceptions

If a breaking migration is deemed unavoidable:
1. An ADR must be submitted to `docs/adr/` documenting the rationale.
2. The change must be split into Expand/Contract phases.
3. Both Line A and Line B engineering leads must provide explicit sign-off before merge.


# Runbook: Database Migration Rollback & Forward-Fix

## 1. Cardinal Rule: Forward-Fix Is Preferred

The **R.G.E Accounting Platform** employs **forward-only database migrations**:
- **.NET 10**: Managed by DbUp (`Backend/db/migrations/V000X__description.sql`).
- **Python**: Managed by Alembic / custom live migration tracker (`schema_migrations`).

Because PostgreSQL state cannot be simply "checked out" to an earlier git commit without risking data loss on live production transactions, **forward-fix migrations (`V000X+1`) are the standard and preferred remediation mechanism**.

---

## 2. Shared Database Invariant During Dual-Line Operation

> [!WARNING]
> While both `migrating-C#` and `legacy-python` run against the same PostgreSQL database, **never drop a column, table, or stored function** as part of a rollback if the other line might read or write to it!
> All rollbacks of additive migrations must preserve backward compatibility.

---

## 3. Scenario A: Forward-Fix (Standard Procedure)

If a migration executed successfully but introduced a non-destructive logic error (e.g. invalid check constraint, wrong index columns, incorrect default):

### Step 1: Create the Next Sequential Migration Script
```bash
# Locate latest migration in Backend/db/migrations
ls -1 Backend/db/migrations
# Example latest: V0004__create_audit_trail.sql

# Create sequential fix script:
cat << 'EOF' > Backend/db/migrations/V0005__fix_audit_index.sql
-- Forward-fix for V0004 index performance
DROP INDEX IF EXISTS ix_audit_trail_created_at_old;
CREATE INDEX IF NOT EXISTS ix_audit_trail_created_at ON audit_trail (created_at DESC);
EOF
```

### Step 2: Validate via Migration Compatibility Gate
```powershell
./tools/versioning/check-migration-compat.ps1 -TargetPath "Backend/db/migrations"
```

### Step 3: Test Locally Against Clean Database
```powershell
# Run DbUp migrator locally
dotnet run --project Backend/tools/Rge.DbMigrator/Rge.DbMigrator.csproj
```

### Step 4: Commit & Deploy
```bash
git add Backend/db/migrations/V0005__fix_audit_index.sql
git commit -m "fix(db): add V0005 forward-fix for audit trail index"
git push origin migrating-C#
```

---

## 4. Scenario B: Rollback of an Erroneous Additive Schema Element

If a newly added column or table must be disabled or deprecated:

### Step 1: Neutralize Constraints First
Rather than dropping the column destructively, make it fully optional (`NULLABLE`) and drop any foreign key constraints:
```sql
-- V0006__deprecate_experimental_column.sql
ALTER TABLE orders ALTER COLUMN experimental_fee_tier DROP NOT NULL;
ALTER TABLE orders ALTER COLUMN experimental_fee_tier SET DEFAULT NULL;
```

### Step 2: Update Application Models
Remove the field reference from both C# models and Python SQLAlchemy models.

### Step 3: Drop Column in Final Cleanup (Post-Decommission)
Only after Python Line B has been completely retired and zero queries reference the column, execute the final drop script.

---

## 5. Scenario C: Failed In-Flight Migration (Crash Recovery)

If a migration failed midway due to a syntax error or lock timeout:
1. PostgreSQL wraps DDL in transactions by default, so the migration rolled back atomically.
2. Check `schema_migrations`:
   ```sql
   SELECT * FROM schema_migrations ORDER BY applied_at DESC LIMIT 5;
   ```
3. Correct the script syntax, verify locally, and re-run.


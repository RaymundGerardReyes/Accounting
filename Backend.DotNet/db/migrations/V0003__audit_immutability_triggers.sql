-- =============================================================================
-- Migration V0003: Append-Only Immutability Triggers for Audit & Financial Ledgers
-- =============================================================================

CREATE OR REPLACE FUNCTION fn_prevent_append_only_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Security Violation: Table "%" is strictly append-only. Operation "%" is forbidden.',
        TG_TABLE_NAME, TG_OP
        USING ERRCODE = '42501'; -- insufficient_privilege
END;
$$;

-- 1. Protect audit_events (Cryptographic Hash Chain)
DROP TRIGGER IF EXISTS trg_audit_events_immutable ON audit_events;
CREATE TRIGGER trg_audit_events_immutable
BEFORE UPDATE OR DELETE OR TRUNCATE ON audit_events
FOR EACH STATEMENT
EXECUTE FUNCTION fn_prevent_append_only_mutation();

-- 2. Protect advance_ledger_entries (Append-Only Financial Ledger)
DROP TRIGGER IF EXISTS trg_advance_ledger_entries_immutable ON advance_ledger_entries;
CREATE TRIGGER trg_advance_ledger_entries_immutable
BEFORE UPDATE OR DELETE OR TRUNCATE ON advance_ledger_entries
FOR EACH STATEMENT
EXECUTE FUNCTION fn_prevent_append_only_mutation();

-- 3. Protect closing_snapshots (Immutable Reconciliation Artifacts)
DROP TRIGGER IF EXISTS trg_closing_snapshots_immutable ON closing_snapshots;
CREATE TRIGGER trg_closing_snapshots_immutable
BEFORE UPDATE OR DELETE OR TRUNCATE ON closing_snapshots
FOR EACH STATEMENT
EXECUTE FUNCTION fn_prevent_append_only_mutation();


-- =============================================================================
-- V0003: Append-Only Immutability Triggers on Audit, Ledger & Closing Snapshots
-- Blocks UPDATE, DELETE, and TRUNCATE even by privileged table owners.
-- =============================================================================

CREATE OR REPLACE FUNCTION fn_prevent_append_only_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Security Invariant Violation: Table "%" is strictly append-only. % operations are forbidden.',
        TG_TABLE_NAME, TG_OP
        USING ERRCODE = '42501';
END;
$$;

DROP TRIGGER IF EXISTS trg_audit_events_immutable ON audit_events;
CREATE TRIGGER trg_audit_events_immutable
    BEFORE UPDATE OR DELETE OR TRUNCATE ON audit_events
    FOR EACH STATEMENT
    EXECUTE FUNCTION fn_prevent_append_only_mutation();

DROP TRIGGER IF EXISTS trg_advance_ledger_immutable ON advance_ledger_entries;
CREATE TRIGGER trg_advance_ledger_immutable
    BEFORE UPDATE OR DELETE OR TRUNCATE ON advance_ledger_entries
    FOR EACH STATEMENT
    EXECUTE FUNCTION fn_prevent_append_only_mutation();

DROP TRIGGER IF EXISTS trg_closing_snapshots_immutable ON closing_snapshots;
CREATE TRIGGER trg_closing_snapshots_immutable
    BEFORE UPDATE OR DELETE OR TRUNCATE ON closing_snapshots
    FOR EACH STATEMENT
    EXECUTE FUNCTION fn_prevent_append_only_mutation();

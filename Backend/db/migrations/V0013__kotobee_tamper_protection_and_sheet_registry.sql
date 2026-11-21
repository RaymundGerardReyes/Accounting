-- Migration V0013: Kotobee Anti-Tamper Protection, Hash-Chained Security Audit Log, and Sheet Registry

-- Requires pgcrypto for SHA-256 hash chaining
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- 1. Last-Known-Good (LKG) verified Google Sheet reference catalog
CREATE TABLE IF NOT EXISTS kotobee_sheet_catalog_snapshots (
    id                  UUID PRIMARY KEY,
    spreadsheet_id      TEXT        NOT NULL,
    sheet_range         TEXT        NOT NULL,
    content_sha256      TEXT        NOT NULL,
    active_row_count    INT         NOT NULL,
    mappings_json       JSONB       NOT NULL,
    synced_by           TEXT        NOT NULL,
    synced_at_utc       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_current          BOOLEAN     NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_kotobee_sheet_snapshots_current
    ON kotobee_sheet_catalog_snapshots (is_current)
    WHERE is_current = TRUE;

-- 2. Strictly append-only, SHA-256 hash-chained security audit log
CREATE TABLE IF NOT EXISTS kotobee_security_audit_log (
    seq_id              BIGSERIAL   PRIMARY KEY,
    event_at_utc        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    table_name          TEXT        NOT NULL,
    operation           TEXT        NOT NULL, -- 'INSERT', 'UPDATE', 'BLOCKED_DELETE', 'BLOCKED_UPDATE'
    record_id           TEXT        NOT NULL,
    actor_id            TEXT        NOT NULL,
    audit_reason        TEXT        NOT NULL,
    correlation_id      TEXT        NULL,
    old_values          JSONB       NULL,
    new_values          JSONB       NULL,
    prev_hash           TEXT        NOT NULL,
    row_hash            TEXT        NOT NULL
);

-- 3. Trigger function: Make audit log strictly append-only (blocks UPDATE and DELETE forever)
CREATE OR REPLACE FUNCTION fn_kotobee_audit_log_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'SECURITY_VIOLATION: kotobee_security_audit_log is strictly append-only. % is forbidden.', TG_OP
        USING ERRCODE = '42501';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_kotobee_audit_log_immutable ON kotobee_security_audit_log;
CREATE TRIGGER trg_kotobee_audit_log_immutable
BEFORE UPDATE OR DELETE ON kotobee_security_audit_log
FOR EACH ROW EXECUTE FUNCTION fn_kotobee_audit_log_immutable();

-- 4. Helper function: Append a SHA-256 hash-chained audit entry
CREATE OR REPLACE FUNCTION fn_kotobee_write_audit(
    p_table_name     TEXT,
    p_operation      TEXT,
    p_record_id      TEXT,
    p_actor_id       TEXT,
    p_audit_reason   TEXT,
    p_correlation_id TEXT,
    p_old_values     JSONB,
    p_new_values     JSONB
) RETURNS VOID AS $$
DECLARE
    v_prev_hash TEXT;
    v_payload   TEXT;
    v_row_hash  TEXT;
BEGIN
    -- Serialize chain writes so concurrent transactions never fork the hash chain
    PERFORM pg_advisory_xact_lock(hashtext('kotobee_security_audit_log_chain'));

    SELECT row_hash INTO v_prev_hash
    FROM kotobee_security_audit_log
    ORDER BY seq_id DESC
    LIMIT 1;

    v_prev_hash := COALESCE(v_prev_hash, 'GENESIS');
    v_payload := v_prev_hash || '|' || p_table_name || '|' || p_operation || '|' ||
                 p_record_id || '|' || p_actor_id || '|' || p_audit_reason || '|' ||
                 COALESCE(p_old_values::TEXT, '') || '|' || COALESCE(p_new_values::TEXT, '');
    v_row_hash := encode(digest(v_payload, 'sha256'), 'hex');

    INSERT INTO kotobee_security_audit_log (
        table_name, operation, record_id, actor_id, audit_reason,
        correlation_id, old_values, new_values, prev_hash, row_hash
    ) VALUES (
        p_table_name, p_operation, p_record_id, p_actor_id, p_audit_reason,
        p_correlation_id, p_old_values, p_new_values, v_prev_hash, v_row_hash
    );
END;
$$ LANGUAGE plpgsql;

-- 5. Trigger function: Protect kotobee_entitlements against DELETE and silent/illegal UPDATE
CREATE OR REPLACE FUNCTION fn_kotobee_entitlements_tamper_guard()
RETURNS TRIGGER AS $$
DECLARE
    v_actor  TEXT := NULLIF(BTRIM(current_setting('app.audit_actor', true)), '');
    v_reason TEXT := NULLIF(BTRIM(current_setting('app.audit_reason', true)), '');
    v_corr   TEXT := NULLIF(BTRIM(current_setting('app.correlation_id', true)), '');
BEGIN
    -- HARD BLOCK 1: Physical DELETE is NEVER allowed on transaction/entitlement records
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'SECURITY_VIOLATION: Physical DELETE on kotobee_entitlements is blocked. Use audited Revoked transition instead.'
            USING ERRCODE = '42501';
    END IF;

    -- HARD BLOCK 2: Every INSERT/UPDATE must declare an explicit audited actor & reason
    IF v_actor IS NULL OR v_reason IS NULL THEN
        RAISE EXCEPTION 'SECURITY_VIOLATION: Unnoticed % on kotobee_entitlements blocked (missing app.audit_actor or app.audit_reason).', TG_OP
            USING ERRCODE = '42501';
    END IF;

    IF TG_OP = 'UPDATE' THEN
        -- HARD BLOCK 3: Core transaction identity columns are immutable
        IF NEW.id <> OLD.id
           OR NEW.order_id <> OLD.order_id
           OR NEW.product_sku <> OLD.product_sku
           OR NEW.buyer_email <> OLD.buyer_email
           OR NEW.kotobee_book_id <> OLD.kotobee_book_id
           OR NEW.created_at_utc <> OLD.created_at_utc THEN
            RAISE EXCEPTION 'SECURITY_VIOLATION: Attempted modification of immutable transaction fields on kotobee_entitlements (id=%).', OLD.id
                USING ERRCODE = '42501';
        END IF;

        -- HARD BLOCK 4: Enforce valid state transitions (prevent silent downgrade of Active/Revoked)
        IF OLD.status = 'Active' AND NEW.status IN ('Pending', 'Failed') THEN
            RAISE EXCEPTION 'SECURITY_VIOLATION: Illegal downgrade from Active to % blocked.', NEW.status
                USING ERRCODE = '42501';
        END IF;

        IF OLD.status = 'Revoked' AND NEW.status <> 'Revoked' THEN
            RAISE EXCEPTION 'SECURITY_VIOLATION: Revoked entitlement cannot be silently flipped to %. Create a new paid order instead.', NEW.status
                USING ERRCODE = '42501';
        END IF;

        PERFORM fn_kotobee_write_audit(
            'kotobee_entitlements', 'UPDATE', OLD.id::TEXT,
            v_actor, v_reason, v_corr, to_jsonb(OLD), to_jsonb(NEW));
        RETURN NEW;
    END IF;

    IF TG_OP = 'INSERT' THEN
        PERFORM fn_kotobee_write_audit(
            'kotobee_entitlements', 'INSERT', NEW.id::TEXT,
            v_actor, v_reason, v_corr, NULL, to_jsonb(NEW));
        RETURN NEW;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_kotobee_entitlements_tamper_guard ON kotobee_entitlements;
CREATE TRIGGER trg_kotobee_entitlements_tamper_guard
BEFORE INSERT OR UPDATE OR DELETE ON kotobee_entitlements
FOR EACH ROW EXECUTE FUNCTION fn_kotobee_entitlements_tamper_guard();

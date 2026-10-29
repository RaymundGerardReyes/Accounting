-- =============================================================================
-- V0004: Outbox Worker Lease Columns & FOR UPDATE SKIP LOCKED Partial Index
-- Matches schemaVersion = "V0004" in Rge.Accounting.Host/Program.cs.
-- =============================================================================

ALTER TABLE fulfillment_outbox
    ADD COLUMN IF NOT EXISTS payload_json     TEXT NOT NULL DEFAULT '{}',
    ADD COLUMN IF NOT EXISTS correlation_id   VARCHAR(64) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS locked_by        VARCHAR(128) NULL,
    ADD COLUMN IF NOT EXISTS lease_expires_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS error_message    TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_fulfillment_outbox_claimable
    ON fulfillment_outbox (created_at ASC)
    WHERE status IN ('pending', 'processing');


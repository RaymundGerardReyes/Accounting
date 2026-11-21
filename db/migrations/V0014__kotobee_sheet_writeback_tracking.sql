-- V0014: Track automatic Google Sheet status write-back per Kotobee entitlement

ALTER TABLE kotobee_entitlements
    ADD COLUMN IF NOT EXISTS sheet_writeback_status TEXT        NOT NULL DEFAULT 'Pending', -- 'Pending', 'Synced', 'Failed'
    ADD COLUMN IF NOT EXISTS sheet_updated_tab      TEXT        NULL,
    ADD COLUMN IF NOT EXISTS sheet_updated_row      INT         NULL,
    ADD COLUMN IF NOT EXISTS sheet_writeback_error  TEXT        NULL,
    ADD COLUMN IF NOT EXISTS sheet_writeback_at_utc TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS idx_kotobee_entitlements_writeback_failed
    ON kotobee_entitlements (sheet_writeback_status)
    WHERE sheet_writeback_status IN ('Pending', 'Failed');

-- Migration: Add Kotobee ebook entitlement tracking table
CREATE TABLE IF NOT EXISTS kotobee_entitlements (
    id                UUID PRIMARY KEY,
    order_id          TEXT        NOT NULL,
    product_sku       TEXT        NOT NULL,
    buyer_email       TEXT        NOT NULL,
    kotobee_uid       TEXT        NULL,
    kotobee_mode      TEXT        NOT NULL DEFAULT 'library', -- 'library' or 'cloud'
    kotobee_lib_id    TEXT        NULL,
    kotobee_book_id   TEXT        NOT NULL,                   -- bid (library) or cid (cloud)
    status            TEXT        NOT NULL DEFAULT 'Pending', -- 'Pending', 'Active', 'Failed', 'Revoked'
    book_access_state TEXT        NULL,                       -- 'full', 'partial', 'none'
    attempt_count     INT         NOT NULL DEFAULT 0,
    last_error        TEXT        NULL,
    expires_at_utc    TIMESTAMPTZ NULL,
    created_at_utc    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    activated_at_utc  TIMESTAMPTZ NULL,
    revoked_at_utc    TIMESTAMPTZ NULL,
    updated_at_utc    TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Prevents duplicate grants for the same paid order + book
    CONSTRAINT uq_kotobee_entitlement_order_book UNIQUE (order_id, kotobee_book_id)
);

CREATE INDEX IF NOT EXISTS idx_kotobee_entitlements_order_id
    ON kotobee_entitlements (order_id);

CREATE INDEX IF NOT EXISTS idx_kotobee_entitlements_status
    ON kotobee_entitlements (status)
    WHERE status IN ('Pending', 'Failed');

-- =============================================================================
-- V0002: Covering & Keyset Pagination Indexes for < 30ms Reads & < 50ms Webhooks
-- =============================================================================

-- Keyset pagination index on orders (created_at DESC, id DESC) with covering status & amount
CREATE INDEX IF NOT EXISTS ix_orders_keyset_created_at
    ON orders (created_at DESC, id DESC)
    INCLUDE (order_number, status, amount_minor, currency);

-- Fast lookup of payment attempts by order_id and provider_payment_id
CREATE INDEX IF NOT EXISTS ix_payment_attempts_order_created
    ON payment_attempts (order_id, created_at DESC)
    INCLUDE (status, provider_payment_id, amount_minor, checkout_url);

CREATE INDEX IF NOT EXISTS ix_payment_attempts_provider_payment_id
    ON payment_attempts (provider_payment_id);

-- Partial index for non-terminal payment reconciliation
CREATE INDEX IF NOT EXISTS ix_payment_attempts_non_terminal
    ON payment_attempts (expires_at ASC)
    WHERE status IN ('pending', 'requires_action', 'processing', 'unknown');

-- Fast lookup of official receipts by order_id
CREATE INDEX IF NOT EXISTS ix_official_receipts_order_issued
    ON official_receipts (order_id, issued_at DESC);

-- Derived balance calculation index on advance_ledger_entries
CREATE INDEX IF NOT EXISTS ix_advance_ledger_entries_advance_id
    ON advance_ledger_entries (advance_id, occurred_at ASC)
    INCLUDE (entry_type, amount_minor, signed_amount_minor);

-- Audit trail query indexes by entity and correlation_id
CREATE INDEX IF NOT EXISTS ix_audit_events_entity
    ON audit_events (entity_type, entity_id, sequence_no DESC);

CREATE INDEX IF NOT EXISTS ix_audit_events_correlation_id
    ON audit_events (correlation_id, sequence_no ASC);


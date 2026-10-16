-- Schema for PayMongo Live Payment Orchestration
-- Database: PostgreSQL (compatible with SQLite for local dev)

CREATE TABLE IF NOT EXISTS orders (
    id VARCHAR(36) PRIMARY KEY,
    user_id VARCHAR(64) NOT NULL DEFAULT 'acme_admin',
    order_number VARCHAR(64) NOT NULL UNIQUE,
    title VARCHAR(255) NOT NULL,
    category VARCHAR(32) NOT NULL, -- 'product_order' | 'department_invoice'
    reference_code VARCHAR(64) NOT NULL,
    amount_minor BIGINT NOT NULL CHECK (amount_minor > 0),
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    status VARCHAR(32) NOT NULL DEFAULT 'pending', -- 'pending' | 'paid' | 'cancelled'
    metadata_json TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_orders_user_status ON orders (user_id, status);
CREATE INDEX IF NOT EXISTS idx_orders_order_number ON orders (order_number);

CREATE TABLE IF NOT EXISTS payment_attempts (
    id VARCHAR(36) PRIMARY KEY,
    order_id VARCHAR(36) NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    provider VARCHAR(32) NOT NULL DEFAULT 'paymongo',
    payment_method VARCHAR(32) NOT NULL DEFAULT 'hosted_checkout',
    provider_payment_id VARCHAR(128),
    amount_minor BIGINT NOT NULL CHECK (amount_minor > 0),
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    status VARCHAR(32) NOT NULL DEFAULT 'pending', -- 'pending' | 'requires_action' | 'processing' | 'succeeded' | 'failed' | 'cancelled' | 'expired' | 'unknown'
    idempotency_key VARCHAR(128) NOT NULL,
    checkout_url TEXT,
    failure_code VARCHAR(64),
    failure_message TEXT,
    expires_at TIMESTAMP WITH TIME ZONE,
    paid_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_payment_attempts_provider_idempotency UNIQUE (provider, idempotency_key)
);

CREATE INDEX IF NOT EXISTS idx_payment_attempts_order ON payment_attempts (order_id);
CREATE INDEX IF NOT EXISTS idx_payment_attempts_provider_id ON payment_attempts (provider, provider_payment_id);
CREATE INDEX IF NOT EXISTS idx_payment_attempts_status ON payment_attempts (status);

CREATE TABLE IF NOT EXISTS webhook_events (
    id VARCHAR(36) PRIMARY KEY,
    provider VARCHAR(32) NOT NULL DEFAULT 'paymongo',
    provider_event_id VARCHAR(128) NOT NULL,
    event_type VARCHAR(128) NOT NULL,
    processing_status VARCHAR(32) NOT NULL DEFAULT 'received', -- 'received' | 'processed' | 'ignored' | 'failed'
    received_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    processed_at TIMESTAMP WITH TIME ZONE,
    payload TEXT NOT NULL,
    CONSTRAINT uq_webhook_events_provider_event UNIQUE (provider, provider_event_id)
);

CREATE INDEX IF NOT EXISTS idx_webhook_events_provider_id ON webhook_events (provider, provider_event_id);

CREATE TABLE IF NOT EXISTS fulfillment_outbox (
    id VARCHAR(36) PRIMARY KEY,
    order_id VARCHAR(36) NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    payment_attempt_id VARCHAR(36) NOT NULL REFERENCES payment_attempts (id) ON DELETE CASCADE,
    event_type VARCHAR(64) NOT NULL DEFAULT 'order.paid',
    status VARCHAR(32) NOT NULL DEFAULT 'pending', -- 'pending' | 'completed' | 'failed'
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    processed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX IF NOT EXISTS idx_fulfillment_outbox_status ON fulfillment_outbox (status);


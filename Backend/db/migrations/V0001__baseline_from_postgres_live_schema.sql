-- =============================================================================
-- V0001: Baseline Authoritative Schema (28 Domain Models + 6 Standalone Tables)
-- Enforces integer centavos (BIGINT), UNIQUE replay guards, and DB-level SoD.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- -----------------------------------------------------------------------------
-- 1. Standalone Sequence, Auth, Session, OAuth & Receipt Tables
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sequence_counters (
    prefix      VARCHAR(64) PRIMARY KEY,
    current_val BIGINT NOT NULL DEFAULT 0,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS user_credentials (
    user_id         VARCHAR(64) PRIMARY KEY,
    username        VARCHAR(128) NOT NULL UNIQUE,
    email           VARCHAR(128) NOT NULL UNIQUE,
    password_hash   TEXT NOT NULL,
    failed_attempts INT NOT NULL DEFAULT 0,
    locked_until    TIMESTAMPTZ NULL,
    last_login_at   TIMESTAMPTZ NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS user_sessions (
    id                      VARCHAR(36) PRIMARY KEY,
    user_id                 VARCHAR(64) NOT NULL,
    token_hash              VARCHAR(64) NOT NULL UNIQUE,
    ip_address              VARCHAR(64) NULL,
    user_agent              TEXT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    expires_at              TIMESTAMPTZ NOT NULL,
    revoked_at              TIMESTAMPTZ NULL,
    act_as_role             VARCHAR(64) NULL,
    act_as_department_scope VARCHAR(64) NULL
);

CREATE TABLE IF NOT EXISTS oauth_states (
    state         VARCHAR(128) PRIMARY KEY,
    provider      VARCHAR(32) NOT NULL,
    code_verifier VARCHAR(256) NOT NULL,
    redirect_uri  TEXT NOT NULL,
    expires_at    TIMESTAMPTZ NOT NULL,
    consumed_at   TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS ph_holidays (
    holiday_date DATE PRIMARY KEY,
    name         VARCHAR(128) NOT NULL,
    is_regular   BOOLEAN NOT NULL DEFAULT TRUE
);

-- -----------------------------------------------------------------------------
-- 2. Commercial & Payment Orchestration Tables (Models 1-4 + Official Receipts)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS orders (
    id             VARCHAR(36) PRIMARY KEY,
    user_id        VARCHAR(64) NOT NULL DEFAULT 'acme_admin',
    order_number   VARCHAR(64) NOT NULL UNIQUE,
    title          VARCHAR(255) NOT NULL,
    category       VARCHAR(32) NOT NULL CHECK (category IN ('product_order', 'department_invoice')),
    reference_code VARCHAR(64) NOT NULL,
    amount_minor   BIGINT NOT NULL CHECK (amount_minor > 0),
    currency       VARCHAR(3) NOT NULL DEFAULT 'PHP',
    status         VARCHAR(32) NOT NULL CHECK (status IN ('pending', 'paid', 'cancelled', 'refunded')),
    metadata_json  TEXT NOT NULL DEFAULT '{}',
    created_at     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS payment_attempts (
    id                  VARCHAR(36) PRIMARY KEY,
    order_id            VARCHAR(36) NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    provider            VARCHAR(32) NOT NULL DEFAULT 'paymongo',
    payment_method      VARCHAR(32) NOT NULL,
    provider_payment_id VARCHAR(128) NOT NULL,
    amount_minor        BIGINT NOT NULL CHECK (amount_minor > 0),
    currency            VARCHAR(3) NOT NULL DEFAULT 'PHP',
    status              VARCHAR(32) NOT NULL CHECK (status IN (
        'pending', 'requires_action', 'processing', 'succeeded',
        'failed', 'cancelled', 'expired', 'unknown', 'duplicate_paid_review'
    )),
    idempotency_key     VARCHAR(128) NOT NULL,
    checkout_url        TEXT NOT NULL,
    failure_code        VARCHAR(64) NULL,
    failure_message     TEXT NULL,
    expires_at          TIMESTAMPTZ NOT NULL,
    paid_at             TIMESTAMPTZ NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_payment_attempts_provider_idempotency UNIQUE (provider, idempotency_key)
);

CREATE TABLE IF NOT EXISTS webhook_events (
    id                VARCHAR(36) PRIMARY KEY,
    provider          VARCHAR(32) NOT NULL DEFAULT 'paymongo',
    provider_event_id VARCHAR(128) NOT NULL,
    event_type        VARCHAR(128) NOT NULL,
    processing_status VARCHAR(32) NOT NULL CHECK (processing_status IN ('received', 'processed', 'ignored', 'failed')),
    payload           TEXT NOT NULL,
    received_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    processed_at      TIMESTAMPTZ NULL,
    CONSTRAINT uq_webhook_events_provider_event UNIQUE (provider, provider_event_id)
);

CREATE TABLE IF NOT EXISTS fulfillment_outbox (
    id                 VARCHAR(36) PRIMARY KEY,
    order_id           VARCHAR(36) NULL REFERENCES orders(id),
    payment_attempt_id VARCHAR(36) NULL REFERENCES payment_attempts(id),
    event_type         VARCHAR(64) NOT NULL DEFAULT 'order.paid',
    status             VARCHAR(32) NOT NULL CHECK (status IN ('pending', 'processing', 'completed', 'failed')),
    created_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    processed_at       TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS official_receipts (
    id                      VARCHAR(36) PRIMARY KEY,
    order_id                VARCHAR(36) NOT NULL REFERENCES orders(id),
    order_number            VARCHAR(64) NOT NULL,
    payment_attempt_id      VARCHAR(36) NOT NULL UNIQUE REFERENCES payment_attempts(id),
    receipt_number          VARCHAR(64) NOT NULL UNIQUE,
    acknowledgement_number  VARCHAR(64) NOT NULL UNIQUE,
    customer_name           VARCHAR(128) NOT NULL,
    customer_email          VARCHAR(128) NOT NULL,
    description             VARCHAR(255) NOT NULL,
    payment_method          VARCHAR(32) NOT NULL,
    vatable_sales_minor     BIGINT NOT NULL CHECK (vatable_sales_minor >= 0),
    vat_amount_minor        BIGINT NOT NULL CHECK (vat_amount_minor >= 0),
    vat_exempt_sales_minor  BIGINT NOT NULL CHECK (vat_exempt_sales_minor >= 0),
    zero_rated_sales_minor  BIGINT NOT NULL CHECK (zero_rated_sales_minor >= 0),
    gross_amount_minor      BIGINT NOT NULL CHECK (gross_amount_minor > 0),
    currency                VARCHAR(3) NOT NULL DEFAULT 'PHP',
    audit_digest            VARCHAR(128) NOT NULL,
    issued_at               TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT chk_official_receipt_vat_sum CHECK (
        vatable_sales_minor + vat_amount_minor + vat_exempt_sales_minor + zero_rated_sales_minor = gross_amount_minor
    )
);

-- -----------------------------------------------------------------------------
-- 3. Financial Governance, Approvals & Ledgers (Models 5-28)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users_finance (
    id             VARCHAR(64) PRIMARY KEY,
    employee_code  VARCHAR(64) NOT NULL UNIQUE,
    full_name      VARCHAR(128) NOT NULL,
    email          VARCHAR(128) NOT NULL UNIQUE,
    department_id  VARCHAR(64) NOT NULL,
    cost_center_id VARCHAR(32) NOT NULL,
    employee_group VARCHAR(64) NOT NULL,
    status         VARCHAR(32) NOT NULL CHECK (status IN ('active', 'suspended', 'terminated'))
);

CREATE TABLE IF NOT EXISTS role_assignments (
    id               VARCHAR(36) PRIMARY KEY,
    user_id          VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    role             VARCHAR(64) NOT NULL,
    department_scope VARCHAR(64) NOT NULL DEFAULT '*',
    is_active        INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    CONSTRAINT uq_role_assignments_user_role_scope UNIQUE (user_id, role, department_scope)
);

CREATE TABLE IF NOT EXISTS approval_matrix (
    id                SERIAL PRIMARY KEY,
    workflow         VARCHAR(64) NOT NULL,
    stage            VARCHAR(64) NOT NULL,
    role             VARCHAR(64) NOT NULL,
    min_amount_minor BIGINT NOT NULL CHECK (min_amount_minor >= 0),
    max_amount_minor BIGINT NOT NULL CHECK (max_amount_minor >= min_amount_minor)
);

CREATE TABLE IF NOT EXISTS audit_events (
    sequence_no    BIGINT PRIMARY KEY,
    actor_id       VARCHAR(64) NOT NULL,
    actor_role     VARCHAR(128) NOT NULL,
    action         VARCHAR(128) NOT NULL,
    entity_type    VARCHAR(64) NOT NULL,
    entity_id      VARCHAR(64) NOT NULL,
    previous_state TEXT NOT NULL,
    new_state      TEXT NOT NULL,
    reason         TEXT NULL,
    correlation_id VARCHAR(64) NOT NULL,
    evidence_ref   VARCHAR(64) NULL,
    occurred_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    prev_hash      VARCHAR(64) NOT NULL,
    record_hash    VARCHAR(64) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS document_references (
    id                VARCHAR(36) PRIMARY KEY,
    original_filename VARCHAR(255) NOT NULL,
    stored_filename   VARCHAR(255) NOT NULL UNIQUE,
    mime_type         VARCHAR(128) NOT NULL,
    size_bytes        BIGINT NOT NULL CHECK (size_bytes > 0 AND size_bytes <= 10485760),
    sha256_hash       VARCHAR(64) NOT NULL,
    uploaded_by       VARCHAR(64) NOT NULL,
    uploaded_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS cash_advances (
    id                        VARCHAR(36) PRIMARY KEY,
    advance_number            VARCHAR(64) NOT NULL UNIQUE,
    requestor_id              VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    employee_id               VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    department_id             VARCHAR(64) NOT NULL,
    purpose_code              VARCHAR(64) NOT NULL,
    requested_amount_minor    BIGINT NOT NULL CHECK (requested_amount_minor > 0),
    approved_amount_minor     BIGINT NOT NULL DEFAULT 0 CHECK (approved_amount_minor >= 0),
    date_needed               VARCHAR(10) NOT NULL,
    activity_start_date       VARCHAR(10) NOT NULL,
    activity_end_date         VARCHAR(10) NOT NULL,
    liquidation_due_date      VARCHAR(10) NOT NULL,
    approval_status           VARCHAR(32) NOT NULL,
    payment_status            VARCHAR(32) NOT NULL,
    liquidation_review_status VARCHAR(32) NOT NULL,
    settlement_status         VARCHAR(32) NOT NULL,
    posting_status            VARCHAR(32) NOT NULL,
    approved_by_id            VARCHAR(64) NULL REFERENCES users_finance(id),
    created_at                TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    -- Final DB Backstop for Four-Eyes Separation of Duties on Approval
    CONSTRAINT chk_cash_advance_sod_self_approval CHECK (approved_by_id IS NULL OR requestor_id <> approved_by_id)
);

CREATE TABLE IF NOT EXISTS disbursements (
    id                    VARCHAR(36) PRIMARY KEY,
    disbursement_number   VARCHAR(64) NOT NULL UNIQUE,
    advance_id            VARCHAR(36) NOT NULL REFERENCES cash_advances(id),
    payment_amount_minor  BIGINT NOT NULL CHECK (payment_amount_minor > 0),
    executed_amount_minor BIGINT NOT NULL DEFAULT 0 CHECK (executed_amount_minor >= 0),
    status                VARCHAR(32) NOT NULL,
    maker_id              VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    verifier_id           VARCHAR(64) NULL REFERENCES users_finance(id),
    bank_approver_id      VARCHAR(64) NULL REFERENCES users_finance(id),
    executed_by_id        VARCHAR(64) NULL REFERENCES users_finance(id),
    evidence_verifier_id  VARCHAR(64) NULL REFERENCES users_finance(id),
    bank_reference        VARCHAR(128) NULL,
    evidence_document_id  VARCHAR(36) NULL REFERENCES document_references(id),
    created_at            TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    -- Final DB Backstop for Four-Eyes Separation of Duties across all 4 stages
    CONSTRAINT chk_disbursement_four_eyes_sod CHECK (
        (verifier_id IS NULL OR verifier_id <> maker_id) AND
        (bank_approver_id IS NULL OR (bank_approver_id <> maker_id AND bank_approver_id <> verifier_id)) AND
        (evidence_verifier_id IS NULL OR (
            evidence_verifier_id <> maker_id AND
            evidence_verifier_id <> verifier_id AND
            evidence_verifier_id <> bank_approver_id
        ))
    )
);

CREATE TABLE IF NOT EXISTS advance_ledger_entries (
    id                  VARCHAR(36) PRIMARY KEY,
    advance_id          VARCHAR(36) NOT NULL REFERENCES cash_advances(id),
    employee_id         VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    entry_type          VARCHAR(32) NOT NULL,
    amount_minor        BIGINT NOT NULL CHECK (amount_minor > 0),
    signed_amount_minor BIGINT NOT NULL,
    currency            VARCHAR(3) NOT NULL DEFAULT 'PHP',
    created_by_actor_id VARCHAR(64) NOT NULL,
    occurred_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS expense_reports (
    id                          VARCHAR(36) PRIMARY KEY,
    report_number               VARCHAR(64) NOT NULL UNIQUE,
    advance_id                  VARCHAR(36) NOT NULL REFERENCES cash_advances(id),
    employee_id                 VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    reporting_period            VARCHAR(16) NOT NULL,
    consolidated_receipt_doc_id VARCHAR(36) NULL REFERENCES document_references(id),
    mfar_doc_id                 VARCHAR(36) NULL REFERENCES document_references(id),
    review_status               VARCHAR(32) NOT NULL,
    created_at                  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS expense_lines (
    id                    VARCHAR(36) PRIMARY KEY,
    report_id             VARCHAR(36) NOT NULL REFERENCES expense_reports(id) ON DELETE CASCADE,
    expense_date          VARCHAR(10) NOT NULL,
    expense_category      VARCHAR(64) NOT NULL,
    claimed_amount_minor  BIGINT NOT NULL CHECK (claimed_amount_minor > 0),
    approved_amount_minor BIGINT NOT NULL DEFAULT 0 CHECK (approved_amount_minor >= 0),
    receipt_or_invoice_no VARCHAR(128) NOT NULL,
    fingerprint_key       VARCHAR(64) NOT NULL UNIQUE,
    tax_code              VARCHAR(64) NOT NULL DEFAULT 'VAT_12',
    vat_amount_minor      BIGINT NOT NULL DEFAULT 0 CHECK (vat_amount_minor >= 0)
);

CREATE TABLE IF NOT EXISTS settlement_applications (
    id                   VARCHAR(36) PRIMARY KEY,
    advance_id           VARCHAR(36) NOT NULL REFERENCES cash_advances(id),
    report_id            VARCHAR(36) NULL REFERENCES expense_reports(id),
    application_type     VARCHAR(32) NOT NULL,
    applied_amount_minor BIGINT NOT NULL CHECK (applied_amount_minor > 0),
    created_at           TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS closing_snapshots (
    id                      VARCHAR(36) PRIMARY KEY,
    advance_id              VARCHAR(36) NOT NULL UNIQUE REFERENCES cash_advances(id),
    released_amount_minor   BIGINT NOT NULL,
    claimed_expense_minor   BIGINT NOT NULL,
    approved_expense_minor  BIGINT NOT NULL,
    cash_returned_minor     BIGINT NOT NULL,
    reimbursement_minor     BIGINT NOT NULL,
    final_outstanding_minor BIGINT NOT NULL,
    snapshot_json           TEXT NOT NULL,
    closed_at               TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS financial_policies (
    policy_code VARCHAR(64) PRIMARY KEY,
    title       VARCHAR(128) NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS policy_versions (
    id                                  VARCHAR(36) PRIMARY KEY,
    policy_code                         VARCHAR(64) NOT NULL REFERENCES financial_policies(policy_code),
    version_no                          INT NOT NULL,
    status                              VARCHAR(32) NOT NULL CHECK (status IN ('draft', 'submitted', 'active', 'retired')),
    aggregate_monthly_limit_minor       BIGINT NOT NULL,
    max_reports_per_period              BIGINT NOT NULL,
    submission_deadline_day_next_month  BIGINT NOT NULL,
    finance_sla_days                    BIGINT NOT NULL,
    authored_by                         VARCHAR(64) NOT NULL,
    approved_by                         VARCHAR(64) NULL,
    created_at                          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT chk_policy_version_sod CHECK (approved_by IS NULL OR authored_by <> approved_by)
);

CREATE TABLE IF NOT EXISTS policy_rules (
    id                VARCHAR(36) PRIMARY KEY,
    policy_version_id VARCHAR(36) NOT NULL REFERENCES policy_versions(id) ON DELETE CASCADE,
    rule_code         VARCHAR(64) NOT NULL,
    max_amount_minor  BIGINT NOT NULL
);

CREATE TABLE IF NOT EXISTS period_counters (
    id                    VARCHAR(36) PRIMARY KEY,
    employee_id           VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    reporting_period      VARCHAR(16) NOT NULL,
    submitted_reports_cnt BIGINT NOT NULL DEFAULT 0,
    total_claimed_minor   BIGINT NOT NULL DEFAULT 0,
    CONSTRAINT uq_period_counters_emp_period UNIQUE (employee_id, reporting_period)
);

CREATE TABLE IF NOT EXISTS exception_requests (
    id          VARCHAR(36) PRIMARY KEY,
    employee_id VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    policy_code VARCHAR(64) NOT NULL,
    reason      TEXT NOT NULL,
    status      VARCHAR(32) NOT NULL,
    decided_by  VARCHAR(64) NULL REFERENCES users_finance(id),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT chk_exception_request_sod CHECK (decided_by IS NULL OR employee_id <> decided_by)
);

CREATE TABLE IF NOT EXISTS vendors (
    id                  VARCHAR(36) PRIMARY KEY,
    name                VARCHAR(128) NOT NULL,
    tin                 VARCHAR(32) NOT NULL UNIQUE,
    bank_name           VARCHAR(64) NOT NULL,
    masked_bank_account VARCHAR(64) NOT NULL,
    encrypted_bank_acct TEXT NOT NULL,
    status              VARCHAR(32) NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS vendor_bank_changes (
    id                      VARCHAR(36) PRIMARY KEY,
    vendor_id               VARCHAR(36) NOT NULL REFERENCES vendors(id),
    maker_id                VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    approved_by_id          VARCHAR(64) NULL REFERENCES users_finance(id),
    new_bank_name           VARCHAR(64) NOT NULL,
    masked_new_account      VARCHAR(64) NOT NULL,
    encrypted_new_account   TEXT NOT NULL,
    bank_certificate_doc_id VARCHAR(36) NOT NULL REFERENCES document_references(id),
    status                  VARCHAR(32) NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT chk_vendor_bank_change_dual_auth CHECK (approved_by_id IS NULL OR maker_id <> approved_by_id)
);

CREATE TABLE IF NOT EXISTS tax_codes (
    code              VARCHAR(64) PRIMARY KEY,
    description       VARCHAR(255) NOT NULL,
    rate_bps          INT NOT NULL CHECK (rate_bps >= 0 AND rate_bps <= 10000),
    requires_bir_2307 BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS purchase_requests (
    id                 VARCHAR(36) PRIMARY KEY,
    pr_number          VARCHAR(64) NOT NULL UNIQUE,
    requestor_id       VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    department_id      VARCHAR(64) NOT NULL,
    vendor_id          VARCHAR(36) NOT NULL REFERENCES vendors(id),
    total_amount_minor BIGINT NOT NULL CHECK (total_amount_minor > 0),
    status             VARCHAR(32) NOT NULL,
    approved_by_id     VARCHAR(64) NULL REFERENCES users_finance(id),
    created_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT chk_purchase_request_sod CHECK (approved_by_id IS NULL OR requestor_id <> approved_by_id)
);

CREATE TABLE IF NOT EXISTS purchase_request_lines (
    id              VARCHAR(36) PRIMARY KEY,
    pr_id           VARCHAR(36) NOT NULL REFERENCES purchase_requests(id) ON DELETE CASCADE,
    description     VARCHAR(255) NOT NULL,
    quantity        INT NOT NULL CHECK (quantity > 0),
    unit_cost_minor BIGINT NOT NULL CHECK (unit_cost_minor > 0)
);

CREATE TABLE IF NOT EXISTS account_payables (
    id                    VARCHAR(36) PRIMARY KEY,
    pr_id                 VARCHAR(36) NOT NULL REFERENCES purchase_requests(id),
    vendor_id             VARCHAR(36) NOT NULL REFERENCES vendors(id),
    raw_invoice_no        VARCHAR(128) NOT NULL,
    normalized_invoice_no VARCHAR(128) NOT NULL,
    gross_amount_minor    BIGINT NOT NULL CHECK (gross_amount_minor > 0),
    ewt_amount_minor      BIGINT NOT NULL DEFAULT 0 CHECK (ewt_amount_minor >= 0),
    net_payable_minor     BIGINT NOT NULL CHECK (net_payable_minor > 0),
    status                VARCHAR(32) NOT NULL,
    created_at            TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_account_payables_vendor_norm_inv UNIQUE (vendor_id, normalized_invoice_no)
);

CREATE TABLE IF NOT EXISTS payment_records (
    id               VARCHAR(36) PRIMARY KEY,
    payable_id       VARCHAR(36) NOT NULL REFERENCES account_payables(id),
    maker_id         VARCHAR(64) NOT NULL REFERENCES users_finance(id),
    verifier_id      VARCHAR(64) NULL REFERENCES users_finance(id),
    bank_approver_id VARCHAR(64) NULL REFERENCES users_finance(id),
    amount_minor     BIGINT NOT NULL CHECK (amount_minor > 0),
    status           VARCHAR(32) NOT NULL,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT chk_payment_record_sod CHECK (
        (verifier_id IS NULL OR verifier_id <> maker_id) AND
        (bank_approver_id IS NULL OR (bank_approver_id <> maker_id AND bank_approver_id <> verifier_id))
    )
);

CREATE TABLE IF NOT EXISTS exception_register_items (
    id          VARCHAR(36) PRIMARY KEY,
    entity_type VARCHAR(64) NOT NULL,
    entity_id   VARCHAR(64) NOT NULL,
    severity    VARCHAR(32) NOT NULL,
    details     TEXT NOT NULL,
    status      VARCHAR(32) NOT NULL DEFAULT 'open',
    resolved_by VARCHAR(64) NULL REFERENCES users_finance(id),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    resolved_at TIMESTAMPTZ NULL
);


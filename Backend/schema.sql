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

-- ============================================================================
-- Finance Operations Schema (Cash Advances, Liquidation, Policy, P2P, Audit)
-- ============================================================================

CREATE TABLE IF NOT EXISTS users_finance (
    id VARCHAR(64) PRIMARY KEY,
    employee_code VARCHAR(64) NOT NULL UNIQUE,
    full_name VARCHAR(128) NOT NULL,
    email VARCHAR(128) NOT NULL,
    department_id VARCHAR(64) NOT NULL,
    cost_center_id VARCHAR(32) NOT NULL,
    employee_group VARCHAR(64) NOT NULL DEFAULT 'field_staff',
    status VARCHAR(32) NOT NULL DEFAULT 'active',
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS role_assignments (
    id VARCHAR(36) PRIMARY KEY,
    user_id VARCHAR(64) NOT NULL REFERENCES users_finance (id) ON DELETE CASCADE,
    role VARCHAR(64) NOT NULL,
    department_scope VARCHAR(64) NOT NULL DEFAULT '*',
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_role_assignment_user_role_scope UNIQUE (user_id, role, department_scope)
);

CREATE TABLE IF NOT EXISTS approval_matrix (
    id VARCHAR(36) PRIMARY KEY,
    workflow VARCHAR(64) NOT NULL,
    stage VARCHAR(64) NOT NULL,
    role VARCHAR(64) NOT NULL,
    department_scope VARCHAR(64) NOT NULL DEFAULT '*',
    min_amount_minor BIGINT NOT NULL DEFAULT 0,
    max_amount_minor BIGINT NOT NULL,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS number_sequences (
    prefix VARCHAR(32) PRIMARY KEY,
    current_value BIGINT NOT NULL DEFAULT 0,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS holiday_calendar (
    holiday_date VARCHAR(10) PRIMARY KEY,
    name VARCHAR(128) NOT NULL,
    country_code VARCHAR(8) NOT NULL DEFAULT 'PH'
);

CREATE TABLE IF NOT EXISTS audit_events (
    id VARCHAR(36) PRIMARY KEY,
    sequence_no BIGINT NOT NULL UNIQUE,
    actor_id VARCHAR(64) NOT NULL,
    actor_role VARCHAR(128) NOT NULL,
    action VARCHAR(128) NOT NULL,
    entity_type VARCHAR(64) NOT NULL,
    entity_id VARCHAR(64) NOT NULL,
    previous_state TEXT,
    new_state TEXT,
    reason TEXT,
    correlation_id VARCHAR(64),
    evidence_ref VARCHAR(64),
    occurred_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    prev_hash VARCHAR(64) NOT NULL,
    record_hash VARCHAR(64) NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS financial_policies (
    id VARCHAR(36) PRIMARY KEY,
    policy_code VARCHAR(64) NOT NULL UNIQUE,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    scope_workflow VARCHAR(64) NOT NULL,
    applicable_employee_groups VARCHAR(255) NOT NULL DEFAULT 'field_staff',
    applicable_departments VARCHAR(255) NOT NULL DEFAULT '*',
    current_version_id VARCHAR(36),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS policy_versions (
    id VARCHAR(36) PRIMARY KEY,
    policy_id VARCHAR(36) NOT NULL REFERENCES financial_policies (id) ON DELETE CASCADE,
    version_number BIGINT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'draft',
    effective_from VARCHAR(10) NOT NULL,
    effective_until VARCHAR(10),
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    aggregate_monthly_limit_minor BIGINT NOT NULL,
    max_reports_per_period BIGINT NOT NULL DEFAULT 1,
    period_type VARCHAR(32) NOT NULL DEFAULT 'monthly',
    submission_deadline_day_of_next_month BIGINT DEFAULT 2,
    deadline_interpretation VARCHAR(64) NOT NULL DEFAULT 'day_of_following_month_unconfirmed',
    finance_sla_days BIGINT NOT NULL DEFAULT 5,
    sla_day_type VARCHAR(32) NOT NULL DEFAULT 'business_days_unconfirmed',
    require_consolidated_receipt_pdf INTEGER NOT NULL DEFAULT 1,
    require_mfar_for_out_of_base INTEGER NOT NULL DEFAULT 1,
    allow_partial_line_approval INTEGER NOT NULL DEFAULT 1,
    block_new_advance_when_overdue INTEGER NOT NULL DEFAULT 1,
    max_advance_amount_minor BIGINT,
    eligible_categories_json TEXT NOT NULL,
    previous_values_json TEXT,
    change_justification TEXT NOT NULL,
    ratification_status VARCHAR(64) NOT NULL DEFAULT 'pending_finance_ratification',
    author_id VARCHAR(64) NOT NULL,
    approver_id VARCHAR(64),
    submitted_at TIMESTAMP WITH TIME ZONE,
    approved_at TIMESTAMP WITH TIME ZONE,
    retired_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_policy_version_number UNIQUE (policy_id, version_number)
);

CREATE TABLE IF NOT EXISTS policy_rules (
    id VARCHAR(36) PRIMARY KEY,
    policy_version_id VARCHAR(36) NOT NULL REFERENCES policy_versions (id) ON DELETE CASCADE,
    rule_code VARCHAR(64) NOT NULL,
    description VARCHAR(255) NOT NULL,
    severity VARCHAR(32) NOT NULL DEFAULT 'block',
    enforcement_mode VARCHAR(32) NOT NULL DEFAULT 'exception_allowed',
    is_exception_allowed INTEGER NOT NULL DEFAULT 1,
    required_exception_role VARCHAR(64) NOT NULL DEFAULT 'finance_controller',
    parameters_json TEXT NOT NULL DEFAULT '{}'
);

CREATE TABLE IF NOT EXISTS period_counters (
    id VARCHAR(36) PRIMARY KEY,
    employee_id VARCHAR(64) NOT NULL,
    period_key VARCHAR(16) NOT NULL,
    rule_code VARCHAR(64) NOT NULL,
    submission_count BIGINT NOT NULL DEFAULT 0,
    aggregate_amount_minor BIGINT NOT NULL DEFAULT 0,
    active_report_id VARCHAR(36),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_period_counter_emp_period_rule UNIQUE (employee_id, period_key, rule_code)
);

CREATE TABLE IF NOT EXISTS exception_requests (
    id VARCHAR(36) PRIMARY KEY,
    exception_number VARCHAR(64) NOT NULL UNIQUE,
    employee_id VARCHAR(64) NOT NULL,
    requester_id VARCHAR(64) NOT NULL,
    target_entity_type VARCHAR(64) NOT NULL DEFAULT 'expense_report',
    target_entity_id VARCHAR(36),
    reporting_period VARCHAR(16) NOT NULL,
    policy_version_id VARCHAR(36) NOT NULL REFERENCES policy_versions (id),
    violated_rule_code VARCHAR(64) NOT NULL,
    exception_type VARCHAR(64) NOT NULL,
    requested_amount_minor BIGINT NOT NULL DEFAULT 0,
    approved_max_amount_minor BIGINT,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    business_justification TEXT NOT NULL,
    supporting_document_ids_json TEXT,
    status VARCHAR(32) NOT NULL DEFAULT 'draft',
    assigned_reviewer_id VARCHAR(64),
    approver_id VARCHAR(64),
    decision_reason TEXT,
    submitted_at TIMESTAMP WITH TIME ZONE,
    decided_at TIMESTAMP WITH TIME ZONE,
    expires_at TIMESTAMP WITH TIME ZONE,
    consumed_at TIMESTAMP WITH TIME ZONE,
    consumed_by_entity_id VARCHAR(36),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS cash_advances (
    id VARCHAR(36) PRIMARY KEY,
    advance_number VARCHAR(64) NOT NULL UNIQUE,
    requestor_id VARCHAR(64) NOT NULL REFERENCES users_finance (id),
    employee_id VARCHAR(64) NOT NULL REFERENCES users_finance (id),
    department_id VARCHAR(64) NOT NULL,
    cost_center_id VARCHAR(32) NOT NULL,
    purpose_code VARCHAR(64) NOT NULL,
    business_justification TEXT NOT NULL,
    destination_or_site VARCHAR(255),
    requested_amount_minor BIGINT NOT NULL CHECK (requested_amount_minor > 0),
    approved_amount_minor BIGINT,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    date_needed VARCHAR(10) NOT NULL,
    activity_start_date VARCHAR(10),
    activity_end_date VARCHAR(10),
    liquidation_due_date VARCHAR(10),
    remarks TEXT,
    attachment_ids_json TEXT,
    approval_status VARCHAR(32) NOT NULL DEFAULT 'draft',
    payment_status VARCHAR(32) NOT NULL DEFAULT 'not_started',
    liquidation_review_status VARCHAR(32) NOT NULL DEFAULT 'not_started',
    settlement_status VARCHAR(32) NOT NULL DEFAULT 'open',
    posting_status VARCHAR(32) NOT NULL DEFAULT 'not_applicable',
    policy_version_id VARCHAR(36),
    policy_evaluation_json TEXT,
    idempotency_key VARCHAR(128) UNIQUE,
    submitted_at TIMESTAMP WITH TIME ZONE,
    approved_at TIMESTAMP WITH TIME ZONE,
    closed_at TIMESTAMP WITH TIME ZONE,
    closed_by_id VARCHAR(64),
    closure_reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS approval_history (
    id VARCHAR(36) PRIMARY KEY,
    entity_type VARCHAR(64) NOT NULL,
    entity_id VARCHAR(36) NOT NULL,
    approver_id VARCHAR(64) NOT NULL,
    approver_role VARCHAR(64) NOT NULL,
    approval_stage VARCHAR(64) NOT NULL,
    decision VARCHAR(32) NOT NULL,
    approved_amount_minor BIGINT,
    comments TEXT NOT NULL,
    previous_status VARCHAR(32) NOT NULL,
    resulting_status VARCHAR(32) NOT NULL,
    decided_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS disbursements (
    id VARCHAR(36) PRIMARY KEY,
    disbursement_number VARCHAR(64) NOT NULL UNIQUE,
    advance_id VARCHAR(36) NOT NULL REFERENCES cash_advances (id) ON DELETE RESTRICT,
    payment_amount_minor BIGINT NOT NULL CHECK (payment_amount_minor > 0),
    executed_amount_minor BIGINT,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    payment_method VARCHAR(64) NOT NULL DEFAULT 'bank_transfer',
    recipient_account_ref VARCHAR(128),
    status VARCHAR(32) NOT NULL DEFAULT 'prepared',
    maker_id VARCHAR(64) NOT NULL,
    prepared_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    verifier_id VARCHAR(64),
    verified_at TIMESTAMP WITH TIME ZONE,
    verification_notes TEXT,
    bank_approver_id VARCHAR(64),
    bank_approved_at TIMESTAMP WITH TIME ZONE,
    bank_approval_notes TEXT,
    executed_by_id VARCHAR(64),
    executed_at TIMESTAMP WITH TIME ZONE,
    bank_reference VARCHAR(128),
    evidence_document_id VARCHAR(36),
    evidence_verifier_id VARCHAR(64),
    evidence_verified_at TIMESTAMP WITH TIME ZONE,
    evidence_verification_notes TEXT,
    failure_reason TEXT,
    idempotency_key VARCHAR(128) UNIQUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS advance_ledger_entries (
    id VARCHAR(36) PRIMARY KEY,
    advance_id VARCHAR(36) NOT NULL REFERENCES cash_advances (id) ON DELETE RESTRICT,
    employee_id VARCHAR(64) NOT NULL,
    entry_type VARCHAR(64) NOT NULL,
    signed_amount_minor BIGINT NOT NULL,
    amount_minor BIGINT NOT NULL CHECK (amount_minor >= 0),
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    source_type VARCHAR(64) NOT NULL,
    source_id VARCHAR(36) NOT NULL,
    reference_code VARCHAR(128),
    evidence_document_id VARCHAR(36),
    actor_id VARCHAR(64) NOT NULL,
    notes TEXT,
    effective_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS document_references (
    id VARCHAR(36) PRIMARY KEY,
    document_number VARCHAR(64) NOT NULL UNIQUE,
    uploader_id VARCHAR(64) NOT NULL,
    department_id VARCHAR(64) NOT NULL,
    document_type VARCHAR(64) NOT NULL,
    entity_type VARCHAR(64),
    entity_id VARCHAR(36),
    original_filename VARCHAR(255) NOT NULL,
    stored_filename VARCHAR(255) NOT NULL UNIQUE,
    mime_type VARCHAR(128) NOT NULL,
    size_bytes BIGINT NOT NULL,
    sha256_hash VARCHAR(64) NOT NULL,
    is_restricted INTEGER NOT NULL DEFAULT 0,
    retention_status VARCHAR(32) NOT NULL DEFAULT 'active',
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS expense_reports (
    id VARCHAR(36) PRIMARY KEY,
    report_number VARCHAR(64) NOT NULL UNIQUE,
    employee_id VARCHAR(64) NOT NULL REFERENCES users_finance (id),
    department_id VARCHAR(64) NOT NULL,
    cost_center_id VARCHAR(32) NOT NULL,
    reporting_period VARCHAR(16) NOT NULL,
    business_purpose_summary TEXT NOT NULL,
    out_of_base_activity INTEGER NOT NULL DEFAULT 0,
    consolidated_receipt_doc_id VARCHAR(36),
    mfar_doc_id VARCHAR(36),
    employee_declaration_confirmed INTEGER NOT NULL DEFAULT 0,
    remarks TEXT,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    review_status VARCHAR(32) NOT NULL DEFAULT 'draft',
    settlement_status VARCHAR(32) NOT NULL DEFAULT 'unapplied',
    posting_status VARCHAR(32) NOT NULL DEFAULT 'not_applicable',
    policy_version_id VARCHAR(36),
    policy_evaluation_json TEXT,
    exception_request_id VARCHAR(36),
    reviewer_id VARCHAR(64),
    reviewed_at TIMESTAMP WITH TIME ZONE,
    review_notes TEXT,
    rejection_reason TEXT,
    submitted_at TIMESTAMP WITH TIME ZONE,
    slot_consumed INTEGER NOT NULL DEFAULT 0,
    idempotency_key VARCHAR(128) UNIQUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS expense_lines (
    id VARCHAR(36) PRIMARY KEY,
    expense_report_id VARCHAR(36) NOT NULL REFERENCES expense_reports (id) ON DELETE CASCADE,
    employee_id VARCHAR(64) NOT NULL,
    transaction_date VARCHAR(10) NOT NULL,
    school_or_destination VARCHAR(255),
    expense_category VARCHAR(64) NOT NULL,
    business_purpose TEXT NOT NULL,
    merchant_or_payee VARCHAR(255) NOT NULL,
    receipt_or_invoice_no VARCHAR(128),
    fingerprint_key VARCHAR(255),
    claimed_amount_minor BIGINT NOT NULL CHECK (claimed_amount_minor > 0),
    approved_amount_minor BIGINT,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    out_of_base INTEGER NOT NULL DEFAULT 0,
    receipt_document_id VARCHAR(36),
    mfar_document_id VARCHAR(36),
    tax_code VARCHAR(64),
    vat_amount_minor BIGINT NOT NULL DEFAULT 0,
    duplicate_warning INTEGER NOT NULL DEFAULT 0,
    duplicate_of_line_id VARCHAR(36),
    line_status VARCHAR(32) NOT NULL DEFAULT 'pending',
    reviewer_id VARCHAR(64),
    reviewed_at TIMESTAMP WITH TIME ZONE,
    review_notes TEXT,
    rejection_reason TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS settlement_applications (
    id VARCHAR(36) PRIMARY KEY,
    expense_report_id VARCHAR(36) REFERENCES expense_reports (id),
    expense_line_id VARCHAR(36) REFERENCES expense_lines (id),
    advance_id VARCHAR(36) REFERENCES cash_advances (id),
    employee_id VARCHAR(64) NOT NULL,
    application_type VARCHAR(64) NOT NULL,
    applied_amount_minor BIGINT NOT NULL CHECK (applied_amount_minor > 0),
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    ledger_entry_id VARCHAR(36),
    evidence_document_id VARCHAR(36),
    bank_reference VARCHAR(128),
    applied_by_id VARCHAR(64) NOT NULL,
    notes TEXT,
    applied_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS closing_snapshots (
    id VARCHAR(36) PRIMARY KEY,
    advance_id VARCHAR(36) NOT NULL UNIQUE REFERENCES cash_advances (id) ON DELETE RESTRICT,
    released_amount_minor BIGINT NOT NULL,
    claimed_expense_minor BIGINT NOT NULL,
    approved_expense_minor BIGINT NOT NULL,
    rejected_expense_minor BIGINT NOT NULL,
    cash_returned_minor BIGINT NOT NULL,
    reimbursement_minor BIGINT NOT NULL,
    final_outstanding_minor BIGINT NOT NULL,
    authorized_exception_id VARCHAR(36),
    controller_id VARCHAR(64) NOT NULL,
    closure_reason TEXT NOT NULL,
    snapshot_json TEXT NOT NULL,
    closed_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS vendors (
    id VARCHAR(36) PRIMARY KEY,
    vendor_code VARCHAR(64) NOT NULL UNIQUE,
    registered_name VARCHAR(255) NOT NULL,
    payee_type VARCHAR(64) NOT NULL,
    tin VARCHAR(64),
    address TEXT,
    contact_person VARCHAR(128),
    contact_email VARCHAR(128),
    payment_method VARCHAR(64) NOT NULL DEFAULT 'bank_transfer',
    bank_name VARCHAR(128),
    bank_account_masked VARCHAR(64),
    bank_account_ref VARCHAR(128),
    verification_status VARCHAR(32) NOT NULL DEFAULT 'pending_verification',
    kyc_document_id VARCHAR(36),
    created_by_id VARCHAR(64) NOT NULL,
    verified_by_id VARCHAR(64),
    verified_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS vendor_bank_changes (
    id VARCHAR(36) PRIMARY KEY,
    vendor_id VARCHAR(36) NOT NULL REFERENCES vendors (id) ON DELETE CASCADE,
    previous_bank_ref VARCHAR(128),
    new_bank_name VARCHAR(128) NOT NULL,
    new_bank_account_masked VARCHAR(64) NOT NULL,
    new_bank_account_ref VARCHAR(128) NOT NULL,
    supporting_document_id VARCHAR(36),
    reason TEXT NOT NULL,
    status VARCHAR(32) NOT NULL DEFAULT 'pending_approval',
    requested_by_id VARCHAR(64) NOT NULL,
    approved_by_id VARCHAR(64),
    requested_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    decided_at TIMESTAMP WITH TIME ZONE
);

CREATE TABLE IF NOT EXISTS tax_codes (
    code VARCHAR(64) PRIMARY KEY,
    description VARCHAR(255) NOT NULL,
    tax_type VARCHAR(32) NOT NULL,
    rate_bps BIGINT NOT NULL,
    requires_bir_2307 INTEGER NOT NULL DEFAULT 0,
    cpa_approved INTEGER NOT NULL DEFAULT 0,
    approved_by_id VARCHAR(64),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS purchase_requests (
    id VARCHAR(36) PRIMARY KEY,
    request_number VARCHAR(64) NOT NULL UNIQUE,
    requestor_id VARCHAR(64) NOT NULL REFERENCES users_finance (id),
    vendor_id VARCHAR(36) NOT NULL REFERENCES vendors (id),
    payment_category VARCHAR(64) NOT NULL,
    is_restricted_category INTEGER NOT NULL DEFAULT 0,
    business_purpose TEXT NOT NULL,
    department_id VARCHAR(64) NOT NULL,
    cost_center_id VARCHAR(32) NOT NULL,
    budget_reference VARCHAR(64),
    requested_amount_minor BIGINT NOT NULL CHECK (requested_amount_minor > 0),
    approved_amount_minor BIGINT,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    requested_payment_date VARCHAR(10) NOT NULL,
    due_date VARCHAR(10),
    supporting_doc_ids_json TEXT,
    invoice_doc_id VARCHAR(36),
    remarks TEXT,
    approval_status VARCHAR(32) NOT NULL DEFAULT 'draft',
    workflow_status VARCHAR(32) NOT NULL DEFAULT 'draft',
    posting_status VARCHAR(32) NOT NULL DEFAULT 'not_applicable',
    idempotency_key VARCHAR(128) UNIQUE,
    submitted_at TIMESTAMP WITH TIME ZONE,
    approved_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS purchase_request_lines (
    id VARCHAR(36) PRIMARY KEY,
    purchase_request_id VARCHAR(36) NOT NULL REFERENCES purchase_requests (id) ON DELETE CASCADE,
    description VARCHAR(255) NOT NULL,
    quantity BIGINT NOT NULL DEFAULT 1,
    unit_amount_minor BIGINT NOT NULL,
    total_amount_minor BIGINT NOT NULL,
    account_code VARCHAR(64)
);

CREATE TABLE IF NOT EXISTS account_payables (
    id VARCHAR(36) PRIMARY KEY,
    payable_number VARCHAR(64) NOT NULL UNIQUE,
    purchase_request_id VARCHAR(36) NOT NULL REFERENCES purchase_requests (id) ON DELETE RESTRICT,
    vendor_id VARCHAR(36) NOT NULL REFERENCES vendors (id) ON DELETE RESTRICT,
    invoice_number VARCHAR(128) NOT NULL,
    normalized_invoice_no VARCHAR(128) NOT NULL,
    invoice_date VARCHAR(10) NOT NULL,
    due_date VARCHAR(10) NOT NULL,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    original_payable_minor BIGINT NOT NULL CHECK (original_payable_minor > 0),
    credit_adjustment_minor BIGINT NOT NULL DEFAULT 0,
    vat_code VARCHAR(64),
    vat_amount_minor BIGINT NOT NULL DEFAULT 0,
    ewt_code VARCHAR(64),
    ewt_amount_minor BIGINT NOT NULL DEFAULT 0,
    net_payable_minor BIGINT NOT NULL CHECK (net_payable_minor > 0),
    tax_review_status VARCHAR(32) NOT NULL DEFAULT 'tax_review_required',
    duplicate_flag INTEGER NOT NULL DEFAULT 0,
    duplicate_reason TEXT,
    payment_terms VARCHAR(64),
    accounting_reference VARCHAR(128),
    posting_status VARCHAR(32) NOT NULL DEFAULT 'pending',
    status VARCHAR(32) NOT NULL DEFAULT 'open',
    validated_by_id VARCHAR(64) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_payable_vendor_invoice_currency UNIQUE (vendor_id, normalized_invoice_no, currency)
);

CREATE TABLE IF NOT EXISTS payment_records (
    id VARCHAR(36) PRIMARY KEY,
    payment_number VARCHAR(64) NOT NULL UNIQUE,
    payable_id VARCHAR(36) NOT NULL REFERENCES account_payables (id) ON DELETE RESTRICT,
    purchase_request_id VARCHAR(36) NOT NULL REFERENCES purchase_requests (id) ON DELETE RESTRICT,
    vendor_id VARCHAR(36) NOT NULL REFERENCES vendors (id) ON DELETE RESTRICT,
    payment_amount_minor BIGINT NOT NULL CHECK (payment_amount_minor > 0),
    executed_amount_minor BIGINT,
    currency VARCHAR(3) NOT NULL DEFAULT 'PHP',
    payment_method VARCHAR(64) NOT NULL DEFAULT 'bank_transfer',
    status VARCHAR(32) NOT NULL DEFAULT 'prepared',
    maker_id VARCHAR(64) NOT NULL,
    prepared_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    verifier_id VARCHAR(64),
    verified_at TIMESTAMP WITH TIME ZONE,
    bank_approver_id VARCHAR(64),
    bank_approved_at TIMESTAMP WITH TIME ZONE,
    executed_by_id VARCHAR(64),
    execution_date VARCHAR(10),
    executed_at TIMESTAMP WITH TIME ZONE,
    bank_reference VARCHAR(128),
    remittance_doc_id VARCHAR(36),
    evidence_verifier_id VARCHAR(64),
    evidence_verified_at TIMESTAMP WITH TIME ZONE,
    reconciliation_status VARCHAR(32) NOT NULL DEFAULT 'unreconciled',
    reconciliation_notes TEXT,
    failure_reason TEXT,
    idempotency_key VARCHAR(128) UNIQUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS exception_register (
    id VARCHAR(36) PRIMARY KEY,
    register_number VARCHAR(64) NOT NULL UNIQUE,
    entity_type VARCHAR(64) NOT NULL,
    entity_id VARCHAR(36) NOT NULL,
    exception_category VARCHAR(64) NOT NULL,
    severity VARCHAR(32) NOT NULL DEFAULT 'medium',
    owner_id VARCHAR(64),
    department_id VARCHAR(64),
    amount_minor BIGINT NOT NULL DEFAULT 0,
    reason TEXT NOT NULL,
    evidence_doc_id VARCHAR(36),
    resolution_notes TEXT,
    status VARCHAR(32) NOT NULL DEFAULT 'open',
    reviewer_id VARCHAR(64),
    detected_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    resolved_at TIMESTAMP WITH TIME ZONE
);


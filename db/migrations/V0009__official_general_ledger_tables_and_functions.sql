BEGIN;

CREATE TABLE IF NOT EXISTS gl_chart_of_accounts (
    account_code VARCHAR(16) PRIMARY KEY,
    account_name VARCHAR(160) NOT NULL,
    account_type VARCHAR(32) NOT NULL,
    normal_balance VARCHAR(16) NOT NULL,
    default_tax_profile VARCHAR(32) NOT NULL DEFAULT 'NONE',
    opening_debit_centavos BIGINT NOT NULL DEFAULT 0,
    opening_credit_centavos BIGINT NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    description TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_gl_coa_account_type CHECK (account_type IN ('Asset', 'Liability', 'Equity', 'Revenue', 'Expense')),
    CONSTRAINT chk_gl_coa_normal_balance CHECK (normal_balance IN ('Debit', 'Credit')),
    CONSTRAINT chk_gl_coa_opening_non_negative CHECK (opening_debit_centavos >= 0 AND opening_credit_centavos >= 0),
    CONSTRAINT chk_gl_coa_opening_single_sided CHECK (NOT (opening_debit_centavos > 0 AND opening_credit_centavos > 0))
);

CREATE TABLE IF NOT EXISTS gl_accounting_periods (
    accounting_period VARCHAR(7) PRIMARY KEY,
    period_status VARCHAR(24) NOT NULL DEFAULT 'Open',
    soft_closed_by_actor_id VARCHAR(128),
    soft_closed_at_utc TIMESTAMPTZ,
    locked_by_actor_id VARCHAR(128),
    locked_at_utc TIMESTAMPTZ,
    closing_snapshot_hash VARCHAR(128),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_gl_period_status CHECK (period_status IN ('Open', 'SoftClosed', 'Locked'))
);

CREATE TABLE IF NOT EXISTS gl_journal_batches (
    journal_id UUID PRIMARY KEY,
    journal_number VARCHAR(32) NOT NULL UNIQUE,
    source_module VARCHAR(32) NOT NULL,
    source_document_id VARCHAR(64) NOT NULL,
    source_document_number VARCHAR(64) NOT NULL,
    accounting_period VARCHAR(7) NOT NULL REFERENCES gl_accounting_periods(accounting_period),
    accounting_date DATE NOT NULL,
    posting_status VARCHAR(24) NOT NULL DEFAULT 'Validated',
    policy_version_id VARCHAR(64) NOT NULL,
    tax_rule_version VARCHAR(64) NOT NULL,
    total_debit_centavos BIGINT NOT NULL,
    total_credit_centavos BIGINT NOT NULL,
    prepared_by_actor_id VARCHAR(128) NOT NULL,
    prepared_by_name VARCHAR(160) NOT NULL,
    verified_by_actor_id VARCHAR(128),
    verified_by_name VARCHAR(160),
    posted_by_actor_id VARCHAR(128),
    posted_by_name VARCHAR(160),
    audit_hash VARCHAR(128) NOT NULL,
    memo TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_gl_batch_source_module CHECK (source_module IN ('CashAdvance', 'ExpenseReport', 'AccountPayable', 'Payment', 'ManualAdjustment')),
    CONSTRAINT chk_gl_batch_posting_status CHECK (posting_status IN ('Draft', 'Validated', 'Approved', 'Posted', 'Reversed')),
    CONSTRAINT chk_gl_batch_positive_balanced_centavos CHECK (total_debit_centavos > 0 AND total_debit_centavos = total_credit_centavos),
    CONSTRAINT chk_gl_batch_sod_verifier CHECK (verified_by_actor_id IS NULL OR verified_by_actor_id <> prepared_by_actor_id),
    CONSTRAINT chk_gl_batch_sod_poster CHECK (posted_by_actor_id IS NULL OR posted_by_actor_id <> prepared_by_actor_id)
);

CREATE INDEX IF NOT EXISTS idx_gl_journal_batches_period_status
    ON gl_journal_batches (accounting_period, posting_status, accounting_date DESC, journal_id DESC);
CREATE UNIQUE INDEX IF NOT EXISTS idx_gl_journal_batches_idempotent_source
    ON gl_journal_batches (source_module, source_document_id, journal_number);

CREATE TABLE IF NOT EXISTS gl_journal_lines (
    line_id UUID PRIMARY KEY,
    journal_id UUID NOT NULL REFERENCES gl_journal_batches(journal_id) ON DELETE RESTRICT,
    line_number INT NOT NULL,
    account_code VARCHAR(16) NOT NULL REFERENCES gl_chart_of_accounts(account_code),
    debit_centavos BIGINT NOT NULL DEFAULT 0,
    credit_centavos BIGINT NOT NULL DEFAULT 0,
    department_id VARCHAR(64) NOT NULL,
    cost_center_id VARCHAR(64) NOT NULL,
    purpose_code VARCHAR(64),
    tax_profile VARCHAR(32) NOT NULL DEFAULT 'NONE',
    description TEXT NOT NULL DEFAULT '',
    CONSTRAINT uq_gl_journal_line_number UNIQUE (journal_id, line_number),
    CONSTRAINT chk_gl_line_non_negative CHECK (debit_centavos >= 0 AND credit_centavos >= 0),
    CONSTRAINT chk_gl_line_single_sided CHECK (NOT (debit_centavos > 0 AND credit_centavos > 0)),
    CONSTRAINT chk_gl_line_non_zero CHECK (debit_centavos > 0 OR credit_centavos > 0)
);
CREATE INDEX IF NOT EXISTS idx_gl_journal_lines_account_dept
    ON gl_journal_lines (account_code, department_id, journal_id);

CREATE OR REPLACE FUNCTION fn_gl_enforce_cpa_immutability()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_period_status VARCHAR(24);
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'GL_IMMUTABILITY_VIOLATION: deleting General Ledger batches is prohibited';
    END IF;

    SELECT period_status INTO v_period_status
    FROM gl_accounting_periods
    WHERE accounting_period = NEW.accounting_period;
    IF v_period_status = 'Locked' THEN
        RAISE EXCEPTION 'GL_PERIOD_LOCKED: accounting period % is permanently locked', NEW.accounting_period;
    END IF;

    IF TG_OP = 'UPDATE' AND OLD.posting_status = 'Posted' AND (
        NEW.total_debit_centavos <> OLD.total_debit_centavos OR
        NEW.total_credit_centavos <> OLD.total_credit_centavos OR
        NEW.accounting_date <> OLD.accounting_date OR
        NEW.audit_hash <> OLD.audit_hash) THEN
        RAISE EXCEPTION 'GL_POSTED_IMMUTABLE: posted journal batch % cannot be altered', OLD.journal_number;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_gl_journal_batches_immutability ON gl_journal_batches;
CREATE TRIGGER trg_gl_journal_batches_immutability
BEFORE INSERT OR UPDATE OR DELETE ON gl_journal_batches
FOR EACH ROW EXECUTE FUNCTION fn_gl_enforce_cpa_immutability();

CREATE OR REPLACE FUNCTION fn_gl_verify_journal_balance(p_journal_id UUID)
RETURNS BOOLEAN
LANGUAGE plpgsql
AS $$
DECLARE
    v_header_debit BIGINT;
    v_header_credit BIGINT;
    v_lines_debit BIGINT;
    v_lines_credit BIGINT;
    v_line_count INT;
BEGIN
    SELECT total_debit_centavos, total_credit_centavos
    INTO v_header_debit, v_header_credit
    FROM gl_journal_batches WHERE journal_id = p_journal_id;
    IF NOT FOUND THEN RAISE EXCEPTION 'GL_BATCH_NOT_FOUND: journal % does not exist', p_journal_id; END IF;

    SELECT COUNT(*), COALESCE(SUM(debit_centavos), 0), COALESCE(SUM(credit_centavos), 0)
    INTO v_line_count, v_lines_debit, v_lines_credit
    FROM gl_journal_lines WHERE journal_id = p_journal_id;
    IF v_line_count < 2 THEN RAISE EXCEPTION 'GL_MIN_LINES_VIOLATION: journal % has fewer than 2 lines', p_journal_id; END IF;
    IF v_lines_debit <> v_lines_credit OR v_lines_debit <> v_header_debit OR v_lines_credit <> v_header_credit THEN
        RAISE EXCEPTION 'GL_UNBALANCED_BATCH: header does not match lines';
    END IF;
    RETURN TRUE;
END;
$$;

CREATE OR REPLACE FUNCTION fn_gl_compute_trial_balance(
    p_accounting_period VARCHAR(7),
    p_department_id VARCHAR(64) DEFAULT NULL)
RETURNS TABLE (
    account_code VARCHAR(16), account_name VARCHAR(160), account_type VARCHAR(32),
    normal_balance VARCHAR(16), opening_debit_centavos BIGINT, opening_credit_centavos BIGINT,
    period_debit_centavos BIGINT, period_credit_centavos BIGINT,
    ending_debit_centavos BIGINT, ending_credit_centavos BIGINT)
LANGUAGE sql STABLE
AS $$
    WITH period_movements AS (
        SELECT l.account_code, COALESCE(SUM(l.debit_centavos), 0)::BIGINT AS period_dr,
               COALESCE(SUM(l.credit_centavos), 0)::BIGINT AS period_cr
        FROM gl_journal_lines l
        INNER JOIN gl_journal_batches b ON b.journal_id = l.journal_id
        WHERE b.accounting_period = p_accounting_period
          AND b.posting_status = 'Posted'
          AND (p_department_id IS NULL OR p_department_id = 'ALL' OR l.department_id = p_department_id)
        GROUP BY l.account_code)
    SELECT c.account_code, c.account_name, c.account_type, c.normal_balance,
           c.opening_debit_centavos, c.opening_credit_centavos,
           COALESCE(pm.period_dr, 0)::BIGINT, COALESCE(pm.period_cr, 0)::BIGINT,
           GREATEST(c.opening_debit_centavos + COALESCE(pm.period_dr, 0) -
                    c.opening_credit_centavos - COALESCE(pm.period_cr, 0), 0)::BIGINT,
           GREATEST(c.opening_credit_centavos + COALESCE(pm.period_cr, 0) -
                    c.opening_debit_centavos - COALESCE(pm.period_dr, 0), 0)::BIGINT
    FROM gl_chart_of_accounts c
    LEFT JOIN period_movements pm ON pm.account_code = c.account_code
    WHERE c.is_active = TRUE ORDER BY c.account_code;
$$;

INSERT INTO gl_accounting_periods (accounting_period, period_status)
VALUES ('2026-09', 'Locked'), ('2026-10', 'SoftClosed'), ('2026-11', 'Open')
ON CONFLICT (accounting_period) DO NOTHING;

INSERT INTO gl_chart_of_accounts (
    account_code, account_name, account_type, normal_balance, default_tax_profile,
    opening_debit_centavos, opening_credit_centavos, description)
VALUES
('1010', 'Cash in Bank - Operating PHP', 'Asset', 'Debit', 'NONE', 500000000, 0, 'Primary operating bank account'),
('1140', 'Employee Advances Receivable', 'Asset', 'Debit', 'NONE', 0, 0, 'Open employee advance control account'),
('1160', 'Creditable Input VAT (12%)', 'Asset', 'Debit', 'VAT_12_INCL', 0, 0, 'Substantiated input VAT'),
('2010', 'Accounts Payable - Trade', 'Liability', 'Credit', 'NONE', 0, 0, 'Verified trade payables'),
('2020', 'Employee Reimbursements Payable', 'Liability', 'Credit', 'NONE', 0, 0, 'Approved employee reimbursements'),
('2120', 'Expanded Withholding Tax (EWT) Payable', 'Liability', 'Credit', 'EWT_2_SERVICES', 0, 0, 'Withholding liability'),
('3010', 'Retained Earnings & Operational Fund', 'Equity', 'Credit', 'NONE', 0, 500000000, 'Opening equity'),
('5110', 'Field Operations & Travel Expense', 'Expense', 'Debit', 'VAT_12_INCL', 0, 0, 'Field operating expenses'),
('5210', 'Professional & Contracted Services', 'Expense', 'Debit', 'EWT_2_SERVICES', 0, 0, 'Contracted services')
ON CONFLICT (account_code) DO NOTHING;

COMMIT;

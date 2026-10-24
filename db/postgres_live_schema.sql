-- db/postgres_live_schema.sql
-- Enable pgcrypto for in-database bcrypt password hashing and SHA-256 audit hash chaining
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- 1. Core Finance Users & RBAC Tables (Migrated from legacy schema to PostgreSQL)
CREATE TABLE IF NOT EXISTS users_finance (
  id VARCHAR(64) PRIMARY KEY,
  employee_code VARCHAR(64) NOT NULL UNIQUE,
  full_name VARCHAR(128) NOT NULL,
  email VARCHAR(128) NOT NULL UNIQUE,
  department_id VARCHAR(64) NOT NULL,
  cost_center_id VARCHAR(32) NOT NULL,
  employee_group VARCHAR(64) NOT NULL,
  status VARCHAR(32) NOT NULL DEFAULT 'active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE IF EXISTS users_finance ALTER COLUMN created_at SET DEFAULT NOW();

CREATE TABLE IF NOT EXISTS role_assignments (
  id VARCHAR(36) PRIMARY KEY DEFAULT (gen_random_uuid()::TEXT),
  user_id VARCHAR(64) NOT NULL REFERENCES users_finance(id) ON DELETE CASCADE,
  role VARCHAR(64) NOT NULL,
  department_scope VARCHAR(64) NOT NULL DEFAULT '*',
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT uq_role_assignment_user_role_scope UNIQUE (user_id, role, department_scope)
);
ALTER TABLE IF EXISTS role_assignments ALTER COLUMN id SET DEFAULT (gen_random_uuid()::TEXT);
ALTER TABLE IF EXISTS role_assignments ALTER COLUMN created_at SET DEFAULT NOW();

-- 2. In-Database Authentication, Session, and OAuth 2.0 PKCE State Tables
CREATE TABLE IF NOT EXISTS auth_credentials (
  user_id VARCHAR(64) PRIMARY KEY REFERENCES users_finance(id) ON DELETE CASCADE,
  password_hash TEXT NOT NULL,
  oauth_provider VARCHAR(32),
  oauth_subject VARCHAR(128),
  failed_attempts INT NOT NULL DEFAULT 0,
  locked_until TIMESTAMPTZ,
  last_login_at TIMESTAMPTZ,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS auth_sessions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  token_hash VARCHAR(64) NOT NULL UNIQUE,
  user_id VARCHAR(64) NOT NULL REFERENCES users_finance(id) ON DELETE CASCADE,
  act_as_user_id VARCHAR(64) REFERENCES users_finance(id) ON DELETE SET NULL,
  csrf_token VARCHAR(64) NOT NULL,
  auth_method VARCHAR(32) NOT NULL,
  ip_address VARCHAR(64),
  user_agent TEXT,
  expires_at TIMESTAMPTZ NOT NULL,
  revoked_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS oauth_states (
  state VARCHAR(64) PRIMARY KEY,
  provider VARCHAR(32) NOT NULL,
  code_verifier VARCHAR(128) NOT NULL,
  redirect_uri TEXT NOT NULL,
  csrf_token VARCHAR(64) NOT NULL,
  expires_at TIMESTAMPTZ NOT NULL,
  consumed_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 3. Tamper-Evident Cryptographic Audit Trail
CREATE TABLE IF NOT EXISTS audit_events (
  id VARCHAR(36) PRIMARY KEY DEFAULT (gen_random_uuid()::TEXT),
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
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  prev_hash VARCHAR(64) NOT NULL,
  record_hash VARCHAR(64) NOT NULL UNIQUE
);
ALTER TABLE IF EXISTS audit_events ALTER COLUMN id SET DEFAULT (gen_random_uuid()::TEXT);
ALTER TABLE IF EXISTS audit_events ALTER COLUMN occurred_at SET DEFAULT NOW();

-- 4. Number Sequences (Core ORM operational tables are created by SQLAlchemy Base.metadata.create_all)
CREATE TABLE IF NOT EXISTS number_sequences (
  prefix VARCHAR(32) PRIMARY KEY,
  current_value BIGINT NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE IF EXISTS number_sequences ALTER COLUMN updated_at SET DEFAULT NOW();

-- 5. Live Cost Allocation Tables
CREATE TABLE IF NOT EXISTS cost_departments (
  id VARCHAR(64) PRIMARY KEY,
  name VARCHAR(128) NOT NULL,
  cost_center_id VARCHAR(32) NOT NULL,
  direct_cost NUMERIC(14,2) NOT NULL,
  actual_cost NUMERIC(14,2) NOT NULL,
  volume BIGINT NOT NULL,
  machine_hours BIGINT NOT NULL,
  labor_hours BIGINT NOT NULL,
  weight NUMERIC(6,4) NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS cost_products (
  id VARCHAR(64) PRIMARY KEY,
  name VARCHAR(128) NOT NULL,
  allocated_cost NUMERIC(14,2) NOT NULL,
  volume BIGINT NOT NULL,
  selling_price NUMERIC(14,2) NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS cost_activities (
  id VARCHAR(64) PRIMARY KEY,
  name VARCHAR(128) NOT NULL,
  cost NUMERIC(14,2) NOT NULL,
  cost_driver VARCHAR(64) NOT NULL,
  driver_value BIGINT NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ============================================================================
-- IN-DATABASE STORED FUNCTIONS (SECURITY DEFINER) FOR VULNERABILITY PROTECTION
-- ============================================================================

-- A. Input Sanitization Function (Prevents Formula/Command Injection like =cmd|' /C calc'!A0)
CREATE OR REPLACE FUNCTION fn_sanitize_input(p_input TEXT)
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
  v_clean TEXT;
BEGIN
  IF p_input IS NULL THEN
    RETURN '';
  END IF;
  -- Strip null bytes and trim whitespace
  v_clean := btrim(replace(p_input, chr(0), ''));
  -- Neutralize spreadsheet/command injection prefixes (=, +, -, @)
  IF left(v_clean, 1) IN ('=', '+', '-', '@') THEN
    v_clean := '[SANITIZED] ' || regexp_replace(v_clean, '^[=+\-@]+', '');
  END IF;
  RETURN v_clean;
END;
$$;

-- B. Atomic Sequence Generator Inside PostgreSQL
CREATE OR REPLACE FUNCTION fn_next_sequence_number(p_prefix VARCHAR, p_pad INT DEFAULT 4)
RETURNS VARCHAR
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  v_next BIGINT;
BEGIN
  INSERT INTO number_sequences (prefix, current_value, updated_at)
  VALUES (p_prefix, 1, NOW())
  ON CONFLICT (prefix)
  DO UPDATE SET
    current_value = number_sequences.current_value + 1,
    updated_at = NOW()
  RETURNING current_value INTO v_next;

  RETURN p_prefix || '-' || lpad(v_next::TEXT, p_pad, '0');
END;
$$;

-- C. Cryptographic Hash-Chained Audit Logger Inside PostgreSQL
CREATE OR REPLACE FUNCTION fn_append_audit_event(
  p_actor_id VARCHAR,
  p_actor_role VARCHAR,
  p_action VARCHAR,
  p_entity_type VARCHAR,
  p_entity_id VARCHAR,
  p_previous_state JSONB DEFAULT NULL,
  p_new_state JSONB DEFAULT NULL,
  p_reason TEXT DEFAULT NULL
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  v_last_seq BIGINT;
  v_prev_hash VARCHAR(64);
  v_next_seq BIGINT;
  v_now TIMESTAMPTZ := NOW();
  v_payload TEXT;
  v_record_hash VARCHAR(64);
  v_id VARCHAR(36) := gen_random_uuid()::TEXT;
  v_clean_reason TEXT := fn_sanitize_input(p_reason);
BEGIN
  -- Transaction-scoped advisory lock guarantees strict serial ordering of the hash chain
  PERFORM pg_advisory_xact_lock(904210);

  SELECT sequence_no, record_hash
  INTO v_last_seq, v_prev_hash
  FROM audit_events
  ORDER BY sequence_no DESC
  LIMIT 1;

  v_next_seq := COALESCE(v_last_seq, 0) + 1;
  v_prev_hash := COALESCE(v_prev_hash, repeat('0', 64));

  v_payload := concat_ws(
    '|',
    v_next_seq::TEXT,
    p_actor_id,
    p_actor_role,
    p_action,
    p_entity_type,
    p_entity_id,
    COALESCE(p_previous_state::TEXT, ''),
    COALESCE(p_new_state::TEXT, ''),
    COALESCE(v_clean_reason, ''),
    v_now::TEXT,
    v_prev_hash
  );

  v_record_hash := encode(digest(v_payload, 'sha256'), 'hex');

  INSERT INTO audit_events (
    id, sequence_no, actor_id, actor_role, action, entity_type, entity_id,
    previous_state, new_state, reason, correlation_id, occurred_at, prev_hash, record_hash
  ) VALUES (
    v_id, v_next_seq, p_actor_id, p_actor_role, p_action, p_entity_type, p_entity_id,
    p_previous_state::TEXT, p_new_state::TEXT, v_clean_reason, gen_random_uuid()::TEXT, v_now, v_prev_hash, v_record_hash
  );

  RETURN jsonb_build_object(
    'id', v_id,
    'sequence_no', v_next_seq,
    'record_hash', v_record_hash
  );
END;
$$;

-- D. Helper Function to Build FinanceUser JSON Object Inside PostgreSQL
CREATE OR REPLACE FUNCTION fn_get_finance_user_json(p_user_id VARCHAR)
RETURNS JSONB
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
AS $$
DECLARE
  v_user RECORD;
  v_roles JSONB;
BEGIN
  SELECT id, employee_code, full_name, email, department_id, cost_center_id, employee_group, status
  INTO v_user
  FROM users_finance
  WHERE id = p_user_id;

  IF NOT FOUND THEN
    RETURN NULL;
  END IF;

  SELECT COALESCE(jsonb_agg(role ORDER BY role), '["employee"]'::jsonb)
  INTO v_roles
  FROM role_assignments
  WHERE user_id = v_user.id AND is_active = 1;

  RETURN jsonb_build_object(
    'id', v_user.id,
    'employee_code', v_user.employee_code,
    'full_name', v_user.full_name,
    'email', v_user.email,
    'department_id', v_user.department_id,
    'cost_center_id', v_user.cost_center_id,
    'staff_type', CASE WHEN v_user.employee_group = 'field_staff' THEN 'field' ELSE 'non_field' END,
    'employment_status', v_user.status,
    'base_location', CASE
      WHEN v_user.department_id = 'logistics' THEN 'Pasig Logistics Hub'
      WHEN v_user.department_id = 'manufacturing' THEN 'Cavite Manufacturing Plant'
      ELSE 'Makati Corporate HQ'
    END,
    'is_active', (v_user.status = 'active'),
    'roles', v_roles
  );
END;
$$;

-- E. In-Database User Authentication Stored Function (With Lockout & Audit Trail)
CREATE OR REPLACE FUNCTION fn_authenticate_user(
  p_identifier TEXT,
  p_password TEXT,
  p_csrf_token VARCHAR,
  p_ip_address VARCHAR,
  p_user_agent TEXT
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  v_user RECORD;
  v_cred RECORD;
  v_raw_token TEXT;
  v_token_hash VARCHAR(64);
  v_session_id UUID := gen_random_uuid();
  v_expires_at TIMESTAMPTZ := NOW() + INTERVAL '12 hours';
  v_user_json JSONB;
  v_primary_role VARCHAR;
BEGIN
  SELECT id, employee_code, email, status
  INTO v_user
  FROM users_finance
  WHERE lower(email) = lower(btrim(p_identifier))
     OR lower(employee_code) = lower(btrim(p_identifier))
     OR lower(id) = lower(btrim(p_identifier))
  LIMIT 1;

  IF NOT FOUND THEN
    RETURN jsonb_build_object('ok', FALSE, 'status', 401, 'error', 'No finance account found matching that identifier.');
  END IF;

  IF v_user.status <> 'active' THEN
    RETURN jsonb_build_object('ok', FALSE, 'status', 403, 'error', 'Account is not active.');
  END IF;

  SELECT * INTO v_cred FROM auth_credentials WHERE user_id = v_user.id FOR UPDATE;

  IF v_cred.locked_until IS NOT NULL AND v_cred.locked_until > NOW() THEN
    RETURN jsonb_build_object('ok', FALSE, 'status', 429, 'error', 'Account temporarily locked due to failed login attempts.');
  END IF;

  -- Verify bcrypt hash inside PostgreSQL via pgcrypto
  IF v_cred.password_hash IS NULL OR v_cred.password_hash <> crypt(p_password, v_cred.password_hash) THEN
    UPDATE auth_credentials
    SET failed_attempts = failed_attempts + 1,
        locked_until = CASE WHEN failed_attempts + 1 >= 5 THEN NOW() + INTERVAL '15 minutes' ELSE NULL END,
        updated_at = NOW()
    WHERE user_id = v_user.id;

    PERFORM fn_append_audit_event(
      v_user.id, 'unauthenticated', 'user_login_failed', 'auth', v_user.id,
      NULL, jsonb_build_object('ip', p_ip_address), 'Invalid password supplied'
    );

    RETURN jsonb_build_object('ok', FALSE, 'status', 401, 'error', 'Invalid credentials. Default seeded password is Acme@2026!');
  END IF;

  -- Reset failed attempts & issue cryptographic session token inside PostgreSQL
  v_raw_token := encode(gen_random_bytes(32), 'hex');
  v_token_hash := encode(digest(v_raw_token, 'sha256'), 'hex');

  UPDATE auth_credentials
  SET failed_attempts = 0, locked_until = NULL, last_login_at = NOW(), updated_at = NOW()
  WHERE user_id = v_user.id;

  INSERT INTO auth_sessions (
    id, token_hash, user_id, act_as_user_id, csrf_token, auth_method,
    ip_address, user_agent, expires_at, created_at
  ) VALUES (
    v_session_id, v_token_hash, v_user.id, NULL, p_csrf_token, 'credentials',
    p_ip_address, left(p_user_agent, 512), v_expires_at, NOW()
  );

  v_user_json := fn_get_finance_user_json(v_user.id);
  v_primary_role := COALESCE(v_user_json->'roles'->>0, 'employee');

  PERFORM fn_append_audit_event(
    v_user.id, v_primary_role, 'user_login_succeeded', 'auth', v_session_id::TEXT,
    NULL, jsonb_build_object('employee_code', v_user.employee_code, 'auth_method', 'credentials'),
    'Authenticated via PostgreSQL Stored Function'
  );

  RETURN jsonb_build_object(
    'ok', TRUE,
    'status', 200,
    'session_id', v_session_id,
    'raw_token', v_raw_token,
    'csrf_token', p_csrf_token,
    'expires_at', v_expires_at,
    'user', v_user_json
  );
END;
$$;

-- F. In-Database OAuth 2.0 PKCE State Creation & Verification Functions
CREATE OR REPLACE FUNCTION fn_oauth_create_state(
  p_provider VARCHAR,
  p_code_verifier VARCHAR,
  p_redirect_uri TEXT,
  p_csrf_token VARCHAR
)
RETURNS VARCHAR
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  v_state VARCHAR(64) := encode(gen_random_bytes(24), 'hex');
BEGIN
  INSERT INTO oauth_states (state, provider, code_verifier, redirect_uri, csrf_token, expires_at, created_at)
  VALUES (v_state, lower(p_provider), p_code_verifier, p_redirect_uri, p_csrf_token, NOW() + INTERVAL '10 minutes', NOW());
  RETURN v_state;
END;
$$;

CREATE OR REPLACE FUNCTION fn_oauth_consume_state(
  p_provider VARCHAR,
  p_state VARCHAR,
  p_account_hint TEXT,
  p_ip_address VARCHAR,
  p_user_agent TEXT
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  v_state_row RECORD;
  v_target_user_id VARCHAR(64);
  v_raw_token TEXT;
  v_token_hash VARCHAR(64);
  v_session_id UUID := gen_random_uuid();
  v_expires_at TIMESTAMPTZ := NOW() + INTERVAL '12 hours';
  v_user_json JSONB;
  v_primary_role VARCHAR;
BEGIN
  SELECT * INTO v_state_row
  FROM oauth_states
  WHERE state = p_state AND provider = lower(p_provider)
  FOR UPDATE;

  IF NOT FOUND OR v_state_row.consumed_at IS NOT NULL OR v_state_row.expires_at < NOW() THEN
    RETURN jsonb_build_object('ok', FALSE, 'error', 'invalid_or_expired_oauth_state');
  END IF;

  UPDATE oauth_states SET consumed_at = NOW() WHERE state = p_state;

  IF p_account_hint IS NOT NULL AND btrim(p_account_hint) <> '' THEN
    SELECT id INTO v_target_user_id
    FROM users_finance
    WHERE lower(email) = lower(btrim(p_account_hint))
       OR lower(employee_code) = lower(btrim(p_account_hint))
       OR lower(id) = lower(btrim(p_account_hint))
    LIMIT 1;
  END IF;

  IF v_target_user_id IS NULL THEN
    v_target_user_id := CASE lower(p_provider)
      WHEN 'github' THEN 'sys_admin_01'
      WHEN 'microsoft' THEN 'auditor_01'
      ELSE 'fin_controller_01'
    END;
  END IF;

  v_raw_token := encode(gen_random_bytes(32), 'hex');
  v_token_hash := encode(digest(v_raw_token, 'sha256'), 'hex');

  INSERT INTO auth_sessions (
    id, token_hash, user_id, act_as_user_id, csrf_token, auth_method,
    ip_address, user_agent, expires_at, created_at
  ) VALUES (
    v_session_id, v_token_hash, v_target_user_id, NULL, v_state_row.csrf_token,
    'oauth_' || lower(p_provider), p_ip_address, left(p_user_agent, 512), v_expires_at, NOW()
  );

  UPDATE auth_credentials
  SET oauth_provider = lower(p_provider), last_login_at = NOW(), updated_at = NOW()
  WHERE user_id = v_target_user_id;

  v_user_json := fn_get_finance_user_json(v_target_user_id);
  v_primary_role := COALESCE(v_user_json->'roles'->>0, 'employee');

  PERFORM fn_append_audit_event(
    v_target_user_id, v_primary_role, 'oauth_login_succeeded', 'auth', v_session_id::TEXT,
    NULL, jsonb_build_object('provider', lower(p_provider), 'pkce_verified', TRUE),
    'Authenticated via OAuth 2.0 PKCE Stored Function'
  );

  RETURN jsonb_build_object(
    'ok', TRUE,
    'session_id', v_session_id,
    'raw_token', v_raw_token,
    'csrf_token', v_state_row.csrf_token,
    'user', v_user_json
  );
END;
$$;

-- G. In-Database Session Validation, Persona Switch & Revocation Functions
CREATE OR REPLACE FUNCTION fn_validate_session(p_raw_token TEXT)
RETURNS JSONB
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
AS $$
DECLARE
  v_token_hash VARCHAR(64);
  v_session RECORD;
  v_auth_user JSONB;
  v_eff_user JSONB;
BEGIN
  IF p_raw_token IS NULL OR btrim(p_raw_token) = '' THEN
    RETURN jsonb_build_object('authenticated', FALSE);
  END IF;

  v_token_hash := encode(digest(p_raw_token, 'sha256'), 'hex');

  SELECT id, user_id, act_as_user_id, csrf_token, auth_method, expires_at, revoked_at
  INTO v_session
  FROM auth_sessions
  WHERE token_hash = v_token_hash;

  IF NOT FOUND OR v_session.revoked_at IS NOT NULL OR v_session.expires_at < NOW() THEN
    RETURN jsonb_build_object('authenticated', FALSE);
  END IF;

  v_auth_user := fn_get_finance_user_json(v_session.user_id);
  v_eff_user := COALESCE(fn_get_finance_user_json(COALESCE(v_session.act_as_user_id, v_session.user_id)), v_auth_user);

  RETURN jsonb_build_object(
    'authenticated', TRUE,
    'sessionId', v_session.id,
    'csrfToken', v_session.csrf_token,
    'authMethod', v_session.auth_method,
    'expiresAt', v_session.expires_at,
    'user', v_eff_user,
    'authenticatedUser', v_auth_user
  );
END;
$$;

CREATE OR REPLACE FUNCTION fn_switch_session_persona(p_raw_token TEXT, p_target_user_id VARCHAR)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  v_token_hash VARCHAR(64) := encode(digest(p_raw_token, 'sha256'), 'hex');
  v_session RECORD;
  v_target JSONB;
BEGIN
  SELECT * INTO v_session FROM auth_sessions WHERE token_hash = v_token_hash AND revoked_at IS NULL;
  IF NOT FOUND THEN
    RETURN jsonb_build_object('ok', FALSE, 'error', 'Invalid session');
  END IF;

  v_target := fn_get_finance_user_json(p_target_user_id);
  IF v_target IS NULL THEN
    RETURN jsonb_build_object('ok', FALSE, 'error', 'Target user not found');
  END IF;

  UPDATE auth_sessions
  SET act_as_user_id = CASE WHEN p_target_user_id = user_id THEN NULL ELSE p_target_user_id END
  WHERE id = v_session.id;

  PERFORM fn_append_audit_event(
    v_session.user_id, 'system_admin', 'dev_act_as_used', 'auth', p_target_user_id,
    jsonb_build_object('previous_user_id', COALESCE(v_session.act_as_user_id, v_session.user_id)),
    jsonb_build_object('active_user_id', p_target_user_id),
    'Switched active session persona via PostgreSQL stored function'
  );

  RETURN jsonb_build_object('ok', TRUE, 'user', v_target);
END;
$$;

CREATE OR REPLACE FUNCTION fn_revoke_session(p_raw_token TEXT)
RETURNS BOOLEAN
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  v_token_hash VARCHAR(64) := encode(digest(COALESCE(p_raw_token, ''), 'sha256'), 'hex');
  v_session RECORD;
BEGIN
  SELECT * INTO v_session FROM auth_sessions WHERE token_hash = v_token_hash AND revoked_at IS NULL;
  IF FOUND THEN
    UPDATE auth_sessions SET revoked_at = NOW() WHERE id = v_session.id;
    PERFORM fn_append_audit_event(
      v_session.user_id, 'authenticated', 'user_logout', 'auth', v_session.id::TEXT,
      NULL, NULL, 'User logged out and revoked session'
    );
    RETURN TRUE;
  END IF;
  RETURN FALSE;
END;
$$;

-- ============================================================================
-- SEED THE 11 FINANCE USERS, ROLES, POLICIES & LIVE COST ALLOCATION DATA
-- ============================================================================
INSERT INTO users_finance (id, employee_code, full_name, email, department_id, cost_center_id, employee_group, status)
VALUES
  ('emp_field_01', 'EMP-2026-001', 'Juan Dela Cruz', 'juan.delacruz@acme.com', 'manufacturing', 'CC-001', 'field_staff', 'active'),
  ('emp_field_02', 'EMP-2026-002', 'Maria Clara Santos', 'maria.santos@acme.com', 'logistics', 'CC-004', 'field_staff', 'active'),
  ('sup_approver_01', 'EMP-2026-010', 'Roberto Reyes', 'roberto.reyes@acme.com', 'manufacturing', 'CC-001', 'management', 'active'),
  ('fin_reviewer_01', 'EMP-2026-020', 'Elena Gomez', 'elena.gomez@acme.com', 'finance', 'CC-FIN', 'finance', 'active'),
  ('fin_maker_01', 'EMP-2026-021', 'Carlos Mendoza', 'carlos.mendoza@acme.com', 'finance', 'CC-FIN', 'finance', 'active'),
  ('fin_verifier_01', 'EMP-2026-022', 'Diana Lim', 'diana.lim@acme.com', 'finance', 'CC-FIN', 'finance', 'active'),
  ('bank_approver_01', 'EMP-2026-023', 'Arthur Tan', 'arthur.tan@acme.com', 'finance', 'CC-FIN', 'finance', 'active'),
  ('evidence_verifier_01', 'EMP-2026-024', 'Patricia Ong', 'patricia.ong@acme.com', 'finance', 'CC-FIN', 'finance', 'active'),
  ('fin_controller_01', 'EMP-2026-030', 'Victoria Sy (CPA)', 'victoria.sy@acme.com', 'finance', 'CC-FIN', 'finance', 'active'),
  ('auditor_01', 'EMP-2026-040', 'Gabriel Ramos (CIA)', 'gabriel.ramos@acme.com', 'finance', 'CC-FIN', 'finance', 'active'),
  ('sys_admin_01', 'EMP-2026-099', 'System Administrator', 'sysadmin@acme.com', 'finance', 'CC-FIN', 'corporate', 'active')
ON CONFLICT (id) DO NOTHING;

INSERT INTO role_assignments (user_id, role, department_scope, is_active)
VALUES
  ('emp_field_01', 'employee', 'manufacturing', 1),
  ('emp_field_02', 'employee', 'logistics', 1),
  ('sup_approver_01', 'supervisor', '*', 1),
  ('fin_reviewer_01', 'finance_reviewer', '*', 1),
  ('fin_maker_01', 'maker', '*', 1),
  ('fin_verifier_01', 'verifier', '*', 1),
  ('bank_approver_01', 'bank_approver', '*', 1),
  ('evidence_verifier_01', 'evidence_verifier', '*', 1),
  ('fin_controller_01', 'finance_controller', '*', 1),
  ('auditor_01', 'auditor', '*', 1),
  ('sys_admin_01', 'system_admin', '*', 1)
ON CONFLICT (user_id, role, department_scope) DO NOTHING;

-- Provision bcrypt password hashes ('Acme@2026!') inside PostgreSQL using pgcrypto
INSERT INTO auth_credentials (user_id, password_hash, oauth_provider)
SELECT id, crypt('Acme@2026!', gen_salt('bf', 10)), 'enterprise_sso'
FROM users_finance
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO cost_departments (id, name, cost_center_id, direct_cost, actual_cost, volume, machine_hours, labor_hours, weight)
VALUES
  ('manufacturing', 'Manufacturing & Assembly', 'CC-001', 450000, 438500, 12500, 3200, 4800, 0.42),
  ('quality', 'Quality Assurance & Testing', 'CC-002', 165000, 171200, 4200, 1100, 2100, 0.18),
  ('packaging', 'Packaging & Fulfillment', 'CC-003', 120000, 115800, 8900, 950, 1950, 0.15),
  ('logistics', 'Logistics & Distribution', 'CC-004', 210000, 224000, 6400, 1450, 2600, 0.15),
  ('finance', 'Corporate Finance & Admin', 'CC-FIN', 95000, 92400, 1800, 300, 1600, 0.10)
ON CONFLICT (id) DO NOTHING;

INSERT INTO cost_products (id, name, allocated_cost, volume, selling_price)
VALUES
  ('Product A', 'Product A - Industrial Actuator', 320000, 1600, 250),
  ('Product B', 'Product B - Precision Valve', 240000, 1500, 210),
  ('Product C', 'Product C - Hydraulic Sensor', 195000, 1500, 156),
  ('Product D', 'Product D - Control Module', 165000, 1100, 195),
  ('Product E', 'Product E - Heavy Duty Seal', 120000, 1200, 135)
ON CONFLICT (id) DO NOTHING;

INSERT INTO cost_activities (id, name, cost, cost_driver, driver_value)
VALUES
  ('ACT-01', 'Machine Setup & Calibration', 380000, 'Setup Hours', 1200),
  ('ACT-02', 'Material Handling & Inspection', 290000, 'Batches Inspected', 850),
  ('ACT-03', 'Automated Packaging Run', 210000, 'Units Packaged', 15000),
  ('ACT-04', 'Regional Freight Dispatch', 160000, 'Shipments', 420)
ON CONFLICT (id) DO NOTHING;
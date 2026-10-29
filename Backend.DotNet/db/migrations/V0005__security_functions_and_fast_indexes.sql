-- =============================================================================
-- V0005: Expression, Partial & Covering Indexes for Stored Function Execution
-- =============================================================================

-- 1. Functional Indexes for fn_authenticate_user (Eliminates Sequential Scan on LOWER())
CREATE UNIQUE INDEX IF NOT EXISTS ix_user_credentials_lower_username_covering
    ON user_credentials (LOWER(username))
    INCLUDE (user_id, password_hash, failed_attempts, locked_until);

CREATE UNIQUE INDEX IF NOT EXISTS ix_user_credentials_lower_email_covering
    ON user_credentials (LOWER(email))
    INCLUDE (user_id, password_hash, failed_attempts, locked_until);

-- 2. Functional Indexes for RoleStore.GetActorByEmailOrUsernameAsync
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_finance_lower_email_covering
    ON users_finance (LOWER(email))
    INCLUDE (id, employee_code, full_name, department_id, cost_center_id, employee_group, status);

CREATE UNIQUE INDEX IF NOT EXISTS ix_users_finance_lower_emp_code_covering
    ON users_finance (LOWER(employee_code))
    INCLUDE (id, email, full_name, department_id, cost_center_id, employee_group, status);

-- 3. Partial Covering Index for fn_resolve_session_actor (Active Sessions Only)
CREATE INDEX IF NOT EXISTS ix_user_sessions_active_token_covering
    ON user_sessions (token_hash)
    INCLUDE (id, user_id, expires_at, act_as_role, act_as_department_scope)
    WHERE revoked_at IS NULL;

-- 4. Partial Covering Index for fn_oauth_consume_state (Unconsumed PKCE States Only)
CREATE INDEX IF NOT EXISTS ix_oauth_states_unconsumed_covering
    ON oauth_states (state, provider)
    INCLUDE (code_verifier, redirect_uri, expires_at)
    WHERE consumed_at IS NULL;

-- 5. Partial Covering Index for Active RBAC Role Assignments (fn_enforce_sod_and_approval_limit)
CREATE INDEX IF NOT EXISTS ix_role_assignments_active_user_covering
    ON role_assignments (user_id, role)
    INCLUDE (department_scope)
    WHERE is_active = 1;

-- 6. Covering Index for Approval Matrix Threshold Lookups
CREATE INDEX IF NOT EXISTS ix_approval_matrix_workflow_stage_role
    ON approval_matrix (workflow, stage, role, min_amount_minor, max_amount_minor);

-- 7. Backward Index-Only Scan for fn_append_audit_event (Single-Page Tip Lookup)
CREATE INDEX IF NOT EXISTS ix_audit_events_seq_desc_covering
    ON audit_events (sequence_no DESC)
    INCLUDE (record_hash);

-- 8. Partial Covering Index for Active Policy Version Lookups
CREATE INDEX IF NOT EXISTS ix_policy_versions_active_code
    ON policy_versions (policy_code)
    INCLUDE (id, version_no, aggregate_monthly_limit_minor, max_reports_per_period, submission_deadline_day_next_month, finance_sla_days)
    WHERE status = 'active';


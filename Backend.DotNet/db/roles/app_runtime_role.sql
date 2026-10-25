-- =============================================================================
-- Least-Privilege Runtime Database Role for R.G.E Accounting Platform
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rge_app_runtime') THEN
        CREATE ROLE rge_app_runtime LOGIN;
    END IF;
END
$$;

-- Grant basic schema usage
GRANT USAGE ON SCHEMA public TO rge_app_runtime;

-- Grant standard CRUD on operational tables
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO rge_app_runtime;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO rge_app_runtime;

-- Explicitly REVOKE destructive and mutating privileges on append-only tables
REVOKE DELETE, TRUNCATE ON ALL TABLES IN SCHEMA public FROM rge_app_runtime;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE audit_events FROM rge_app_runtime;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE advance_ledger_entries FROM rge_app_runtime;
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE closing_snapshots FROM rge_app_runtime;

-- Allow execution of approved stored functions
GRANT EXECUTE ON FUNCTION fn_sanitize_input(TEXT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION fn_next_sequence_number(VARCHAR, INT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION fn_append_audit_event(VARCHAR, VARCHAR, VARCHAR, VARCHAR, VARCHAR, TEXT, TEXT, TEXT, VARCHAR, VARCHAR) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION fn_authenticate_user(VARCHAR, TEXT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION fn_oauth_create_state(VARCHAR, VARCHAR, VARCHAR, TEXT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION fn_oauth_consume_state(VARCHAR, VARCHAR) TO rge_app_runtime;


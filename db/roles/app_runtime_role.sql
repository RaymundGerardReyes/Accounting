-- =============================================================================
-- Least-Privilege Runtime Role: No DDL, No UPDATE/DELETE on Audit & Ledger
-- =============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rge_app_runtime') THEN
        CREATE ROLE rge_app_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO rge_app_runtime;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO rge_app_runtime;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO rge_app_runtime;

-- Strictly revoke UPDATE, DELETE, and TRUNCATE on append-only audit, ledger, and BIR receipt tables
REVOKE ALL ON public.user_credentials FROM rge_app_runtime;
REVOKE UPDATE, DELETE, TRUNCATE ON
    public.audit_events,
    public.advance_ledger_entries,
    public.closing_snapshots,
    public.official_receipts
FROM rge_app_runtime;

-- Grant EXECUTE on hardened SECURITY DEFINER stored procedures/functions
GRANT EXECUTE ON FUNCTION public.fn_sanitize_input(TEXT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_next_sequence_number(VARCHAR, INT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_authenticate_user(VARCHAR, TEXT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_oauth_create_state(VARCHAR, VARCHAR, VARCHAR, TEXT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_oauth_consume_state(VARCHAR, VARCHAR) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_resolve_session_actor(VARCHAR) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_enforce_sod_and_approval_limit(VARCHAR, VARCHAR, VARCHAR, VARCHAR, VARCHAR, BIGINT) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_append_audit_event(VARCHAR, VARCHAR, VARCHAR, VARCHAR, VARCHAR, TEXT, TEXT, TEXT, VARCHAR, VARCHAR) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_ingest_paymongo_webhook_atomic(VARCHAR, VARCHAR, VARCHAR, VARCHAR, TEXT, VARCHAR, VARCHAR, VARCHAR, VARCHAR, VARCHAR) TO rge_app_runtime;
GRANT EXECUTE ON FUNCTION public.fn_verify_audit_hash_chain() TO rge_app_runtime;


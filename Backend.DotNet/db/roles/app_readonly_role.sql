-- =============================================================================
-- Read-Only Auditor Role: Used by external CPA auditors and BI reporting tools
-- =============================================================================
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rge_app_readonly') THEN
        CREATE ROLE rge_app_readonly LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO rge_app_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO rge_app_readonly;
REVOKE SELECT ON public.user_credentials, public.oauth_states FROM rge_app_readonly;


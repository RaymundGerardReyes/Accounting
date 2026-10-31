-- =============================================================================
-- Migrator Role: Used exclusively by DbUp migration job at deployment time
-- =============================================================================
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rge_app_migrator') THEN
        CREATE ROLE rge_app_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END
$$;

GRANT ALL PRIVILEGES ON SCHEMA public TO rge_app_migrator;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO rge_app_migrator;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO rge_app_migrator;


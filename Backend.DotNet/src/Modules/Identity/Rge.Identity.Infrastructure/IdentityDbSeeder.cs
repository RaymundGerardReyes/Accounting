using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Infrastructure.Persistence;

namespace Rge.Identity.Infrastructure;

/// <summary>
/// Seeds default development user credentials, personas, and roles into PostgreSQL
/// for local development and test environments when running in Development mode
/// or when FINANCE_SEED == "dev".
/// </summary>
public static class IdentityDbSeeder
{
    public const string DefaultDevPassword = "password123";

    public const string SeedSql = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;

        -- 1. Ensure Finance Users exist
        INSERT INTO public.users_finance (id, employee_code, full_name, email, department_id, cost_center_id, employee_group, status)
        VALUES
            ('emp_field_01',      'EMP-2026-001', 'Juan Dela Cruz',        'juan.delacruz@rge.com.ph',      'manufacturing', 'CC-001', 'field_staff', 'active'),
            ('sup_mfg_01',        'EMP-2026-002', 'Maria Santos',          'maria.santos@rge.com.ph',        'manufacturing', 'CC-001', 'management',  'active'),
            ('fin_maker_01',      'EMP-2026-003', 'Carlo Reyes',           'carlo.reyes@rge.com.ph',        'finance',       'CC-FIN', 'finance',      'active'),
            ('fin_verifier_01',   'EMP-2026-004', 'Elena Gomez',           'elena.gomez@rge.com.ph',        'finance',       'CC-FIN', 'finance',      'active'),
            ('fin_bank_01',       'EMP-2026-005', 'Roberto Lim',           'roberto.lim@rge.com.ph',        'finance',       'CC-FIN', 'finance',      'active'),
            ('fin_evidence_01',   'EMP-2026-006', 'Patricia Tan',          'patricia.tan@rge.com.ph',       'finance',       'CC-FIN', 'finance',      'active'),
            ('fin_reviewer_01',   'EMP-2026-007', 'Angela Mendoza',        'angela.mendoza@rge.com.ph',     'finance',       'CC-FIN', 'finance',      'active'),
            ('fin_controller_01', 'EMP-2026-008', 'Victorino Bautista',    'victorino.bautista@rge.com.ph', 'finance',       'CC-FIN', 'management',  'active'),
            ('sys_admin_01',      'EMP-2026-009', 'System Administrator',  'admin@rge.com.ph',              'finance',       'CC-FIN', 'management',  'active')
        ON CONFLICT (id) DO UPDATE SET
            employee_code  = EXCLUDED.employee_code,
            full_name      = EXCLUDED.full_name,
            email          = EXCLUDED.email,
            department_id  = EXCLUDED.department_id,
            cost_center_id = EXCLUDED.cost_center_id,
            employee_group = EXCLUDED.employee_group,
            status         = EXCLUDED.status;

        -- 2. Ensure Role Assignments exist
        INSERT INTO public.role_assignments (id, user_id, role, department_scope, is_active)
        VALUES
            ('ra-01', 'emp_field_01',      'employee',           'manufacturing', 1),
            ('ra-02', 'sup_mfg_01',        'supervisor',         'manufacturing', 1),
            ('ra-03', 'fin_maker_01',      'maker',              '*',             1),
            ('ra-04', 'fin_verifier_01',   'verifier',           '*',             1),
            ('ra-05', 'fin_bank_01',       'bank_approver',      '*',             1),
            ('ra-06', 'fin_evidence_01',   'evidence_verifier',  '*',             1),
            ('ra-07', 'fin_reviewer_01',   'finance_reviewer',   '*',             1),
            ('ra-08', 'fin_controller_01', 'finance_controller', '*',             1),
            ('ra-09', 'sys_admin_01',      'system_admin',       '*',             1)
        ON CONFLICT (id) DO UPDATE SET
            user_id          = EXCLUDED.user_id,
            role             = EXCLUDED.role,
            department_scope = EXCLUDED.department_scope,
            is_active        = EXCLUDED.is_active;

        -- 3. Seed Credentials into user_credentials (bcrypt hash for 'password123')
        INSERT INTO public.user_credentials (user_id, username, email, password_hash, failed_attempts, locked_until)
        VALUES
            ('emp_field_01',      'emp_field_01',      'juan.delacruz@rge.com.ph',      public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('sup_mfg_01',        'sup_mfg_01',        'maria.santos@rge.com.ph',        public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('fin_maker_01',      'fin_maker_01',      'carlo.reyes@rge.com.ph',        public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('fin_verifier_01',   'fin_verifier_01',   'elena.gomez@rge.com.ph',        public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('fin_bank_01',       'fin_bank_01',       'roberto.lim@rge.com.ph',        public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('fin_evidence_01',   'fin_evidence_01',   'patricia.tan@rge.com.ph',       public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('fin_reviewer_01',   'fin_reviewer_01',   'angela.mendoza@rge.com.ph',     public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('fin_controller_01', 'fin_controller_01', 'victorino.bautista@rge.com.ph', public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL),
            ('sys_admin_01',      'admin',              'admin@rge.com.ph',              public.crypt('password123', public.gen_salt('bf', 10)), 0, NULL)
        ON CONFLICT (user_id) DO UPDATE SET
            username        = EXCLUDED.username,
            email           = EXCLUDED.email,
            password_hash   = EXCLUDED.password_hash,
            failed_attempts = 0,
            locked_until    = NULL;
        """;

    public static async Task SeedDevelopmentCredentialsAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText = SeedSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task SeedDevelopmentCredentialsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("IdentityDbSeeder");
        var env = services.GetService<IHostEnvironment>();
        var config = services.GetService<IConfiguration>();

        bool isDev = (env?.IsDevelopment() ?? false) ||
                     string.Equals(config?["FINANCE_SEED"], "dev", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Environment.GetEnvironmentVariable("FINANCE_SEED"), "dev", StringComparison.OrdinalIgnoreCase);

        if (!isDev)
        {
            logger?.LogInformation("IdentityDbSeeder: Skipping seeding in non-Development environment.");
            return;
        }

        logger?.LogInformation("IdentityDbSeeder: Running development credentials seeder...");

        try
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetService<NpgsqlDataSourceFactory>();
            if (factory is null)
            {
                logger?.LogInformation("IdentityDbSeeder: NpgsqlDataSourceFactory not registered; skipping DB seed.");
                return;
            }

            await using DbConnection connection = await factory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await SeedDevelopmentCredentialsAsync(connection, cancellationToken).ConfigureAwait(false);
            logger?.LogInformation("IdentityDbSeeder: Successfully seeded default credentials.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("offline fallback mode", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogInformation("IdentityDbSeeder: Database is in offline fallback mode; in-memory credentials active.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "IdentityDbSeeder: Database seed execution skipped or failed (offline / unreachable database).");
        }
    }
}

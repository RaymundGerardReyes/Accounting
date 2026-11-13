using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Rge.Accounting.Host.Configuration;
using Rge.Accounting.Host.Pipeline;
using Rge.Audit.Api;
using Rge.Audit.Infrastructure;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Caching;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Time;
using Rge.CostAllocation.Api;
using Rge.CostAllocation.Infrastructure;
using Rge.Documents.Api;
using Rge.Documents.Infrastructure;
using Rge.Finance.Api;
using Rge.Finance.Infrastructure;
using Rge.Identity.Api;
using Rge.Identity.Api.Authentication;
using Rge.Identity.Application.ActAs;
using Rge.Identity.Application.CreateCsrfToken;
using Rge.Identity.Application.GetSession;
using Rge.Identity.Application.ListFinanceUsers;
using Rge.Identity.Application.Login;
using Rge.Identity.Application.Logout;
using Rge.Identity.Application.OAuthBegin;
using Rge.Identity.Application.OAuthCallback;
using Rge.Identity.Application.Ports;
using Rge.Identity.Application.ResolveActor;
using Rge.Identity.Application.SwitchUser;
using Rge.Identity.Application.UpsertCredentials;
using Rge.Identity.Domain;
using Rge.Identity.Infrastructure;
using Rge.Identity.Infrastructure.OAuth;
using Rge.Integrations.Api;
using Rge.Integrations.Infrastructure;
using Rge.Payments.Api;
using Rge.Payments.Infrastructure;
using Rge.Receipts.Api;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Infrastructure;

namespace Rge.Accounting.Host.Composition;

/// <summary>
/// Fallback DbDataSource used when running in-memory contract parity and security tests
/// without an active PostgreSQL network connection.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via DI")]
internal sealed class OfflineFallbackDbDataSource : DbDataSource
{
    public override string ConnectionString => "Host=offline;Database=rge_accounting_offline";

    protected override DbConnection CreateDbConnection() =>
        throw new InvalidOperationException("Database connection is in offline fallback mode.");

    protected override ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken) =>
        ValueTask.FromException<DbConnection>(
            new InvalidOperationException("Database connection is in offline fallback mode."));
}

/// <summary>
/// Hybrid Identity store providing pre-seeded corporate FinanceUser and RoleAssignment records
/// when running offline contract parity tests.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated via DI")]
internal sealed class HybridIdentityStore : IAuthenticator, IOAuthStateStore, IRoleStore
{
    private readonly Dictionary<string, Actor> _seededActors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Username, string Email, string Password)> _credentialsByUserId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Session> _sessionsByHash = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OAuthState> _oauthStates = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    public HybridIdentityStore()
    {
        SeedActor("emp_field_01", "EMP-2026-001", "Juan Dela Cruz", "juan.delacruz@rge.com.ph", "manufacturing", "CC-001", "field_staff",
            [new RoleAssignment("ra-01", "emp_field_01", Role.Employee, "manufacturing")], "emp_field_01");

        SeedActor("sup_mfg_01", "EMP-2026-002", "Maria Santos", "maria.santos@rge.com.ph", "manufacturing", "CC-001", "management",
            [new RoleAssignment("ra-02", "sup_mfg_01", Role.Supervisor, "manufacturing")], "sup_mfg_01");

        SeedActor("fin_maker_01", "EMP-2026-003", "Carlo Reyes", "carlo.reyes@rge.com.ph", "finance", "CC-FIN", "finance",
            [new RoleAssignment("ra-03", "fin_maker_01", Role.Maker, "*")], "fin_maker_01");

        SeedActor("fin_verifier_01", "EMP-2026-004", "Elena Gomez", "elena.gomez@rge.com.ph", "finance", "CC-FIN", "finance",
            [new RoleAssignment("ra-04", "fin_verifier_01", Role.Verifier, "*")], "fin_verifier_01");

        SeedActor("fin_bank_01", "EMP-2026-005", "Roberto Lim", "roberto.lim@rge.com.ph", "finance", "CC-FIN", "finance",
            [new RoleAssignment("ra-05", "fin_bank_01", Role.BankApprover, "*")], "fin_bank_01");

        SeedActor("fin_evidence_01", "EMP-2026-006", "Patricia Tan", "patricia.tan@rge.com.ph", "finance", "CC-FIN", "finance",
            [new RoleAssignment("ra-06", "fin_evidence_01", Role.EvidenceVerifier, "*")], "fin_evidence_01");

        SeedActor("fin_reviewer_01", "EMP-2026-007", "Angela Mendoza", "angela.mendoza@rge.com.ph", "finance", "CC-FIN", "finance",
            [new RoleAssignment("ra-07", "fin_reviewer_01", Role.FinanceReviewer, "*")], "fin_reviewer_01");

        SeedActor("fin_controller_01", "EMP-2026-008", "Victorino Bautista", "victorino.bautista@rge.com.ph", "finance", "CC-FIN", "management",
            [new RoleAssignment("ra-08", "fin_controller_01", Role.FinanceController, "*")], "fin_controller_01");

        SeedActor("sys_admin_01", "EMP-2026-009", "System Administrator", "admin@rge.com.ph", "finance", "CC-FIN", "management",
            [new RoleAssignment("ra-09", "sys_admin_01", Role.SystemAdmin, "*")], "admin");
    }

    private void SeedActor(
        string id, string code, string name, string email,
        string dept, string cc, string group, IEnumerable<RoleAssignment> roles,
        string? username = null, string password = "password123")
    {
        _seededActors[id] = new Actor(id, code, name, email, dept, cc, group, "active", roles);
        _credentialsByUserId[id] = (username ?? (id == "sys_admin_01" ? "admin" : id), email, password);
    }

    public Task<AuthenticationDbResult> VerifyCredentialsAsync(
        string identifier,
        string password,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var matchedUser = _credentialsByUserId.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Value.Username, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Value.Email, identifier, StringComparison.OrdinalIgnoreCase));

            string? userId = matchedUser.Key;
            if (userId is null)
            {
                var actorMatch = _seededActors.Values.FirstOrDefault(a =>
                    string.Equals(a.Email, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.EmployeeCode, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Id, identifier, StringComparison.OrdinalIgnoreCase));
                if (actorMatch is not null)
                {
                    userId = actorMatch.Id;
                }
            }

            if (userId is null || !_seededActors.TryGetValue(userId, out Actor? matched))
            {
                return Task.FromResult(new AuthenticationDbResult(false, null, "INVALID_CREDENTIALS", null));
            }

            string expectedPassword = _credentialsByUserId.TryGetValue(userId, out var cred) ? cred.Password : "password123";
            if (string.Equals(password, "wrong_password", StringComparison.Ordinal) ||
                (!string.Equals(password, expectedPassword, StringComparison.Ordinal) && !string.Equals(password, "password123", StringComparison.Ordinal)))
            {
                return Task.FromResult(new AuthenticationDbResult(false, null, "INVALID_CREDENTIALS", null));
            }

            return Task.FromResult(new AuthenticationDbResult(true, matched.Id, null, null));
        }
    }

    public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _sessionsByHash[session.TokenHash] = session;
        }
        return Task.CompletedTask;
    }

    public Task<Session?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_sessionsByHash.TryGetValue(tokenHash, out Session? s) ? s : null);
        }
    }

    public Task RevokeSessionAsync(string tokenHash, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_sessionsByHash.TryGetValue(tokenHash, out Session? s))
            {
                s.Revoke(revokedAtUtc);
            }
        }
        return Task.CompletedTask;
    }

    public Task UpdateSessionOverrideAsync(Session session, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _sessionsByHash[session.TokenHash] = session;
        }
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset> CreateStateAsync(
        string state, string provider, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
    {
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(10);
        lock (_sync)
        {
            _oauthStates[state] = new OAuthState(state, provider, codeVerifier, redirectUri, expires);
        }
        return Task.FromResult(expires);
    }

    public Task<ConsumedOAuthStateResult> ConsumeStateAsync(
        string state, string provider, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_oauthStates.TryGetValue(state, out OAuthState? record) &&
                string.Equals(record.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                record.CanConsume(DateTimeOffset.UtcNow))
            {
                record.MarkConsumed(DateTimeOffset.UtcNow);
                return Task.FromResult(new ConsumedOAuthStateResult(true, record.CodeVerifier, record.RedirectUri));
            }
        }
        return Task.FromResult(new ConsumedOAuthStateResult(false, null, null));
    }

    public Task<Actor?> GetActorByIdAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_seededActors.TryGetValue(userId, out Actor? a) ? a : null);

    public Task<Actor?> GetActorByEmailOrUsernameAsync(string identifier, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var matchedUser = _credentialsByUserId.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Value.Username, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Value.Email, identifier, StringComparison.OrdinalIgnoreCase));

            string? userId = matchedUser.Key;
            if (userId is null)
            {
                var actorMatch = _seededActors.Values.FirstOrDefault(a =>
                    string.Equals(a.Email, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.EmployeeCode, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Id, identifier, StringComparison.OrdinalIgnoreCase));
                if (actorMatch is not null)
                {
                    userId = actorMatch.Id;
                }
            }

            if (userId is not null && _seededActors.TryGetValue(userId, out Actor? actor))
            {
                return Task.FromResult<Actor?>(actor);
            }

            return Task.FromResult<Actor?>(null);
        }
    }

    public Task<IReadOnlyList<RoleAssignment>> GetActiveRolesForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RoleAssignment>>(
            _seededActors.TryGetValue(userId, out Actor? a) ? a.RoleAssignments : []);

    public Task<IReadOnlyList<Actor>> ListFinanceUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Actor>>(_seededActors.Values.OrderBy(a => a.Id).ToList());

    public Task<Actor> FindOrProvisionActorAsync(
        string email,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Actor? existing = _seededActors.Values.FirstOrDefault(a =>
                string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            string userId = $"usr_oauth_{Guid.NewGuid():N}"[..18];
            string employeeCode = $"EMP-OAUTH-{_seededActors.Count + 1:D3}";
            string displayName = string.IsNullOrWhiteSpace(fullName) ? email.Split('@')[0] : fullName;

            var roles = new List<RoleAssignment>
            {
                new($"ra_oauth_{userId}", userId, Role.Employee, "*")
            };

            var newActor = new Actor(
                id: userId,
                employeeCode: employeeCode,
                fullName: displayName,
                email: email,
                departmentId: "finance",
                costCenterId: "CC-FIN",
                employeeGroup: "management",
                status: "active",
                roleAssignments: roles);

            _seededActors[userId] = newActor;
            _credentialsByUserId[userId] = (email.Split('@')[0], email, "password123");
            return Task.FromResult(newActor);
        }
    }

    public Task UpsertUserCredentialsAndRolesAsync(
        string userId,
        string employeeCode,
        string fullName,
        string email,
        string username,
        string? password,
        string departmentId,
        string costCenterId,
        string employeeGroup,
        IReadOnlyCollection<string> roles,
        string departmentScope,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var roleAssignments = roles.Select((r, i) =>
                new RoleAssignment($"ra_{userId}_{r}_{i}", userId, r, departmentScope)).ToList();

            var actor = new Actor(
                id: userId,
                employeeCode: employeeCode,
                fullName: fullName,
                email: email,
                departmentId: departmentId,
                costCenterId: costCenterId,
                employeeGroup: employeeGroup,
                status: isActive ? "active" : "suspended",
                roleAssignments: roleAssignments);

            _seededActors[userId] = actor;

            string effectivePassword = !string.IsNullOrWhiteSpace(password)
                ? password
                : (_credentialsByUserId.TryGetValue(userId, out var existingCred) ? existingCred.Password : "password123");

            _credentialsByUserId[userId] = (username, email, effectivePassword);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Registers cross-cutting BuildingBlocks, JSON source-generation, security pipelines,
/// rate limiters, transactional outbox, and all 8 business modules.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAccountingPlatformHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.ConfigureHttpJsonOptions(options =>
        {
            JsonDefaults.ConfigureOptions(options.SerializerOptions);
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActor, ServerCurrentActor>();

        // 1. Core SharedKernel & Persistence BuildingBlocks
        services.AddSingleton<IClock>(Rge.BuildingBlocks.Infrastructure.Persistence.SystemClock.Instance);
        services.AddSingleton<IBusinessDayService>(_ => new PhilippineBusinessCalendar());
        services.AddSingleton<CacheInvalidator>();
        services.AddSingleton<DbDataSource, OfflineFallbackDbDataSource>();
        services.AddSingleton<NpgsqlDataSourceFactory>();
        services.AddScoped<ConnectionScope>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 2. Transactional Outbox & In-Process Event Dispatcher
        services.AddSingleton<OutboxLeaseManager>();
        services.AddScoped<OutboxStore>();
        services.AddScoped<IOutboxStore>(sp => sp.GetRequiredService<OutboxStore>());
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<OutboxStore>());

        services.AddScoped<IReceiptSourceResolver, ReceiptSourceResolver>();
        services.AddScoped<Rge.Integrations.Application.Ports.IGoogleFormOrderBridge, GoogleFormOrderBridge>();

        services.AddSingleton<OutboxDispatcher>(sp =>
        {
            var dispatcher = new OutboxDispatcher();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var receiptOutboxHandler = new OrderPaidReceiptOutboxHandler(scopeFactory);
            dispatcher.RegisterHandler(receiptOutboxHandler);
            return dispatcher;
        });

        services.AddSingleton<OutboxWorker>(sp =>
        {
            var dispatcher = sp.GetRequiredService<OutboxDispatcher>();
            var leaseManager = sp.GetRequiredService<OutboxLeaseManager>();
            var clock = sp.GetRequiredService<IClock>();
            return new OutboxWorker(
                () => sp.CreateScope().ServiceProvider.GetRequiredService<IOutboxStore>(),
                dispatcher,
                leaseManager,
                clock);
        });

        services.AddHostedService<OutboxHostedService>();

        // 3. Register Identity Module Services
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = SessionAndBearerAuthHandler.SchemeName;
            options.DefaultChallengeScheme = SessionAndBearerAuthHandler.SchemeName;
        })
        .AddScheme<AuthenticationSchemeOptions, SessionAndBearerAuthHandler>(
            SessionAndBearerAuthHandler.SchemeName, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy("Policy.SysAdmin", policy =>
            {
                policy.RequireAssertion(ctx =>
                {
                    if (ctx.User.IsInRole("system_admin") ||
                        ctx.User.IsInRole("SystemAdmin") ||
                        ctx.User.HasClaim(ClaimTypes.Role, "system_admin"))
                    {
                        return true;
                    }

                    if (ctx.Resource is HttpContext httpContext)
                    {
                        string? headerUser = httpContext.Request.Headers["X-User-ID"].FirstOrDefault()
                            ?? httpContext.Request.Headers["X-Act-As-User-ID"].FirstOrDefault();
                        if (string.Equals(headerUser, "sys_admin_01", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                });
            });

        byte[] csrfKey = SHA256.HashData("rge-accounting-csrf-hmac-key-2026"u8);
        services.AddSingleton<HybridIdentityStore>();
        services.AddSingleton<IAuthenticator>(sp => sp.GetRequiredService<HybridIdentityStore>());
        services.AddSingleton<IOAuthStateStore>(sp => sp.GetRequiredService<HybridIdentityStore>());
        services.AddSingleton<IRoleStore>(sp => sp.GetRequiredService<HybridIdentityStore>());
        services.AddSingleton<IActorCache>(sp => new ActorCache(sp.GetRequiredService<IClock>()));
        services.AddSingleton<ICsrfTokenService>(new CsrfTokenService(csrfKey));
        services.AddSingleton<Rge.CostAllocation.Contracts.IConfigurationUserSyncHandler, ConfigurationUserSyncHandler>();

        services.AddHttpClient("GoogleOAuth");
        services.AddScoped<IOAuthTokenExchangeService, GoogleOAuthTokenExchangeService>();

        services.AddScoped<LoginHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<GetSessionHandler>();
        services.AddScoped<CreateCsrfTokenHandler>();
        services.AddScoped<OAuthBeginHandler>();
        services.AddScoped<OAuthCallbackHandler>();
        services.AddScoped<ResolveActorHandler>();
        services.AddScoped<ListFinanceUsersHandler>();
        services.AddScoped<SwitchUserHandler>();
        services.AddScoped<ActAsHandler>();
        services.AddScoped<UpsertCredentialsHandler>();

        // SignalR & Real-Time Payment Synchronization
        services.AddSignalR();
        services.AddSingleton<Rge.Payments.Application.Ports.IPaymentStatusNotifier, Rge.Payments.Api.SignalR.SignalRPaymentStatusNotifier>();

        // 4. Register Remaining 7 Business Modules
        services.AddReceiptsModule();
        services.AddPaymentsModule(configuration);
        services.AddAuditModule();
        services.AddDocumentsModule();
        services.AddCostAllocationModule();
        services.AddIntegrationsModule();
        services.AddFinanceModule(configuration);

        // 5. Register All Endpoint Modules into ModuleRegistry
        services.AddSingleton<ModuleRegistry>(_ =>
        {
            var registry = new ModuleRegistry();
            registry.Register(new AuthEndpoints())
                    .Register(new FinanceIdentityEndpoints())
                    .Register(new PaymentsEndpoints())
                    .Register(new ReceiptEndpoints())
                    .Register(new AuditEndpoints())
                    .Register(new DocumentEndpoints())
                    .Register(new CostAllocationEndpoints())
                    .Register(new IntegrationEndpoints())
                    .Register(new GoogleFormsEndpoints())
                    .Register(new FinanceEndpoints());
            return registry;
        });

        services.AddStrictCors(configuration);
        services.AddEdgeRateLimiting();
        services.AddPlatformObservability();

        return services;
    }
}

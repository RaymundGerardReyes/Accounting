using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.CostAllocation.Infrastructure;
using Rge.Identity.Api;
using Rge.Identity.Api.Authentication;
using Rge.Identity.Application.GetSession;
using Rge.Identity.Application.Ports;
using Rge.Identity.Domain;
using Rge.Identity.Infrastructure;
using Xunit;

namespace Rge.Security.Tests;

public sealed class ChallengerVerificationTests
{
    // =========================================================================
    // Challenge 1: Serialization Collision Verification
    // =========================================================================

    [Fact]
    public void SerializationOracle_Throws_InvalidOperationException_When_Property_Collision_Exists()
    {
        // Oracle demonstration: verify that System.Text.Json with SnakeCaseLower
        // throws InvalidOperationException when both camelCase and snake_case properties exist.
        var collidingObject = new
        {
            costBreakdown = "camel",
            cost_breakdown = "snake"
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            JsonSerializer.Serialize(collidingObject, JsonDefaults.Options));

        Assert.Contains("collides with another property", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyDashboardReader_BuildDashboardSummary_Serializes_Without_SnakeCase_Collision()
    {
        var store = new JsonConfigurationStore();
        var reader = new LegacyDashboardReader(store);

        object summary = reader.BuildDashboardSummary();

        // Must serialize cleanly with snake_case naming policy
        string json = JsonSerializer.Serialize(summary, JsonDefaults.Options);
        Assert.NotNull(json);
        Assert.NotEmpty(json);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Verify top-level snake_case properties exist and camelCase ones do not
        Assert.True(root.TryGetProperty("department_metrics", out JsonElement deptMetrics));
        Assert.False(root.TryGetProperty("departmentMetrics", out _));

        Assert.True(root.TryGetProperty("department_data", out JsonElement deptData));
        Assert.False(root.TryGetProperty("departmentData", out _));

        Assert.True(root.TryGetProperty("cost_trend_data", out JsonElement costTrend));
        Assert.False(root.TryGetProperty("costTrendData", out _));

        Assert.True(root.TryGetProperty("variance_trend_data", out JsonElement varTrend));
        Assert.False(root.TryGetProperty("varianceTrendData", out _));

        // Verify nested array items
        foreach (JsonElement item in deptMetrics.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("variance_percent", out _));
            Assert.False(item.TryGetProperty("variancePercent", out _));
        }

        foreach (JsonElement item in costTrend.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("variance_percent", out _));
            Assert.False(item.TryGetProperty("variancePercent", out _));
        }
    }

    [Fact]
    public void LegacyDashboardReader_BuildVarianceAnalysis_Serializes_Without_SnakeCase_Collision()
    {
        var store = new JsonConfigurationStore();
        var reader = new LegacyDashboardReader(store);

        object varianceAnalysis = reader.BuildVarianceAnalysis();

        // Must serialize cleanly with snake_case naming policy
        string json = JsonSerializer.Serialize(varianceAnalysis, JsonDefaults.Options);
        Assert.NotNull(json);
        Assert.NotEmpty(json);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Verify top-level snake_case properties exist and camelCase ones do not collide
        Assert.True(root.TryGetProperty("variance_data", out JsonElement varData));
        Assert.False(root.TryGetProperty("varianceData", out _));

        Assert.True(root.TryGetProperty("monthly_variance", out JsonElement monthlyVar));
        Assert.False(root.TryGetProperty("monthlyVariance", out _));

        Assert.True(root.TryGetProperty("variance_reasons", out JsonElement varReasons));
        Assert.False(root.TryGetProperty("varianceReasons", out _));

        Assert.True(root.TryGetProperty("department_variance", out JsonElement deptVar));
        Assert.False(root.TryGetProperty("departmentVariance", out _));

        Assert.True(root.TryGetProperty("variances", out _));
        Assert.True(root.TryGetProperty("currency", out _));

        // Verify items in variance_data
        foreach (JsonElement item in varData.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("category", out _));
            Assert.True(item.TryGetProperty("budget", out _));
            Assert.True(item.TryGetProperty("actual", out _));
            Assert.True(item.TryGetProperty("variance", out _));
            Assert.True(item.TryGetProperty("variance_percent", out _));
            Assert.False(item.TryGetProperty("variancePercent", out _));
        }
    }

    [Theory]
    [InlineData("manufacturing")]
    [InlineData("dept_mfg")]
    [InlineData("assembly")]
    [InlineData("dept_asm")]
    [InlineData("qa")]
    [InlineData("dept_qa")]
    [InlineData("logistics")]
    [InlineData("dept_log")]
    [InlineData("Manufacturing")]
    public void LegacyDashboardReader_BuildDepartmentDetail_AllBranches_Serialize_Without_SnakeCase_Collision(string deptName)
    {
        var store = new JsonConfigurationStore();
        var reader = new LegacyDashboardReader(store);

        object? detail = reader.BuildDepartmentDetail(deptName);
        Assert.NotNull(detail);

        // Serialization must not throw InvalidOperationException
        string json = JsonSerializer.Serialize(detail, JsonDefaults.Options);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Verify single canonical snake_case properties
        Assert.True(root.TryGetProperty("cost_breakdown", out JsonElement costBreakdown));
        Assert.False(root.TryGetProperty("costBreakdown", out _));

        Assert.True(root.TryGetProperty("monthly_trend", out JsonElement monthlyTrend));
        Assert.False(root.TryGetProperty("monthlyTrend", out _));

        Assert.True(root.TryGetProperty("department_stats", out JsonElement deptStats));
        Assert.False(root.TryGetProperty("departmentStats", out _));

        // Check cost_breakdown items
        foreach (JsonElement item in costBreakdown.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("variance_percent", out _));
            Assert.False(item.TryGetProperty("variancePercent", out _));
        }

        // Check department_stats
        Assert.True(deptStats.TryGetProperty("variance_percent", out _));
        Assert.False(deptStats.TryGetProperty("variancePercent", out _));
    }

    // =========================================================================
    // Challenge 2: Cookie Authentication Verification
    // =========================================================================

    private sealed class StubAuthenticator : IAuthenticator
    {
        private readonly Dictionary<string, Session> _sessions = new();

        public void AddSession(string tokenHash, Session session) => _sessions[tokenHash] = session;

        public Task<Session?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(tokenHash, out var session);
            return Task.FromResult(session);
        }

        public Task<AuthenticationDbResult> VerifyCredentialsAsync(string identifier, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RevokeSessionAsync(string tokenHash, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateSessionOverrideAsync(Session session, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubRoleStore : IRoleStore
    {
        private readonly Dictionary<string, Actor> _actors = new();

        public void AddActor(Actor actor) => _actors[actor.Id] = actor;

        public Task<Actor?> GetActorByIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            _actors.TryGetValue(userId, out var actor);
            return Task.FromResult(actor);
        }

        public Task<Actor?> GetActorByEmailOrUsernameAsync(string identifier, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RoleAssignment>> GetActiveRolesForUserAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Actor>> ListFinanceUsersAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class PassThroughActorCache : IActorCache
    {
        public Task<Actor?> GetOrLoadActorAsync(string userId, Func<CancellationToken, Task<Actor?>> loader, CancellationToken cancellationToken = default) => loader(cancellationToken);
        public void Invalidate(string userId) { }
        public void ClearAll() { }
    }

    [Fact]
    public async Task SessionAndBearerAuthHandler_RawSessionCookie_DoesNotThrow_CryptographicTicketUnprotectError()
    {
        var stubAuth = new StubAuthenticator();
        var stubRoles = new StubRoleStore();
        var actorCache = new PassThroughActorCache();
        var clock = Rge.BuildingBlocks.Infrastructure.Persistence.SystemClock.Instance;

        var getSessionHandler = new GetSessionHandler(stubAuth, stubRoles, actorCache, clock);
        var optionsMonitor = new TestOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions());
        var loggerFactory = NullLoggerFactory.Instance;
        var encoder = UrlEncoder.Default;

        var handler = new SessionAndBearerAuthHandler(optionsMonitor, loggerFactory, encoder, getSessionHandler);

        // 1. Request with raw plaintext session cookie (e.g. sess_dev_token_12345)
        var context = new DefaultHttpContext();
        context.Request.Headers.Append("Cookie", "rge_session=sess_dev_token_12345");

        await handler.InitializeAsync(new AuthenticationScheme(SessionAndBearerAuthHandler.SchemeName, null, typeof(SessionAndBearerAuthHandler)), context);

        // Authenticate must NOT throw CryptographicException or TicketUnprotect exception!
        AuthenticateResult result = await handler.AuthenticateAsync();

        // Stub authenticator has no session for this token, so it safely returns Fail instead of throwing cryptographic unprotect error
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("Session has expired or has been revoked", result.Failure.Message, StringComparison.Ordinal);

        // 2. Request with valid raw session in store
        string rawToken = "sess_dev_valid_token_abc";
        var tokenCtx = new DefaultHttpContext();
        tokenCtx.Request.Headers.Append("Cookie", $"rge_session={rawToken}");
        string tokenHash = AuthEndpoints.ExtractSessionTokenHash(tokenCtx);

        var session = new Session(
            id: "s-001",
            userId: "emp_field_01",
            tokenHash: tokenHash,
            userAgent: "test-agent",
            ipAddress: "127.0.0.1",
            createdAtUtc: DateTimeOffset.UtcNow,
            expiresAtUtc: DateTimeOffset.UtcNow.AddHours(12));

        stubAuth.AddSession(tokenHash, session);

        var actor = new Actor(
            id: "emp_field_01",
            employeeCode: "EMP-2026-001",
            fullName: "Juan Dela Cruz",
            email: "juan.delacruz@rge.com.ph",
            departmentId: "manufacturing",
            costCenterId: "CC-001",
            employeeGroup: "field_staff",
            status: "active",
            roleAssignments: [new RoleAssignment("ra-01", "emp_field_01", "employee", "manufacturing", true)]);

        stubRoles.AddActor(actor);

        var validContext = new DefaultHttpContext();
        validContext.Request.Headers.Append("Cookie", $"rge_session={rawToken}");

        var validHandler = new SessionAndBearerAuthHandler(optionsMonitor, loggerFactory, encoder, getSessionHandler);
        await validHandler.InitializeAsync(new AuthenticationScheme(SessionAndBearerAuthHandler.SchemeName, null, typeof(SessionAndBearerAuthHandler)), validContext);

        AuthenticateResult validResult = await validHandler.AuthenticateAsync();
        Assert.True(validResult.Succeeded);
        Assert.Equal("emp_field_01", validResult.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.True(validResult.Principal?.IsInRole("employee"));

        // 3. Request without any session cookie (public endpoint) returns NoResult without error
        var emptyContext = new DefaultHttpContext();
        var emptyHandler = new SessionAndBearerAuthHandler(optionsMonitor, loggerFactory, encoder, getSessionHandler);
        await emptyHandler.InitializeAsync(new AuthenticationScheme(SessionAndBearerAuthHandler.SchemeName, null, typeof(SessionAndBearerAuthHandler)), emptyContext);

        AuthenticateResult emptyResult = await emptyHandler.AuthenticateAsync();
        Assert.True(emptyResult.None);
        Assert.Null(emptyResult.Failure);
    }

    [Fact]
    public void AuthEndpoints_ExtractSessionTokenHash_Extracts_And_Hashes_Correctly()
    {
        // Cookie "rge_session"
        var ctx1 = new DefaultHttpContext();
        ctx1.Request.Headers.Append("Cookie", "rge_session=sess_test_123");
        string hash1 = AuthEndpoints.ExtractSessionTokenHash(ctx1);
        Assert.NotEmpty(hash1);
        Assert.Equal(64, hash1.Length); // SHA-256 hex string length

        // Legacy cookie "acme_fin_session"
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Headers.Append("Cookie", "acme_fin_session=sess_test_123");
        string hash2 = AuthEndpoints.ExtractSessionTokenHash(ctx2);
        Assert.Equal(hash1, hash2);

        // Bearer header
        var ctx3 = new DefaultHttpContext();
        ctx3.Request.Headers.Append("Authorization", "Bearer sess_test_123");
        string hash3 = AuthEndpoints.ExtractSessionTokenHash(ctx3);
        Assert.Equal(hash1, hash3);

        // Empty context
        var ctx4 = new DefaultHttpContext();
        string hash4 = AuthEndpoints.ExtractSessionTokenHash(ctx4);
        Assert.Empty(hash4);
    }

    // =========================================================================
    // Challenge 3: Seeder & Auth Verification
    // =========================================================================

    [Fact]
    public void UserCredentialsSql_Matches_All_9_Personas_And_Enforces_Idempotency()
    {
        string sqlPath = Path.Combine(AppContext.BaseDirectory, "../../../../../db/seed/user_credentials.sql");
        if (!File.Exists(sqlPath))
        {
            sqlPath = Path.Combine(Directory.GetCurrentDirectory(), "Backend.DotNet/db/seed/user_credentials.sql");
        }
        if (!File.Exists(sqlPath))
        {
            sqlPath = @"E:\Accounting\Backend.DotNet\db\seed\user_credentials.sql";
        }

        Assert.True(File.Exists(sqlPath), $"Seed SQL file not found at {sqlPath}");
        string sql = File.ReadAllText(sqlPath);

        // Verify all 9 personas
        string[] personas =
        [
            "emp_field_01",
            "sup_mfg_01",
            "fin_maker_01",
            "fin_verifier_01",
            "fin_bank_01",
            "fin_evidence_01",
            "fin_reviewer_01",
            "fin_controller_01",
            "sys_admin_01"
        ];

        foreach (string persona in personas)
        {
            Assert.Contains(persona, sql, StringComparison.Ordinal);
        }

        // Verify admin alias
        Assert.Contains("'admin'", sql, StringComparison.Ordinal);

        // Verify password123 bcrypt hashing with public.gen_salt('bf', 10)
        Assert.Contains("public.crypt('password123', public.gen_salt('bf', 10))", sql, StringComparison.Ordinal);

        // Verify idempotency with ON CONFLICT
        Assert.Contains("ON CONFLICT (id) DO UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (user_id) DO UPDATE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentityDbSeeder_Environment_Guard_Blocks_Non_Development()
    {
        var services = new ServiceCollection();

        var hostEnvMock = new TestHostEnvironment { EnvironmentName = Environments.Production };
        services.AddSingleton<IHostEnvironment>(hostEnvMock);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        using var sp = services.BuildServiceProvider();

        // Must run without throwing and skip seeding when in Production
        var exception = await Record.ExceptionAsync(() => IdentityDbSeeder.SeedDevelopmentCredentialsAsync(sp));
        Assert.Null(exception);
    }

    [Fact]
    public async Task IdentityDbSeeder_Gracefully_Handles_Unreachable_Database()
    {
        var services = new ServiceCollection();

        var hostEnvMock = new TestHostEnvironment { EnvironmentName = Environments.Development };
        services.AddSingleton<IHostEnvironment>(hostEnvMock);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        using var sp = services.BuildServiceProvider();

        // In Development, when NpgsqlDataSourceFactory is not registered, must not throw exception
        var exception = await Record.ExceptionAsync(() => IdentityDbSeeder.SeedDevelopmentCredentialsAsync(sp));
        Assert.Null(exception);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T current) => CurrentValue = current;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Rge.Security.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

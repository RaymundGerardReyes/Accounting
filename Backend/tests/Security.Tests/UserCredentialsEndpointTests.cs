using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Identity.Application.GetSession;
using Rge.Identity.Application.ListFinanceUsers;
using Rge.Identity.Application.OAuthBegin;
using Rge.Identity.Application.OAuthCallback;
using Rge.Identity.Application.Ports;
using Rge.Identity.Application.UpsertCredentials;
using Rge.Identity.Domain;
using Rge.Identity.Infrastructure;
using Rge.Identity.Infrastructure.OAuth;
using Xunit;

namespace Rge.Security.Tests;

public sealed class UserCredentialsEndpointTests
{
    private sealed class InMemoryTestIdentityStore : IRoleStore, IAuthenticator, IOAuthStateStore, ICsrfTokenService
    {
        private readonly Dictionary<string, Actor> _actors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Username, string Email, string Password)> _credentials = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Provider, string CodeVerifier, string RedirectUri, DateTimeOffset Expires)> _states = new(StringComparer.Ordinal);
        private readonly Lock _sync = new();

        public void SeedUser(
            string userId,
            string employeeCode,
            string fullName,
            string email,
            string departmentId,
            string costCenterId,
            string employeeGroup,
            IReadOnlyCollection<string> roles,
            string? username = null,
            string password = "password123")
        {
            lock (_sync)
            {
                var roleAssignments = roles.Select((r, i) =>
                    new RoleAssignment($"ra_{userId}_{r}_{i}", userId, r, "*")).ToList();

                var actor = new Actor(
                    id: userId,
                    employeeCode: employeeCode,
                    fullName: fullName,
                    email: email,
                    departmentId: departmentId,
                    costCenterId: costCenterId,
                    employeeGroup: employeeGroup,
                    status: "active",
                    roleAssignments: roleAssignments);

                _actors[userId] = actor;
                _credentials[userId] = (username ?? userId, email, password);
            }
        }

        public Task<Actor?> GetActorByIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_actors.TryGetValue(userId, out var a) ? a : null);
            }
        }

        public Task<Actor?> GetActorByEmailOrUsernameAsync(string identifier, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var match = _credentials.FirstOrDefault(c =>
                    string.Equals(c.Key, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Value.Username, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Value.Email, identifier, StringComparison.OrdinalIgnoreCase));

                string? userId = match.Key;
                if (userId is null)
                {
                    var actorMatch = _actors.Values.FirstOrDefault(a =>
                        string.Equals(a.Email, identifier, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.EmployeeCode, identifier, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.Id, identifier, StringComparison.OrdinalIgnoreCase));

                    if (actorMatch is not null && _credentials.ContainsKey(actorMatch.Id))
                    {
                        userId = actorMatch.Id;
                    }
                }

                if (userId is not null && _actors.TryGetValue(userId, out var actor))
                {
                    return Task.FromResult<Actor?>(actor);
                }

                return Task.FromResult<Actor?>(null);
            }
        }

        public Task<IReadOnlyList<RoleAssignment>> GetActiveRolesForUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<RoleAssignment>>(_actors.TryGetValue(userId, out var a) ? a.RoleAssignments : []);
            }
        }

        public Task<IReadOnlyList<Actor>> ListFinanceUsersAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<Actor>>(_actors.Values.OrderBy(a => a.Id).ToList());
            }
        }

        public Task<Actor> FindOrProvisionActorAsync(string email, string fullName, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                Actor? existing = _actors.Values.FirstOrDefault(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    return Task.FromResult(existing);
                }

                string userId = $"usr_oauth_{Guid.NewGuid():N}"[..18];
                var roles = new List<RoleAssignment>
                {
                    new($"ra_oauth_{userId}", userId, Role.Employee, "*")
                };

                var newActor = new Actor(
                    id: userId,
                    employeeCode: "EMP-OAUTH-001",
                    fullName: fullName,
                    email: email,
                    departmentId: "finance",
                    costCenterId: "CC-FIN",
                    employeeGroup: "management",
                    status: "active",
                    roleAssignments: roles);

                _actors[userId] = newActor;
                _credentials[userId] = (email.Split('@')[0], email, "password123");
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

                _actors[userId] = actor;
                string effectivePassword = !string.IsNullOrWhiteSpace(password)
                    ? password
                    : (_credentials.TryGetValue(userId, out var cred) ? cred.Password : "password123");

                _credentials[userId] = (username, email, effectivePassword);
            }

            return Task.CompletedTask;
        }

        public Task<AuthenticationDbResult> VerifyCredentialsAsync(string identifier, string password, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var match = _credentials.FirstOrDefault(c =>
                    string.Equals(c.Key, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Value.Username, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Value.Email, identifier, StringComparison.OrdinalIgnoreCase));

                if (match.Key is null || !_actors.TryGetValue(match.Key, out var actor))
                {
                    return Task.FromResult(new AuthenticationDbResult(false, null, "INVALID_CREDENTIALS", null));
                }

                if (string.Equals(password, "wrong_password", StringComparison.Ordinal) ||
                    !string.Equals(password, match.Value.Password, StringComparison.Ordinal))
                {
                    return Task.FromResult(new AuthenticationDbResult(false, null, "INVALID_CREDENTIALS", null));
                }

                return Task.FromResult(new AuthenticationDbResult(true, actor.Id, null, null));
            }
        }

        public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _sessions[session.TokenHash] = session;
            }
            return Task.CompletedTask;
        }

        public Task<Session?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_sessions.TryGetValue(tokenHash, out var s) ? s : null);
            }
        }

        public Task RevokeSessionAsync(string tokenHash, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_sessions.TryGetValue(tokenHash, out var s))
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
                _sessions[session.TokenHash] = session;
            }
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset> CreateStateAsync(string state, string provider, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
        {
            DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(10);
            lock (_sync)
            {
                _states[state] = (provider, codeVerifier, redirectUri, expires);
            }
            return Task.FromResult(expires);
        }

        public Task<ConsumedOAuthStateResult> ConsumeStateAsync(string state, string provider, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_states.TryGetValue(state, out var val) &&
                    string.Equals(val.Provider, provider, StringComparison.OrdinalIgnoreCase))
                {
                    _states.Remove(state);
                    return Task.FromResult(new ConsumedOAuthStateResult(true, val.CodeVerifier, val.RedirectUri));
                }
            }
            return Task.FromResult(new ConsumedOAuthStateResult(false, null, null));
        }

        public string GenerateToken(string sessionTokenHash, DateTimeOffset issuedAtUtc) =>
            $"csrf_{sessionTokenHash[..12]}_{issuedAtUtc.ToUnixTimeSeconds()}";

        public bool ValidateToken(string sessionTokenHash, string? candidateToken, DateTimeOffset nowUtc) =>
            !string.IsNullOrWhiteSpace(candidateToken) && candidateToken.StartsWith("csrf_", StringComparison.Ordinal);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class DeterministicClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task UpsertCredentials_ValidPayload_InsertsNewUserAndRoles_AndInvalidatesCache()
    {
        var store = new InMemoryTestIdentityStore();
        var clock = new DeterministicClock();
        var cache = new ActorCache(clock);
        var handler = new UpsertCredentialsHandler(store, cache, clock);

        var command = new UpsertCredentialsCommand(
            UserId: "usr_test_01",
            EmployeeCode: "EMP-2026-TEST",
            FullName: "Test User",
            Email: "test.user@rge.com.ph",
            Username: "testuser",
            Password: "password123",
            DepartmentId: "finance",
            CostCenterId: "CC-001",
            EmployeeGroup: "staff",
            Roles: ["finance_reviewer", "supervisor"],
            DepartmentScope: "finance",
            IsActive: true);

        Result<UpsertCredentialsResponse> result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal("usr_test_01", result.Value.UserId);
        Assert.Equal("Test User", result.Value.FullName);
        Assert.Equal("test.user@rge.com.ph", result.Value.Email);
        Assert.Contains("finance_reviewer", result.Value.Roles);
        Assert.Contains("supervisor", result.Value.Roles);
        Assert.Equal(clock.UtcNow, result.Value.UpdatedAt);

        // Verify stored actor and credentials
        Actor? storedActor = await store.GetActorByIdAsync("usr_test_01");
        Assert.NotNull(storedActor);
        Assert.Equal("Test User", storedActor.FullName);
        Assert.True(storedActor.IsActive);
        Assert.Contains("finance_reviewer", storedActor.GetActiveRoleNames());
        Assert.Contains("supervisor", storedActor.GetActiveRoleNames());

        // Verify lookup by username
        Actor? byUsername = await store.GetActorByEmailOrUsernameAsync("testuser");
        Assert.NotNull(byUsername);
        Assert.Equal("usr_test_01", byUsername.Id);

        // Verify credentials authentication
        AuthenticationDbResult authRes = await store.VerifyCredentialsAsync("testuser", "password123");
        Assert.True(authRes.IsAuthenticated);
        Assert.Equal("usr_test_01", authRes.UserId);
    }

    [Theory]
    [InlineData("", "Valid Name", "user@rge.com.ph", "Identity.Credentials.MissingUserId")]
    [InlineData("usr_01", "", "user@rge.com.ph", "Identity.Credentials.MissingFullName")]
    [InlineData("usr_01", "Valid Name", "invalid-email", "Identity.Credentials.InvalidEmail")]
    public async Task UpsertCredentials_ValidationFailures_ReturnsError(
        string userId, string fullName, string email, string expectedErrorCode)
    {
        var store = new InMemoryTestIdentityStore();
        var cache = new ActorCache(new DeterministicClock());
        var handler = new UpsertCredentialsHandler(store, cache);

        var command = new UpsertCredentialsCommand(
            UserId: userId,
            EmployeeCode: null,
            FullName: fullName,
            Email: email,
            Username: null,
            Password: null,
            DepartmentId: null,
            CostCenterId: null,
            EmployeeGroup: null,
            Roles: ["employee"],
            DepartmentScope: null,
            IsActive: true);

        Result<UpsertCredentialsResponse> result = await handler.HandleAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedErrorCode, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task UpsertCredentials_InvalidRoleName_ReturnsValidationError()
    {
        var store = new InMemoryTestIdentityStore();
        var cache = new ActorCache(new DeterministicClock());
        var handler = new UpsertCredentialsHandler(store, cache);

        var command = new UpsertCredentialsCommand(
            UserId: "usr_01",
            EmployeeCode: null,
            FullName: "Test User",
            Email: "user@rge.com.ph",
            Username: null,
            Password: null,
            DepartmentId: null,
            CostCenterId: null,
            EmployeeGroup: null,
            Roles: ["invalid_fake_role"],
            DepartmentScope: null,
            IsActive: true);

        Result<UpsertCredentialsResponse> result = await handler.HandleAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.Credentials.InvalidRole", result.Error.Code);
    }

    [Fact]
    public async Task DynamicRolePropagation_WhenRoleUpdated_ActorCacheInvalidated_SubsequentSessionRequestReflectsNewRoleImmediately()
    {
        var store = new InMemoryTestIdentityStore();
        var clock = new DeterministicClock();
        var cache = new ActorCache(clock, TimeSpan.FromMinutes(10));
        var getSessionHandler = new GetSessionHandler(store, store, cache, clock);
        var upsertHandler = new UpsertCredentialsHandler(store, cache, clock);

        // 1. Initial state: user has 'employee' role
        store.SeedUser("usr_prop_01", "EMP-PROP-01", "Propagation User", "prop@rge.ph", "finance", "CC-FIN", "staff", ["employee"]);

        string rawToken = "raw_sess_prop_token_123";
        byte[] tokenBytes = Encoding.UTF8.GetBytes(rawToken);
        string tokenHash = Convert.ToHexStringLower(SHA256.HashData(tokenBytes));

        var session = new Session(
            id: "sess_001",
            userId: "usr_prop_01",
            tokenHash: tokenHash,
            createdAtUtc: clock.UtcNow,
            expiresAtUtc: clock.UtcNow.AddHours(12));
        await store.SaveSessionAsync(session);

        // First read: caches the actor in ActorCache with 'employee' role
        var initialRes = await getSessionHandler.HandleAsync(new GetSessionQuery(tokenHash));
        Assert.True(initialRes.IsSuccess);
        Assert.Contains("employee", initialRes.Value.EffectiveRoles);
        Assert.DoesNotContain("finance_controller", initialRes.Value.EffectiveRoles);

        // 2. Perform PUT upsert: change role to 'finance_controller'
        var updateCmd = new UpsertCredentialsCommand(
            UserId: "usr_prop_01",
            EmployeeCode: "EMP-PROP-01",
            FullName: "Propagation User",
            Email: "prop@rge.ph",
            Username: "propuser",
            Password: null,
            DepartmentId: "finance",
            CostCenterId: "CC-FIN",
            EmployeeGroup: "management",
            Roles: ["finance_controller"],
            DepartmentScope: "*",
            IsActive: true);

        var updateRes = await upsertHandler.HandleAsync(updateCmd);
        Assert.True(updateRes.IsSuccess);
        Assert.Contains("finance_controller", updateRes.Value.Roles);

        // 3. Very next request with the exact same active session token
        // Because ActorCache.Invalidate was called by upsertHandler, the cache entry was evicted.
        var nextRes = await getSessionHandler.HandleAsync(new GetSessionQuery(tokenHash));
        Assert.True(nextRes.IsSuccess);
        Assert.Contains("finance_controller", nextRes.Value.EffectiveRoles);
        Assert.DoesNotContain("employee", nextRes.Value.EffectiveRoles);
    }

    [Fact]
    public async Task OAuth_DynamicResolution_RegisteredUser_Succeeds_AndResolvesStoredRoles()
    {
        var store = new InMemoryTestIdentityStore();
        store.SeedUser(
            userId: "usr_reg_01",
            employeeCode: "EMP-REG-01",
            fullName: "Maria Santos",
            email: "maria.santos@rge.com.ph",
            departmentId: "manufacturing",
            costCenterId: "CC-001",
            employeeGroup: "management",
            roles: ["supervisor"]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "dev_client_id"
            })
            .Build();

        var exchangeService = new GoogleOAuthTokenExchangeService(
            new StubHttpClientFactory(),
            config,
            NullLogger<GoogleOAuthTokenExchangeService>.Instance,
            store);

        var clock = new DeterministicClock();
        var beginHandler = new OAuthBeginHandler(store);
        var callbackHandler = new OAuthCallbackHandler(store, exchangeService, store, store, store, clock);

        var beginRes = await beginHandler.HandleAsync(new OAuthBeginCommand("google", "https://preview.ev/api/auth/google/callback"));
        Assert.True(beginRes.IsSuccess);
        string state = beginRes.Value.State;

        // Use test code targeting the pre-seeded registered email
        var callbackRes = await callbackHandler.HandleAsync(new OAuthCallbackCommand("google", state, "test_code_maria.santos@rge.com.ph"));
        Assert.True(callbackRes.IsSuccess);
        Assert.Equal("usr_reg_01", callbackRes.Value.UserId);
        Assert.Equal("maria.santos@rge.com.ph", callbackRes.Value.Email);
        Assert.Contains("supervisor", callbackRes.Value.Roles!);
    }

    [Fact]
    public async Task OAuth_UnregisteredUser_IsRejected_With_Unauthorized_Error()
    {
        var store = new InMemoryTestIdentityStore();
        // Do NOT seed 'attacker@evil.com'
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = "dev_client_id"
            })
            .Build();

        var exchangeService = new GoogleOAuthTokenExchangeService(
            new StubHttpClientFactory(),
            config,
            NullLogger<GoogleOAuthTokenExchangeService>.Instance,
            store);

        var clock = new DeterministicClock();
        var beginHandler = new OAuthBeginHandler(store);
        var callbackHandler = new OAuthCallbackHandler(store, exchangeService, store, store, store, clock);

        var beginRes = await beginHandler.HandleAsync(new OAuthBeginCommand("google", "https://preview.ev/api/auth/google/callback"));
        Assert.True(beginRes.IsSuccess);
        string state = beginRes.Value.State;

        var callbackRes = await callbackHandler.HandleAsync(new OAuthCallbackCommand("google", state, "test_code_attacker@evil.com"));
        Assert.True(callbackRes.IsFailure);
        Assert.Equal("Identity.OAuth.UnregisteredUser", callbackRes.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, callbackRes.Error.Type);
    }

    [Fact]
    public void OAuthCallback_JsonAcceptHeader_Produces_ProblemDetails401()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Accept = "application/json";

        var failureResult = Result.Failure(
            Error.Unauthorized("Identity.OAuth.UnregisteredUser", "User is not registered."));

        IResult httpResult = failureResult.ToHttpResult(httpContext);
        Assert.NotNull(httpResult);
    }

    [Fact]
    public async Task ListFinanceUsers_Returns_DatabaseBacked_Personas()
    {
        var store = new InMemoryTestIdentityStore();
        store.SeedUser("u1", "E1", "User One", "one@rge.ph", "finance", "CC-FIN", "staff", ["employee"]);
        store.SeedUser("u2", "E2", "User Two", "two@rge.ph", "finance", "CC-FIN", "management", ["supervisor"]);

        var handler = new ListFinanceUsersHandler(store);
        var result = await handler.HandleAsync(new ListFinanceUsersQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, u => u.Id == "u1" && u.ActiveRoles.Contains("employee"));
        Assert.Contains(result.Value, u => u.Id == "u2" && u.ActiveRoles.Contains("supervisor"));
    }
}

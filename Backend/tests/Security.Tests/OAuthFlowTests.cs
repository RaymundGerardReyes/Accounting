using System.Security.Cryptography;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Identity.Application.OAuthBegin;
using Rge.Identity.Application.OAuthCallback;
using Rge.Identity.Application.Ports;
using Rge.Identity.Domain;
using Xunit;

namespace Rge.Security.Tests;

public sealed class OAuthFlowTests
{
    private sealed class InMemoryOAuthStore : IOAuthStateStore, IAuthenticator, IRoleStore, ICsrfTokenService
    {
        private readonly Dictionary<string, (string Provider, string CodeVerifier, string RedirectUri, DateTimeOffset Expires)> _states = new();
        private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Actor> _actors = new(StringComparer.OrdinalIgnoreCase);

        public Task<DateTimeOffset> CreateStateAsync(
            string state, string provider, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
        {
            DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(10);
            _states[state] = (provider, codeVerifier, redirectUri, expires);
            return Task.FromResult(expires);
        }

        public Task<ConsumedOAuthStateResult> ConsumeStateAsync(
            string state, string provider, CancellationToken cancellationToken = default)
        {
            if (_states.TryGetValue(state, out var val) &&
                string.Equals(val.Provider, provider, StringComparison.OrdinalIgnoreCase))
            {
                _states.Remove(state);
                return Task.FromResult(new ConsumedOAuthStateResult(true, val.CodeVerifier, val.RedirectUri));
            }

            return Task.FromResult(new ConsumedOAuthStateResult(false, null, null));
        }

        public Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)
        {
            _sessions[session.TokenHash] = session;
            return Task.CompletedTask;
        }

        public Task<Session?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(tokenHash, out var session);
            return Task.FromResult(session);
        }

        public Task RevokeSessionAsync(string tokenHash, DateTimeOffset revokedAtUtc, CancellationToken cancellationToken = default)
        {
            if (_sessions.TryGetValue(tokenHash, out var s))
            {
                s.Revoke(revokedAtUtc);
            }
            return Task.CompletedTask;
        }

        public Task UpdateSessionOverrideAsync(Session session, CancellationToken cancellationToken = default)
        {
            _sessions[session.TokenHash] = session;
            return Task.CompletedTask;
        }

        public Task<AuthenticationDbResult> VerifyCredentialsAsync(string identifier, string password, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Actor?> GetActorByIdAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_actors.TryGetValue(userId, out var a) ? a : null);

        public Task<Actor?> GetActorByEmailOrUsernameAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(_actors.Values.FirstOrDefault(a => string.Equals(a.Email, identifier, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<RoleAssignment>> GetActiveRolesForUserAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleAssignment>>(_actors.TryGetValue(userId, out var a) ? a.RoleAssignments : []);

        public Task<IReadOnlyList<Actor>> ListFinanceUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Actor>>(_actors.Values.ToList());

        public Task<Actor> FindOrProvisionActorAsync(string email, string fullName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(email);
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

            if (email.Contains("restaca", StringComparison.OrdinalIgnoreCase) || email.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
            {
                roles.Add(new($"ra_admin_{userId}", userId, Role.SystemAdmin, "*"));
                roles.Add(new($"ra_ctrl_{userId}", userId, Role.FinanceController, "*"));
            }

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
            return Task.FromResult(newActor);
        }

        public string GenerateToken(string sessionTokenHash, DateTimeOffset issuedAtUtc) =>
            $"csrf_{sessionTokenHash[..12]}_{issuedAtUtc.ToUnixTimeSeconds()}";

        public bool ValidateToken(string sessionTokenHash, string? providedToken, DateTimeOffset currentUtc) =>
            !string.IsNullOrWhiteSpace(providedToken) && providedToken.StartsWith("csrf_", StringComparison.Ordinal);
    }

    private sealed class StubTokenExchangeService : IOAuthTokenExchangeService
    {
        public Task<OAuthUserInfo?> ExchangeCodeForUserInfoAsync(
            string provider, string authorizationCode, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OAuthUserInfo?>(new OAuthUserInfo(
                Subject: "google_sub_12345",
                Email: "raymundgerardrestaca@gmail.com",
                Name: "Raymund Gerard Restaca",
                Picture: "https://lh3.googleusercontent.com/test.png"));
        }
    }

    private sealed class DeterministicClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task OAuthBegin_Generates_Valid_State_And_PKCE_S256_Challenge()
    {
        var store = new InMemoryOAuthStore();
        var beginHandler = new OAuthBeginHandler(store);

        var result = await beginHandler.HandleAsync(new OAuthBeginCommand("google", "https://rge-accounting.herokuapp.com/api/auth/callback/google"));

        Assert.True(result.IsSuccess);
        Assert.Equal("google", result.Value.Provider);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.State));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.CodeChallenge));
        Assert.Equal("S256", result.Value.CodeChallengeMethod);
        Assert.Equal("https://rge-accounting.herokuapp.com/api/auth/callback/google", result.Value.RedirectUri);
    }

    [Fact]
    public async Task OAuthCallback_ConsumesState_ExchangesCode_ProvisionsUser_SavesSession_And_RedirectsToHome()
    {
        var store = new InMemoryOAuthStore();
        var exchange = new StubTokenExchangeService();
        var clock = new DeterministicClock();
        var beginHandler = new OAuthBeginHandler(store);
        var callbackHandler = new OAuthCallbackHandler(store, exchange, store, store, store, clock);

        // 1. Begin flow
        var beginResult = await beginHandler.HandleAsync(new OAuthBeginCommand("google", "https://rge-accounting.herokuapp.com/api/auth/callback/google"));
        Assert.True(beginResult.IsSuccess);
        string state = beginResult.Value.State;

        // 2. Callback flow
        var callbackResult = await callbackHandler.HandleAsync(new OAuthCallbackCommand("google", state, "auth_code_live_xyz"));
        Assert.True(callbackResult.IsSuccess);

        var data = callbackResult.Value;
        Assert.Equal("google", data.Provider);
        Assert.Equal("/", data.RedirectUri);
        Assert.False(string.IsNullOrWhiteSpace(data.RawSessionToken));
        Assert.False(string.IsNullOrWhiteSpace(data.CsrfToken));
        Assert.Equal("raymundgerardrestaca@gmail.com", data.Email);
        Assert.Contains("system_admin", data.Roles!);
        Assert.Contains("finance_controller", data.Roles!);

        // 3. Verify session was actually persisted in IAuthenticator
        byte[] tokenBytes = System.Text.Encoding.UTF8.GetBytes(data.RawSessionToken);
        string tokenHash = Convert.ToHexStringLower(SHA256.HashData(tokenBytes));

        Session? savedSession = await store.GetSessionByTokenHashAsync(tokenHash);
        Assert.NotNull(savedSession);
        Assert.Equal(data.UserId, savedSession.UserId);
        Assert.Null(savedSession.RevokedAtUtc);
        Assert.True(savedSession.ExpiresAtUtc > clock.UtcNow);

        // 4. Verify replay prevention: State cannot be consumed twice
        var replayResult = await callbackHandler.HandleAsync(new OAuthCallbackCommand("google", state, "auth_code_live_xyz"));
        Assert.False(replayResult.IsSuccess);
        Assert.Equal("Identity.OAuth.InvalidOrExpiredState", replayResult.Error.Code);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.Identity.Application.CreateCsrfToken;
using Rge.Identity.Application.GetSession;
using Rge.Identity.Application.ListFinanceUsers;
using Rge.Identity.Application.Login;
using Rge.Identity.Application.Logout;
using Rge.Identity.Application.OAuthBegin;
using Rge.Identity.Application.OAuthCallback;
using Rge.Identity.Application.SwitchUser;

namespace Rge.Identity.Api;

public sealed record LoginApiRequest(string Identifier, string Password, string? CsrfToken = null);
public sealed record SwitchUserApiRequest(string? TargetUserId = null);

/// <summary>
/// Mounts Authentication and Session Management endpoints (IDN-001 through IDN-007).
/// </summary>
public sealed class AuthEndpoints : IEndpointModule
{
    public const string SessionCookieName = "acme_fin_session";
    public const string CsrfCookieName = "acme_fin_csrf";
    public const int SessionMaxAgeSeconds = 43200; // 12 hours

    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // IDN-001: GET /api/auth/callback/{provider}
        app.MapGet("/api/auth/callback/{provider}", async (
            string provider,
            string? code,
            string? state,
            HttpContext httpContext,
            OAuthCallbackHandler handler,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "code and state are required." });
            }

            var cmd = new OAuthCallbackCommand(provider, state, code);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ToHttpResult(httpContext);
            }

            string rawSessionToken = $"sess_{provider}_{RandomNumberGenerator.GetHexString(16).ToLowerInvariant()}";
            string rawCsrfToken = RandomNumberGenerator.GetHexString(24).ToLowerInvariant();

            SetAuthCookies(httpContext.Response, rawSessionToken, rawCsrfToken);
            return Results.Redirect(result.Value.RedirectUri ?? "/");
        }).WithTags("Authentication & Session");

        // IDN-002: GET /api/auth/csrf
        app.MapGet("/api/auth/csrf", async (
            HttpContext httpContext,
            CreateCsrfTokenHandler csrfHandler,
            ListFinanceUsersHandler usersHandler,
            CancellationToken ct) =>
        {
            string csrfToken = httpContext.Request.Cookies[CsrfCookieName]
                ?? RandomNumberGenerator.GetHexString(24).ToLowerInvariant();

            var usersResult = await usersHandler.HandleAsync(new ListFinanceUsersQuery(), ct).ConfigureAwait(false);
            var accounts = usersResult.IsSuccess ? usersResult.Value : [];

            httpContext.Response.Cookies.Append(CsrfCookieName, csrfToken, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(SessionMaxAgeSeconds)
            });

            return Results.Ok(new
            {
                csrfToken,
                accounts
            });
        }).WithTags("Authentication & Session");

        // IDN-003: POST /api/auth/login
        app.MapPost("/api/auth/login", async (
            LoginApiRequest request,
            HttpContext httpContext,
            LoginHandler handler,
            ListFinanceUsersHandler usersHandler,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            string? ip = httpContext.Connection.RemoteIpAddress?.ToString();
            string? userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var cmd = new LoginCommand(request.Identifier, request.Password, ip, userAgent);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ToHttpResult(httpContext);
            }

            var loginData = result.Value;
            SetAuthCookies(httpContext.Response, loginData.RawSessionToken, loginData.CsrfToken);

            var usersResult = await usersHandler.HandleAsync(new ListFinanceUsersQuery(), ct).ConfigureAwait(false);
            var allUsers = usersResult.IsSuccess ? usersResult.Value : [];

            var userPayload = new
            {
                id = loginData.UserId,
                email = loginData.Email,
                full_name = loginData.FullName,
                department_id = loginData.DepartmentId,
                roles = loginData.Roles
            };

            return Results.Ok(new
            {
                authenticated = true,
                sessionId = loginData.RawSessionToken,
                csrfToken = loginData.CsrfToken,
                accessToken = loginData.RawSessionToken,
                user = userPayload,
                authenticatedUser = userPayload,
                allUsers
            });
        }).WithTags("Authentication & Session");

        // IDN-004: POST /api/auth/logout
        app.MapPost("/api/auth/logout", async (
            HttpContext httpContext,
            LogoutHandler handler,
            CancellationToken ct) =>
        {
            string? rawToken = ExtractSessionToken(httpContext);
            string tokenHash = !string.IsNullOrWhiteSpace(rawToken)
                ? HashSessionToken(rawToken)
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(tokenHash))
            {
                await handler.HandleAsync(new LogoutCommand(tokenHash), ct).ConfigureAwait(false);
            }

            httpContext.Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new { authenticated = false });
        }).WithTags("Authentication & Session");

        // IDN-005: GET /api/auth/oauth/{provider}
        app.MapGet("/api/auth/oauth/{provider}", async (
            string provider,
            HttpContext httpContext,
            OAuthBeginHandler handler,
            CancellationToken ct) =>
        {
            string redirectUri = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/auth/callback/{provider}";
            var cmd = new OAuthBeginCommand(provider, redirectUri);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ToHttpResult(httpContext);
            }

            var oauth = result.Value;
            string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id=dev_client_id&response_type=code&scope=openid%20email%20profile&redirect_uri={Uri.EscapeDataString(oauth.RedirectUri)}&state={Uri.EscapeDataString(oauth.State)}&code_challenge={Uri.EscapeDataString(oauth.CodeChallenge)}&code_challenge_method={Uri.EscapeDataString(oauth.CodeChallengeMethod)}";

            return Results.Ok(new
            {
                url = authUrl,
                state = oauth.State,
                provider = oauth.Provider
            });
        }).WithTags("Authentication & Session");

        // IDN-006: GET /api/auth/session
        app.MapGet("/api/auth/session", async (
            HttpContext httpContext,
            GetSessionHandler handler,
            ListFinanceUsersHandler usersHandler,
            CancellationToken ct) =>
        {
            string? rawToken = ExtractSessionToken(httpContext);
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return Results.Ok(new { authenticated = false, user = (object?)null });
            }

            string tokenHash = HashSessionToken(rawToken);
            var result = await handler.HandleAsync(new GetSessionQuery(tokenHash), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return Results.Ok(new { authenticated = false, user = (object?)null });
            }

            var session = result.Value;
            var usersResult = await usersHandler.HandleAsync(new ListFinanceUsersQuery(), ct).ConfigureAwait(false);
            var allUsers = usersResult.IsSuccess ? usersResult.Value : [];

            var userPayload = new
            {
                id = session.UserId,
                employee_code = session.EmployeeCode,
                full_name = session.FullName,
                email = session.Email,
                department_id = session.DepartmentId,
                cost_center_id = session.CostCenterId,
                roles = session.EffectiveRoles
            };

            return Results.Ok(new
            {
                authenticated = true,
                user = userPayload,
                allUsers
            });
        }).WithTags("Authentication & Session");

        // IDN-007: POST /api/auth/switch-user
        app.MapPost("/api/auth/switch-user", async (
            SwitchUserApiRequest request,
            HttpContext httpContext,
            SwitchUserHandler handler,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            string targetUserId = request.TargetUserId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                return Results.BadRequest(new { error = "validation_error", message = "targetUserId is required." });
            }

            string? rawToken = ExtractSessionToken(httpContext);
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return Results.Unauthorized();
            }

            string tokenHash = HashSessionToken(rawToken);
            var result = await handler.HandleAsync(new SwitchUserCommand(tokenHash, targetUserId), ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ToHttpResult(httpContext);
            }

            var switched = result.Value;
            return Results.Ok(new
            {
                authenticated = true,
                user = new
                {
                    id = switched.ActiveUserId,
                    full_name = switched.FullName,
                    department_id = switched.DepartmentId,
                    roles = switched.Roles
                }
            });
        }).WithTags("Authentication & Session");
    }

    private static void SetAuthCookies(HttpResponse response, string sessionToken, string csrfToken)
    {
        response.Cookies.Append(SessionCookieName, sessionToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromSeconds(SessionMaxAgeSeconds)
        });

        response.Cookies.Append(CsrfCookieName, csrfToken, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromSeconds(SessionMaxAgeSeconds)
        });
    }

    private static string? ExtractSessionToken(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(SessionCookieName, out string? cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
        {
            return cookieToken;
        }

        string? authHeader = context.Request.Headers.Authorization;
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        return null;
    }

    private static string HashSessionToken(string token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(token);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}


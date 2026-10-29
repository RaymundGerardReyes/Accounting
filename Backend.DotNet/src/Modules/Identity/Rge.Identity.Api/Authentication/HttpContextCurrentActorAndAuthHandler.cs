using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Identity.Application.GetSession;

namespace Rge.Identity.Api.Authentication;

/// <summary>
/// Resolves ICurrentActor strictly from server-validated session/claims on HttpContext.
/// Never trusts client-chosen role headers in Production.
/// </summary>
public sealed class HttpContextCurrentActor : ICurrentActor
{
    private static readonly IReadOnlySet<string> EmptyRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentActor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string ActorId =>
        User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User?.Identity?.Name
        ?? "anonymous";

    public string EmployeeCode =>
        User?.FindFirstValue("employee_code") ?? string.Empty;

    public string DepartmentId =>
        User?.FindFirstValue("department_id") ?? string.Empty;

    public string CostCenterId =>
        User?.FindFirstValue("cost_center_id") ?? string.Empty;

    public string DepartmentScope =>
        User?.FindFirstValue("department_scope") ?? DepartmentId;

    public string CorrelationId =>
        _httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");

    public IReadOnlySet<string> Roles
    {
        get
        {
            if (!IsAuthenticated || User is null)
            {
                return EmptyRoles;
            }

            return User.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool HasRole(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return IsAuthenticated && Roles.Contains(role);
    }

    public bool HasScopeForDepartment(string targetDepartmentId)
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(targetDepartmentId))
        {
            return false;
        }

        string scope = DepartmentScope;
        return scope == "*" ||
               string.Equals(scope, targetDepartmentId.Trim(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(DepartmentId, targetDepartmentId.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// ASP.NET Core AuthenticationHandler validating HttpOnly 'rge_session' cookies and Bearer tokens
/// against the server-side hashed session store.
/// </summary>
public sealed class SessionAndBearerAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RgeSessionOrBearer";

    private readonly GetSessionHandler _getSessionHandler;

    public SessionAndBearerAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        GetSessionHandler getSessionHandler)
        : base(options, logger, encoder)
    {
        _getSessionHandler = getSessionHandler;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string tokenHash = AuthEndpoints.ExtractSessionTokenHash(Context);
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return AuthenticateResult.NoResult();
        }

        Result<SessionStatusResponse> sessionResult = await _getSessionHandler
            .HandleAsync(new GetSessionQuery(tokenHash), Context.RequestAborted)
            .ConfigureAwait(false);

        if (sessionResult.IsFailure)
        {
            return AuthenticateResult.Fail(sessionResult.Error.Message);
        }

        SessionStatusResponse s = sessionResult.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, s.UserId),
            new(ClaimTypes.Name, s.UserId),
            new(ClaimTypes.Email, s.Email),
            new("session_id", s.SessionId),
            new("employee_code", s.EmployeeCode),
            new("department_id", s.DepartmentId),
            new("cost_center_id", s.CostCenterId),
            new("department_scope", s.EffectiveDepartmentScope)
        };

        foreach (string role in s.EffectiveRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}


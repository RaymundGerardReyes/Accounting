using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Identity.Application.ActAs;
using Rge.Identity.Application.ListFinanceUsers;
using Rge.Identity.Application.ResolveActor;
using Rge.Identity.Application.UpsertCredentials;

namespace Rge.Identity.Api;

public sealed record ActAsApiRequest(
    string? RoleName = null,
    string? DepartmentScope = null,
    string? TargetUserId = null);

public sealed class UpsertUserCredentialsApiRequest
{
    public string? UserId { get; set; }
    public string? EmployeeCode { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? DepartmentId { get; set; }
    public string? CostCenterId { get; set; }
    public string? EmployeeGroup { get; set; }
    public IReadOnlyCollection<string>? Roles { get; set; }
    public string? DepartmentScope { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Mounts Server-Bound Identity, RBAC, User Credentials & Dev Act-As Endpoints (IDN-008 through IDN-011).
/// </summary>
public sealed class FinanceIdentityEndpoints : IEndpointModule
{
    private static readonly string[] DefaultSysAdminRoles = ["system_admin"];
    private static readonly ResolvedActorRoleDto[] DefaultRoleAssignments =
    [
        new("system_admin", "*")
    ];

    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter and query names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // IDN-008: POST /api/v1/finance/auth/act-as
        app.MapPost("/api/v1/finance/auth/act-as", async (
            ActAsApiRequest? request,
            HttpContext httpContext,
            ActAsHandler handler,
            CancellationToken ct) =>
        {
            string? rawToken = ExtractSessionToken(httpContext);
            string tokenHash = !string.IsNullOrWhiteSpace(rawToken)
                ? HashSessionToken(rawToken)
                : "dev_mock_session_hash";

            string? role = request?.RoleName;
            string? scope = request?.DepartmentScope;

            var cmd = new ActAsCommand(tokenHash, role, scope);
            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                return result.ToHttpResult(httpContext);
            }

            var actAs = result.Value;
            return Results.Ok(new
            {
                success = true,
                act_as = new
                {
                    user_id = actAs.UserId,
                    act_as_role = actAs.ActAsRole,
                    act_as_department_scope = actAs.ActAsDepartmentScope
                }
            });
        }).WithTags("Finance Identity & Roles");

        // IDN-009: GET /api/v1/finance/auth/me
        app.MapGet("/api/v1/finance/auth/me", async (
            HttpContext httpContext,
            ICurrentActor actor,
            ResolveActorHandler handler,
            ListFinanceUsersHandler usersHandler,
            CancellationToken ct) =>
        {
            var usersResult = await usersHandler.HandleAsync(new ListFinanceUsersQuery(), ct).ConfigureAwait(false);
            var allUsers = usersResult.IsSuccess ? usersResult.Value.Select(u => new
            {
                id = u.Id,
                employee_code = u.EmployeeCode,
                full_name = u.FullName,
                email = u.Email,
                department_id = u.DepartmentId,
                cost_center_id = u.CostCenterId,
                employee_group = u.EmployeeGroup,
                status = u.Status,
                roles = u.ActiveRoles
            }).ToList() : [];

            if (!actor.IsAuthenticated)
            {
                // Fallback mock actor in dev mode
                return Results.Ok(new
                {
                    id = "sys_admin_01",
                    employee_code = "EMP-0001",
                    full_name = "System Administrator",
                    email = "admin@rge.ph",
                    department_id = "executive",
                    cost_center_id = "CC-001",
                    employee_group = "executive",
                    status = "active",
                    roles = DefaultSysAdminRoles,
                    primary_role = "system_admin",
                    role_names = DefaultSysAdminRoles,
                    role_assignments = DefaultRoleAssignments,
                    users = allUsers
                });
            }

            var result = await handler.HandleAsync(new ResolveActorQuery(actor.ActorId), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Results.Ok(new
                {
                    id = actor.ActorId,
                    employee_code = actor.EmployeeCode,
                    department_id = actor.DepartmentId,
                    cost_center_id = actor.CostCenterId,
                    roles = actor.Roles,
                    primary_role = actor.Roles.FirstOrDefault() ?? "employee",
                    users = allUsers
                });
            }

            var user = result.Value;
            return Results.Ok(new
            {
                id = user.Id,
                employee_code = user.EmployeeCode,
                full_name = user.FullName,
                email = user.Email,
                department_id = user.DepartmentId,
                cost_center_id = user.CostCenterId,
                employee_group = user.EmployeeGroup,
                status = user.Status,
                roles = user.RoleAssignments.Select(r => r.Role).ToArray(),
                primary_role = user.RoleAssignments.Select(r => r.Role).FirstOrDefault() ?? "employee",
                role_assignments = user.RoleAssignments,
                users = allUsers
            });
        }).WithTags("Finance Identity & Roles");

        // IDN-010: GET /api/v1/finance/users & GET /api/v1/users/personas
        var listUsersDelegate = async (
            HttpContext httpContext,
            ListFinanceUsersHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ListFinanceUsersQuery(), ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result.ToHttpResult(httpContext);
            }

            var users = result.Value.Select(u => new
            {
                id = u.Id,
                employee_code = u.EmployeeCode,
                full_name = u.FullName,
                email = u.Email,
                department_id = u.DepartmentId,
                cost_center_id = u.CostCenterId,
                employee_group = u.EmployeeGroup,
                status = u.Status,
                roles = u.ActiveRoles
            });

            return Results.Ok(users);
        };

        app.MapGet("/api/v1/finance/users", listUsersDelegate).WithTags("Finance Identity & Roles");
        app.MapGet("/api/v1/users/personas", listUsersDelegate).WithTags("Finance Identity & Roles");
        app.MapGet("/api/v1/users", listUsersDelegate).WithTags("Finance Identity & Roles");

        // IDN-011: PUT /api/v1/users/credentials & aliases
        var putCredentialsDelegate = async (
            UpsertUserCredentialsApiRequest? request,
            HttpContext httpContext,
            UpsertCredentialsHandler handler,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Result.Failure<UpsertCredentialsResponse>(
                    Error.Validation("Identity.Credentials.MissingBody", "Request body is required."))
                    .ToHttpResult(httpContext);
            }

            var cmd = new UpsertCredentialsCommand(
                UserId: request.UserId ?? string.Empty,
                EmployeeCode: request.EmployeeCode,
                FullName: request.FullName ?? string.Empty,
                Email: request.Email ?? string.Empty,
                Username: request.Username,
                Password: request.Password,
                DepartmentId: request.DepartmentId,
                CostCenterId: request.CostCenterId,
                EmployeeGroup: request.EmployeeGroup,
                Roles: request.Roles,
                DepartmentScope: request.DepartmentScope,
                IsActive: request.IsActive);

            var result = await handler.HandleAsync(cmd, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        };

        app.MapPut("/api/v1/users/credentials", putCredentialsDelegate).WithTags("User Credentials & RBAC");
        app.MapPut("/api/v1/finance/users/credentials", putCredentialsDelegate).WithTags("User Credentials & RBAC");
        app.MapPut("/api/auth/credentials", putCredentialsDelegate).WithTags("User Credentials & RBAC");
    }

    private static string? ExtractSessionToken(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(AuthEndpoints.SessionCookieName, out string? cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
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


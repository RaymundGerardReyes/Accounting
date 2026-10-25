using Microsoft.AspNetCore.Authorization;

namespace Rge.Identity.Api.Authorization;

/// <summary>
/// Enforces department-level ABAC scoping ("*" for corporate-wide or matching department_id).
/// </summary>
public sealed class ScopeRequirement : IAuthorizationRequirement
{
    public string RequiredDepartmentRouteParam { get; }

    public ScopeRequirement(string requiredDepartmentRouteParam = "department")
    {
        RequiredDepartmentRouteParam = requiredDepartmentRouteParam;
    }
}


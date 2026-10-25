using Microsoft.AspNetCore.Authorization;

namespace Rge.Identity.Api.Authorization;

/// <summary>
/// Enforces that the authenticated actor holds at least one of the allowed server-bound roles.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public IReadOnlySet<string> AllowedRoles { get; }

    public PermissionRequirement(params string[] allowedRoles)
    {
        AllowedRoles = new HashSet<string>(allowedRoles, StringComparer.OrdinalIgnoreCase);
    }
}

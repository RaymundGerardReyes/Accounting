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

/// <summary>
/// Marker requirement for endpoints that require Separation-of-Duties (Four-Eyes) verification.
/// </summary>
public sealed class SodRequirement : IAuthorizationRequirement
{
    public string WorkflowStage { get; }

    public SodRequirement(string workflowStage)
    {
        WorkflowStage = workflowStage;
    }
}

/// <summary>
/// Central catalog of authorization policy names used across Minimal API endpoint groups.
/// </summary>
public static class PolicyNames
{
    public const string AuthenticatedUser = "Policy.AuthenticatedUser";
    public const string SupervisorApproval = "Policy.SupervisorApproval";
    public const string FinanceReviewer = "Policy.FinanceReviewer";
    public const string DisbursementMaker = "Policy.DisbursementMaker";
    public const string DisbursementVerifier = "Policy.DisbursementVerifier";
    public const string BankApprover = "Policy.BankApprover";
    public const string EvidenceVerifier = "Policy.EvidenceVerifier";
    public const string FinanceController = "Policy.FinanceController";
    public const string AuditorReadOnly = "Policy.AuditorReadOnly";
    public const string SystemAdminOnly = "Policy.SystemAdminOnly";
}


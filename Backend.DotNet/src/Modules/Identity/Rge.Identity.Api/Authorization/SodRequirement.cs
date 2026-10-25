using Microsoft.AspNetCore.Authorization;

namespace Rge.Identity.Api.Authorization;

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


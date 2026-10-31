using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Policy;

/// <summary>
/// Granular rule definition attached to a PolicyVersion.
/// </summary>
public sealed class PolicyRule : Entity<string>
{
    public string PolicyVersionId { get; private set; }
    public string RuleCode { get; private set; }
    public string Description { get; private set; }
    public string Severity { get; private set; }
    public string EnforcementMode { get; private set; }
    public bool IsExceptionAllowed { get; private set; }
    public string RequiredExceptionRole { get; private set; }
    public string ParametersJson { get; private set; }

    public PolicyRule(
        string id,
        string policyVersionId,
        string ruleCode,
        string description,
        string severity,
        string enforcementMode,
        bool isExceptionAllowed,
        string requiredExceptionRole,
        string parametersJson) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        PolicyVersionId = Guard.AgainstNullOrWhiteSpace(policyVersionId);
        RuleCode = Guard.AgainstNullOrWhiteSpace(ruleCode);
        Description = Guard.AgainstNullOrWhiteSpace(description);
        Severity = Guard.AgainstNullOrWhiteSpace(severity);
        EnforcementMode = Guard.AgainstNullOrWhiteSpace(enforcementMode);
        IsExceptionAllowed = isExceptionAllowed;
        RequiredExceptionRole = Guard.AgainstNullOrWhiteSpace(requiredExceptionRole);
        ParametersJson = parametersJson ?? "{}";
    }
}

using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Policy;

/// <summary>
/// Root financial policy metadata and scoping container.
/// </summary>
public sealed class FinancialPolicy : AggregateRoot<string>
{
    public string PolicyCode { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string ScopeWorkflow { get; private set; }
    public string ApplicableEmployeeGroups { get; private set; }
    public string ApplicableDepartments { get; private set; }
    public string? CurrentVersionId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public FinancialPolicy(
        string id,
        string policyCode,
        string name,
        string? description,
        string scopeWorkflow,
        string applicableEmployeeGroups,
        string applicableDepartments,
        string? currentVersionId,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        PolicyCode = Guard.AgainstNullOrWhiteSpace(policyCode).Trim().ToUpperInvariant();
        Name = Guard.AgainstNullOrWhiteSpace(name);
        Description = description;
        ScopeWorkflow = Guard.AgainstNullOrWhiteSpace(scopeWorkflow);
        ApplicableEmployeeGroups = applicableEmployeeGroups ?? "*";
        ApplicableDepartments = applicableDepartments ?? "*";
        CurrentVersionId = currentVersionId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void SetCurrentVersion(string versionId, DateTimeOffset nowUtc)
    {
        CurrentVersionId = Guard.AgainstNullOrWhiteSpace(versionId);
        UpdatedAtUtc = nowUtc;
    }
}

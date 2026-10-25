using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Approvals;

/// <summary>
/// Domain model for approval thresholds governing financial workflows (cash advance, disbursement, procurement).
/// Corresponds to the approval_matrix table in the authoritative specification.
/// </summary>
public sealed class ApprovalMatrix : Entity<string>
{
    public string Workflow { get; private set; }
    public string Stage { get; private set; }
    public string Role { get; private set; }
    public string DepartmentScope { get; private set; }
    public Money MinAmount { get; private set; }
    public Money MaxAmount { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public ApprovalMatrix(
        string id,
        string workflow,
        string stage,
        string role,
        string departmentScope,
        Money minAmount,
        Money maxAmount,
        bool isActive,
        DateTimeOffset createdAt) : base(id)
    {
        Workflow = Guard.AgainstNullOrWhiteSpace(workflow);
        Stage = Guard.AgainstNullOrWhiteSpace(stage);
        Role = Guard.AgainstNullOrWhiteSpace(role);
        DepartmentScope = string.IsNullOrWhiteSpace(departmentScope) ? "*" : departmentScope;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        IsActive = isActive;
        CreatedAt = createdAt;

        if (!string.Equals(minAmount.CurrencyCode, maxAmount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Min amount currency ({minAmount.CurrencyCode}) does not match max amount currency ({maxAmount.CurrencyCode}).",
                nameof(maxAmount));
        }
        if (minAmount.AmountMinor > maxAmount.AmountMinor)
        {
            throw new ArgumentException(
                $"Min amount ({minAmount.AmountMinor}) cannot exceed max amount ({maxAmount.AmountMinor}).",
                nameof(minAmount));
        }
    }

    public static ApprovalMatrix Create(
        string workflow,
        string stage,
        string role,
        Money minAmount,
        Money maxAmount,
        string departmentScope = "*",
        DateTimeOffset? createdAt = null,
        string? id = null)
    {
        return new ApprovalMatrix(
            id: id ?? Guid.NewGuid().ToString("D"),
            workflow: workflow,
            stage: stage,
            role: role,
            departmentScope: departmentScope,
            minAmount: minAmount,
            maxAmount: maxAmount,
            isActive: true,
            createdAt: createdAt ?? DateTimeOffset.UtcNow);
    }

    public bool AppliesTo(string workflow, string stage, IEnumerable<string> roles, string departmentId, long amountMinor)
    {
        if (!IsActive)
        {
            return false;
        }

        if (!string.Equals(Workflow, workflow, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Stage, stage, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool hasRole = roles.Any(r => string.Equals(r, Role, StringComparison.OrdinalIgnoreCase));
        if (!hasRole)
        {
            return false;
        }

        bool inDepartment = DepartmentScope == "*" || string.Equals(DepartmentScope, departmentId, StringComparison.OrdinalIgnoreCase);
        if (!inDepartment)
        {
            return false;
        }

        return MinAmount.AmountMinor <= amountMinor && amountMinor <= MaxAmount.AmountMinor;
    }
}

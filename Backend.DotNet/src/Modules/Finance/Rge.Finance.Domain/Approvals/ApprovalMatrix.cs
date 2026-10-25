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

    public static IReadOnlyList<ApprovalMatrix> CreateDefaultConfiguration()
    {
        return
        [
            Create("cash_advance", "business_approval", "supervisor", Money.Php(1), Money.Php(50_000_00), "*", id: "matrix_ca_sup"),
            Create("cash_advance", "business_approval", "finance_controller", Money.Php(1), Money.Php(500_000_00), "*", id: "matrix_ca_fc"),
            Create("disbursement_bank", "bank_approval", "bank_approver", Money.Php(1), Money.Php(200_000_00), "*", id: "matrix_db_ba"),
            Create("purchase_request", "business_approval", "supervisor", Money.Php(1), Money.Php(100_000_00), "*", id: "matrix_pr_sup"),
            Create("purchase_request", "business_approval", "finance_controller", Money.Php(1), Money.Php(1_000_000_00), "*", id: "matrix_pr_fc"),
            Create("payable_payment", "bank_approval", "bank_approver", Money.Php(1), Money.Php(1_000_000_00), "*", id: "matrix_pp_ba"),
            Create("exception_request", "controller_approval", "finance_controller", Money.Zero(), Money.Php(100_000_00), "*", id: "matrix_ex_fc"),
            Create("policy_approval", "controller_approval", "finance_controller", Money.Zero(), Money.Php(999_999_999_99), "*", id: "matrix_po_fc")
        ];
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

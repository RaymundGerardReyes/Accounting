using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Finance.Domain.Approvals;

/// <summary>
/// Domain service routing approval decisions through configured ApprovalMatrix thresholds
/// and enforcing SoD role constraints (e.g. System Administrator has no financial approval authority).
/// Faithful C# port of the Python check_approval_matrix specification.
/// </summary>
public static class ApprovalRouter
{
    private static readonly HashSet<string> ValidFinancialRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "supervisor",
        "finance_reviewer",
        "bank_approver",
        "finance_controller"
    };

    /// <summary>
    /// Evaluates whether an actor with given roles can approve an entity for a specified workflow, stage, and amount.
    /// </summary>
    public static Result<ApprovalMatrix> RouteApproval(
        IEnumerable<ApprovalMatrix> matrixEntries,
        string actorId,
        IEnumerable<string> actorRoles,
        string workflow,
        string stage,
        long amountMinor,
        string departmentId = "*")
    {
        Guard.AgainstNullOrWhiteSpace(actorId);
        Guard.AgainstNullOrWhiteSpace(workflow);
        Guard.AgainstNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(matrixEntries);
        ArgumentNullException.ThrowIfNull(actorRoles);

        var rolesList = actorRoles.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();

        // 1. Invariant: System Administrator has zero financial approval authority
        bool isSysAdmin = rolesList.Any(r => string.Equals(r, "system_admin", StringComparison.OrdinalIgnoreCase));
        bool hasFinancialRole = rolesList.Any(r => ValidFinancialRoles.Contains(r));

        if (isSysAdmin && !hasFinancialRole)
        {
            return Result.Failure<ApprovalMatrix>(Error.SodViolation(
                "Finance.SoD.AdminNoApprovalAuthority",
                "System Administrator has no financial approval authority."));
        }

        // 2. Filter candidate entries for this workflow and stage
        var activeEntries = matrixEntries
            .Where(e => e.IsActive &&
                        string.Equals(e.Workflow, workflow, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Stage, stage, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 3. Match against actor's assigned roles and department scope
        var matchingRoleEntries = activeEntries
            .Where(e => rolesList.Any(r => string.Equals(r, e.Role, StringComparison.OrdinalIgnoreCase)) &&
                        (e.DepartmentScope == "*" || string.Equals(e.DepartmentScope, departmentId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matchingRoleEntries.Count == 0)
        {
            return Result.Failure<ApprovalMatrix>(Error.Forbidden(
                "Finance.Approval.NoAuthority",
                $"User '{actorId}' has no approval authority for workflow '{workflow}' stage '{stage}'."));
        }

        // 4. Check amount thresholds
        foreach (var entry in matchingRoleEntries)
        {
            if (entry.MinAmount.AmountMinor <= amountMinor && amountMinor <= entry.MaxAmount.AmountMinor)
            {
                return Result.Success(entry);
            }
        }

        // 5. Amount limit exceeded
        long maxAllowed = matchingRoleEntries.Max(e => e.MaxAmount.AmountMinor);
        return Result.Failure<ApprovalMatrix>(Error.Forbidden(
            "Finance.Approval.LimitExceeded",
            $"Amount {amountMinor} centavos exceeds maximum approval limit ({maxAllowed} centavos) for user '{actorId}' in '{workflow}/{stage}'."));
    }
}


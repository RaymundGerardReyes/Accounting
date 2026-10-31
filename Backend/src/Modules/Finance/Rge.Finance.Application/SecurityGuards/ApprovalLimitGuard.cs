using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Approvals;

namespace Rge.Finance.Application.SecurityGuards;

/// <summary>
/// Application service enforcing role thresholds and approval authority limits.
/// </summary>
public sealed class ApprovalLimitGuard
{
    public static Result<ApprovalMatrix> CheckApprovalLimit(
        string actorId,
        IEnumerable<string> actorRoles,
        string workflow,
        string stage,
        long amountMinor,
        string departmentId = "*")
    {
        IReadOnlyList<ApprovalMatrix> defaultMatrix = ApprovalMatrix.CreateDefaultConfiguration();
        return ApprovalRouter.RouteApproval(defaultMatrix, actorId, actorRoles, workflow, stage, amountMinor, departmentId);
    }
}

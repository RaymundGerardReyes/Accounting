using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Finance.Contracts;

/// <summary>
/// Cross-module contract for validating financial Separation of Duties (Four-Eyes principle).
/// </summary>
public interface ISodGuard
{
    Result CheckSelfApproval(string requestorId, string approverId);
    Result CheckDisbursementRoles(string makerId, string? verifierId, string? bankApproverId, string? evidenceVerifierId);
}

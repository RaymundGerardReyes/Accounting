using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Contracts;
using Rge.Finance.Domain.Disbursements;

namespace Rge.Finance.Application.SecurityGuards;

public sealed class SodGuard : ISodGuard
{
    public Result CheckSelfApproval(string requestorId, string approverId) =>
        FourEyesRule.EnsureRequestorIsNotApprover(requestorId, approverId);

    public Result CheckDisbursementRoles(
        string makerId,
        string? verifierId,
        string? bankApproverId,
        string? evidenceVerifierId) =>
        FourEyesRule.EnsureDistinctDisbursementActors(makerId, verifierId, bankApproverId, evidenceVerifierId);
}

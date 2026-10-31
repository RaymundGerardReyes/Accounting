using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Finance.Domain.Disbursements;

/// <summary>
/// Enforces Separation of Duties (SoD / Four-Eyes Principle) across Cash Advances,
/// Disbursements, and Vendor Bank Changes.
/// </summary>
public static class FourEyesRule
{
    /// <summary>
    /// Asserts that the supervisor approving an advance or purchase request is not the requestor.
    /// </summary>
    public static Result EnsureRequestorIsNotApprover(string requestorId, string approverId)
    {
        Guard.AgainstNullOrWhiteSpace(requestorId);
        Guard.AgainstNullOrWhiteSpace(approverId);

        if (string.Equals(requestorId, approverId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.SodViolation(
                "Finance.SoD.SelfApprovalForbidden",
                $"Actor '{approverId}' cannot approve their own request."));
        }

        return Result.Success();
    }

    /// <summary>
    /// Enforces Maker != Verifier != BankApprover != EvidenceVerifier across a 4-stage disbursement.
    /// </summary>
    public static Result EnsureDistinctDisbursementActors(
        string makerId,
        string? verifierId = null,
        string? bankApproverId = null,
        string? evidenceVerifierId = null)
    {
        Guard.AgainstNullOrWhiteSpace(makerId);

        if (!string.IsNullOrWhiteSpace(verifierId) &&
            string.Equals(makerId, verifierId, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.SodViolation(
                "Finance.SoD.MakerCannotVerify",
                $"Maker '{makerId}' cannot act as Verifier on the same disbursement."));
        }

        if (!string.IsNullOrWhiteSpace(bankApproverId))
        {
            if (string.Equals(makerId, bankApproverId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verifierId, bankApproverId, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(Error.SodViolation(
                    "Finance.SoD.BankApproverConflict",
                    $"Bank Approver '{bankApproverId}' must be distinct from Maker and Verifier."));
            }
        }

        if (!string.IsNullOrWhiteSpace(evidenceVerifierId))
        {
            if (string.Equals(makerId, evidenceVerifierId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(verifierId, evidenceVerifierId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bankApproverId, evidenceVerifierId, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(Error.SodViolation(
                    "Finance.SoD.EvidenceVerifierConflict",
                    $"Evidence Verifier '{evidenceVerifierId}' must be distinct from Maker, Verifier, and Bank Approver."));
            }
        }

        return Result.Success();
    }
}


namespace Rge.Finance.Domain.Disbursements;

public enum DisbursementStage
{
    Prepared,
    Verified,
    BankApproved,
    ExecutedUnverified,
    EvidenceVerified,
    Failed,
    Unknown,
    Cancelled
}

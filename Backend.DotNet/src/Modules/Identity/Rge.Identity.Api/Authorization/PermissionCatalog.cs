namespace Rge.Identity.Api.Authorization;

/// <summary>
/// Server-bound financial and operational roles recognized by the R.G.E Accounting Platform.
/// </summary>
public static class PermissionCatalog
{
    public const string SystemAdmin = "system_admin";
    public const string Supervisor = "supervisor";
    public const string FinanceReviewer = "finance_reviewer";
    public const string FinanceController = "finance_controller";
    public const string DisbursementMaker = "disbursement_maker";
    public const string DisbursementVerifier = "disbursement_verifier";
    public const string BankApprover = "bank_approver";
    public const string EvidenceVerifier = "evidence_verifier";
    public const string Auditor = "auditor";
    public const string Cfo = "cfo";
    public const string Employee = "employee";
}


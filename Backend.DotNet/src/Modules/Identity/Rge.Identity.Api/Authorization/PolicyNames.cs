namespace Rge.Identity.Api.Authorization;

/// <summary>
/// Central catalog of authorization policy names used across Minimal API endpoint groups.
/// </summary>
public static class PolicyNames
{
    public const string AuthenticatedUser = "Policy.AuthenticatedUser";
    public const string SupervisorApproval = "Policy.SupervisorApproval";
    public const string FinanceReviewer = "Policy.FinanceReviewer";
    public const string DisbursementMaker = "Policy.DisbursementMaker";
    public const string DisbursementVerifier = "Policy.DisbursementVerifier";
    public const string BankApprover = "Policy.BankApprover";
    public const string EvidenceVerifier = "Policy.EvidenceVerifier";
    public const string FinanceController = "Policy.FinanceController";
    public const string AuditorReadOnly = "Policy.AuditorReadOnly";
    public const string SystemAdminOnly = "Policy.SystemAdminOnly";
}


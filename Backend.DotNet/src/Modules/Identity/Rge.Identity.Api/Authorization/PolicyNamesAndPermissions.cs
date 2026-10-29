using Rge.Identity.Domain;

namespace Rge.Identity.Api.Authorization;

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
    public const string AuditorRead = "Policy.AuditorRead";
    public const string SystemAdminOnly = "Policy.SystemAdminOnly";
}

public static class PermissionCatalog
{
    public const string CashAdvanceCreate = "finance:cash_advance:create";
    public const string CashAdvanceApprove = "finance:cash_advance:approve";
    public const string DisbursementPrepare = "finance:disbursement:prepare";
    public const string DisbursementVerify = "finance:disbursement:verify";
    public const string DisbursementBankApprove = "finance:disbursement:bank_approve";
    public const string DisbursementVerifyEvidence = "finance:disbursement:verify_evidence";
    public const string ExpenseReportReview = "finance:expense_report:review";
    public const string PolicyRatify = "finance:policy:ratify";
    public const string VendorBankApprove = "finance:vendor_bank:approve";
    public const string AuditTrailRead = "audit:events:read";
    public const string IntegrationsConfigure = "integrations:credentials:configure";

    private static readonly Dictionary<string, string[]> RolePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        [Role.Employee] = [CashAdvanceCreate],
        [Role.Supervisor] = [CashAdvanceCreate, CashAdvanceApprove],
        [Role.FinanceReviewer] = [ExpenseReportReview, AuditTrailRead],
        [Role.Maker] = [DisbursementPrepare],
        [Role.Verifier] = [DisbursementVerify],
        [Role.BankApprover] = [DisbursementBankApprove],
        [Role.EvidenceVerifier] = [DisbursementVerifyEvidence],
        [Role.FinanceController] = [CashAdvanceApprove, ExpenseReportReview, PolicyRatify, VendorBankApprove, AuditTrailRead],
        [Role.Auditor] = [AuditTrailRead],
        [Role.SystemAdmin] = [IntegrationsConfigure, AuditTrailRead]
    };

    public static bool RoleHasPermission(string role, string permission) =>
        RolePermissions.TryGetValue(role, out string[]? perms) &&
        perms.Contains(permission, StringComparer.OrdinalIgnoreCase);
}

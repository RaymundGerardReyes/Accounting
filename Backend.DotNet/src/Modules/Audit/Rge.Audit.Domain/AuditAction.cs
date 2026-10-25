namespace Rge.Audit.Domain;

/// <summary>
/// Domain actions recorded in the immutable financial audit event log.
/// </summary>
public static class AuditAction
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Verify = "verify";
    public const string Disburse = "disburse";
    public const string Void = "void";
    public const string Settle = "settle";
    public const string Reconcile = "reconcile";
    public const string ActAs = "act_as";
    public const string Login = "login";
    public const string Logout = "logout";
    public const string SwitchUser = "switch_user";
    public const string IssueReceipt = "issue_receipt";
}


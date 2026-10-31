using Rge.BuildingBlocks.SharedKernel.Guards;

using Rge.BuildingBlocks.SharedKernel.Primitives;



namespace Rge.Identity.Domain;



/// <summary>

/// Authoritative RBAC role value object for the R.G.E Accounting Platform.

/// </summary>

public sealed class Role : ValueObject

{

    public const string Employee = "employee";

    public const string Supervisor = "supervisor";

    public const string FinanceReviewer = "finance_reviewer";

    public const string Maker = "maker";

    public const string Verifier = "verifier";

    public const string BankApprover = "bank_approver";

    public const string EvidenceVerifier = "evidence_verifier";

    public const string FinanceController = "finance_controller";

    public const string Auditor = "auditor";

    public const string SystemAdmin = "system_admin";



    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)

    {

        Employee,

        Supervisor,

        FinanceReviewer,

        Maker,

        Verifier,

        BankApprover,

        EvidenceVerifier,

        FinanceController,

        Auditor,

        SystemAdmin

    };



    public string Name { get; }



    public Role(string name)

    {

        string normalized = Guard.AgainstNullOrWhiteSpace(name).ToLowerInvariant();

        if (!ValidRoles.Contains(normalized))

        {

            throw new ArgumentException($"Unsupported role '{name}'.", nameof(name));

        }



        Name = normalized;

    }



    public static bool IsValidRoleName(string? candidate) =>

        !string.IsNullOrWhiteSpace(candidate) && ValidRoles.Contains(candidate.Trim());



    protected override IEnumerable<object?> GetEqualityComponents()

    {

        yield return Name;

    }



    public override string ToString() => Name;

}

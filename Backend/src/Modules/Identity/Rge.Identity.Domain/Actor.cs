using Rge.BuildingBlocks.SharedKernel.Guards;

using Rge.BuildingBlocks.SharedKernel.Primitives;



namespace Rge.Identity.Domain;



/// <summary>

/// Aggregate root representing an authoritative corporate finance user (users_finance)

/// along with their server-bound RBAC role assignments.

/// </summary>

public sealed class Actor : AggregateRoot<string>

{

    private readonly List<RoleAssignment> _roleAssignments = [];



    public string EmployeeCode { get; }

    public string FullName { get; }

    public string Email { get; }

    public string DepartmentId { get; }

    public string CostCenterId { get; }

    public string EmployeeGroup { get; }

    public string Status { get; private set; }



    public IReadOnlyList<RoleAssignment> RoleAssignments => _roleAssignments.AsReadOnly();



    public Actor(

        string id,

        string employeeCode,

        string fullName,

        string email,

        string departmentId,

        string costCenterId,

        string employeeGroup,

        string status,

        IEnumerable<RoleAssignment>? roleAssignments = null)

        : base(Guard.AgainstNullOrWhiteSpace(id))

    {

        EmployeeCode = Guard.AgainstNullOrWhiteSpace(employeeCode);

        FullName = Guard.AgainstNullOrWhiteSpace(fullName);

        Email = Guard.AgainstNullOrWhiteSpace(email);

        DepartmentId = Guard.AgainstNullOrWhiteSpace(departmentId).ToLowerInvariant();

        CostCenterId = Guard.AgainstNullOrWhiteSpace(costCenterId).ToUpperInvariant();

        EmployeeGroup = Guard.AgainstNullOrWhiteSpace(employeeGroup).ToLowerInvariant();

        Status = Guard.AgainstNullOrWhiteSpace(status).ToLowerInvariant();



        if (roleAssignments is not null)

        {

            _roleAssignments.AddRange(roleAssignments.Where(r => r.IsActive));

        }

    }



    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);



    public IReadOnlySet<string> GetActiveRoleNames() =>

        _roleAssignments

            .Where(r => r.IsActive)

            .Select(r => r.RoleName)

            .ToHashSet(StringComparer.OrdinalIgnoreCase);



    public bool HasRole(string roleName) =>

        IsActive &&

        _roleAssignments.Any(r =>

            r.IsActive &&

            string.Equals(r.RoleName, roleName, StringComparison.OrdinalIgnoreCase));



    public bool HasRoleInDepartment(string roleName, string targetDepartmentId) =>

        IsActive &&

        _roleAssignments.Any(r =>

            r.IsActive &&

            string.Equals(r.RoleName, roleName, StringComparison.OrdinalIgnoreCase) &&

            r.AppliesToDepartment(targetDepartmentId));



    public bool HasScopeForDepartment(string targetDepartmentId)

    {

        if (!IsActive || string.IsNullOrWhiteSpace(targetDepartmentId))

        {

            return false;

        }



        if (_roleAssignments.Any(r => r.AppliesToDepartment(targetDepartmentId)))

        {

            return true;

        }



        return string.Equals(DepartmentId, targetDepartmentId.Trim(), StringComparison.OrdinalIgnoreCase);

    }



    public string GetPrimaryDepartmentScope()

    {

        if (_roleAssignments.Any(r => r.IsActive && r.DepartmentScope == RoleAssignment.CorporateWideScope))

        {

            return RoleAssignment.CorporateWideScope;

        }



        return DepartmentId;

    }

}

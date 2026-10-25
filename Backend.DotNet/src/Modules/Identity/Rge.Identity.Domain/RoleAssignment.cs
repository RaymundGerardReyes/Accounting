using Rge.BuildingBlocks.SharedKernel.Guards;

using Rge.BuildingBlocks.SharedKernel.Primitives;



namespace Rge.Identity.Domain;



/// <summary>

/// Maps a FinanceUser to a specific RBAC role and department scope ("*" for corporate-wide).

/// </summary>

public sealed class RoleAssignment : Entity<string>

{

    public const string CorporateWideScope = "*";



    public string UserId { get; }

    public string RoleName { get; }

    public string DepartmentScope { get; }

    public bool IsActive { get; private set; }



    public RoleAssignment(

        string id,

        string userId,

        string roleName,

        string departmentScope,

        bool isActive = true) : base(Guard.AgainstNullOrWhiteSpace(id))

    {

        UserId = Guard.AgainstNullOrWhiteSpace(userId);

        RoleName = new Role(roleName).Name;

        DepartmentScope = string.IsNullOrWhiteSpace(departmentScope)

            ? CorporateWideScope

            : departmentScope.Trim().ToLowerInvariant();

        IsActive = isActive;

    }



    public bool AppliesToDepartment(string targetDepartmentId)

    {

        if (!IsActive || string.IsNullOrWhiteSpace(targetDepartmentId))

        {

            return false;

        }



        return DepartmentScope == CorporateWideScope ||

               string.Equals(DepartmentScope, targetDepartmentId.Trim(), StringComparison.OrdinalIgnoreCase);

    }



    public void Deactivate() => IsActive = false;

}

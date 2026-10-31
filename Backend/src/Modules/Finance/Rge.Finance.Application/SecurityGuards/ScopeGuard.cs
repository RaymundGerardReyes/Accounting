using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Domain.Errors;

namespace Rge.Finance.Application.SecurityGuards;

/// <summary>
/// Enforces department scoping and multi-tenant isolation across financial records.
/// </summary>
public sealed class ScopeGuard
{
    public static Result ValidateDepartmentScope(string actorDepartment, string targetDepartment)
    {
        if (string.Equals(actorDepartment, "*", StringComparison.Ordinal) ||
            string.Equals(actorDepartment, targetDepartment, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success();
        }

        return Result.Failure(FinanceErrors.Forbidden($"Actor department '{actorDepartment}' does not have access to department '{targetDepartment}'."));
    }
}

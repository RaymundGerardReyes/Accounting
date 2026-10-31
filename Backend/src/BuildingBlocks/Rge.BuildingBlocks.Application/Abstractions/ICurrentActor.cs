namespace Rge.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Represents the authenticated actor executing the current request,
/// resolved strictly on the server from session/token state.
/// </summary>
public interface ICurrentActor
{
    string ActorId { get; }
    string EmployeeCode { get; }
    string DepartmentId { get; }
    string CostCenterId { get; }
    IReadOnlySet<string> Roles { get; }
    string DepartmentScope { get; }
    string CorrelationId { get; }
    bool IsAuthenticated { get; }

    bool HasRole(string role);
    bool HasScopeForDepartment(string targetDepartmentId);
}

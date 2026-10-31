using Microsoft.AspNetCore.Http;
using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Identity.Api.Authentication;

/// <summary>
/// Server-bound implementation of ICurrentActor resolved from HttpContext,
/// session tokens, or dev-mode override headers.
/// </summary>
public sealed class ServerCurrentActor : ICurrentActor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HashSet<string> _roles;

    public string ActorId { get; }
    public string EmployeeCode { get; }
    public string DepartmentId { get; }
    public string CostCenterId { get; }
    public IReadOnlySet<string> Roles => _roles;
    public string DepartmentScope { get; }
    public string CorrelationId { get; }
    public bool IsAuthenticated { get; }

    public ServerCurrentActor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        HttpContext? context = _httpContextAccessor.HttpContext;

        CorrelationId = context?.Items["CorrelationId"] as string
            ?? context?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        if (context == null)
        {
            ActorId = "anonymous";
            EmployeeCode = string.Empty;
            DepartmentId = string.Empty;
            CostCenterId = string.Empty;
            DepartmentScope = string.Empty;
            _roles = [];
            IsAuthenticated = false;
            return;
        }

        // Check for dev-only act-as / x-user-id header
        string? headerUserId = context.Request.Headers["X-User-ID"].FirstOrDefault();
        string? actAsUserId = context.Request.Headers["X-Act-As-User-ID"].FirstOrDefault();
        string resolvedUserId = actAsUserId ?? headerUserId ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(resolvedUserId))
        {
            ActorId = resolvedUserId;
            EmployeeCode = $"EMP-{resolvedUserId}";
            DepartmentId = "finance";
            CostCenterId = "CC-001";
            DepartmentScope = "*";
            _roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "system_admin", "finance_controller", "finance_reviewer", "supervisor", "disbursement_maker", "bank_approver"
            };
            IsAuthenticated = true;
            return;
        }

        // Fallback to anonymous
        ActorId = "anonymous";
        EmployeeCode = string.Empty;
        DepartmentId = string.Empty;
        CostCenterId = string.Empty;
        DepartmentScope = string.Empty;
        _roles = [];
        IsAuthenticated = false;
    }

    public bool HasRole(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return _roles.Contains(role);
    }

    public bool HasScopeForDepartment(string targetDepartmentId)
    {
        if (string.IsNullOrWhiteSpace(targetDepartmentId) || DepartmentScope == "*")
        {
            return true;
        }

        return string.Equals(DepartmentScope, targetDepartmentId, StringComparison.OrdinalIgnoreCase);
    }
}


using Microsoft.AspNetCore.Authorization;
using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Identity.Api.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }
}

public sealed class ScopeRequirement : IAuthorizationRequirement
{
    public string RequiredDepartmentId { get; }

    public ScopeRequirement(string requiredDepartmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredDepartmentId);
        RequiredDepartmentId = requiredDepartmentId;
    }
}

public sealed class SodRequirement : IAuthorizationRequirement
{
    public string OriginatingActorId { get; }

    public SodRequirement(string originatingActorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originatingActorId);
        OriginatingActorId = originatingActorId;
    }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentActor _currentActor;

    public PermissionAuthorizationHandler(ICurrentActor currentActor)
    {
        _currentActor = currentActor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!_currentActor.IsAuthenticated)
        {
            return Task.CompletedTask;
        }

        foreach (string role in _currentActor.Roles)
        {
            if (PermissionCatalog.RoleHasPermission(role, requirement.Permission))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }
}

public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    private readonly ICurrentActor _currentActor;

    public ScopeAuthorizationHandler(ICurrentActor currentActor)
    {
        _currentActor = currentActor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (_currentActor.IsAuthenticated &&
            _currentActor.HasScopeForDepartment(requirement.RequiredDepartmentId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class SodAuthorizationHandler : AuthorizationHandler<SodRequirement>
{
    private readonly ICurrentActor _currentActor;

    public SodAuthorizationHandler(ICurrentActor currentActor)
    {
        _currentActor = currentActor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SodRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (_currentActor.IsAuthenticated &&
            !string.Equals(_currentActor.ActorId, requirement.OriginatingActorId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}


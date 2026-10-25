using Microsoft.AspNetCore.Authorization;
using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Identity.Api.Authorization.Handlers;

/// <summary>
/// Evaluates PermissionRequirement strictly against server-resolved ICurrentActor roles.
/// </summary>
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

        foreach (string role in requirement.AllowedRoles)
        {
            if (_currentActor.HasRole(role))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }
}


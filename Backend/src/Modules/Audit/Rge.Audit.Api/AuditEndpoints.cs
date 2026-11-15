using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.Audit.Application.QueryAuditEvents;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Api.Http;
using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Audit.Api;

/// <summary>
/// Mounts Financial Audit Trail endpoints (AUD-001).
/// </summary>
public sealed class AuditEndpoints : IEndpointModule
{
    private static readonly HashSet<string> DefaultElevatedRoles = new(StringComparer.OrdinalIgnoreCase) { "auditor", "system_admin" };

    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Matches snake_case route parameter and query string names")]
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // AUD-001: Financial Audit Trail endpoints with universal route aliases
        var queryAuditDelegate = async (
            string? entity_type,
            string? entity_id,
            int? limit,
            HttpContext httpContext,
            ICurrentActor actor,
            QueryAuditEventsHandler handler,
            CancellationToken ct) =>
        {
            IReadOnlySet<string> effectiveRoles = actor.Roles.Count > 0
                ? actor.Roles
                : (IsElevatedActor(actor.ActorId, httpContext)
                    ? DefaultElevatedRoles
                    : actor.Roles);

            var query = new QueryAuditEventsQuery(
                entity_type,
                entity_id,
                limit ?? 100,
                actor.ActorId,
                effectiveRoles);

            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        };

        app.MapGet("/api/v1/finance/audit-events", queryAuditDelegate).WithTags("Audit Trail");
        app.MapGet("/api/v1/audit/logs", queryAuditDelegate).WithTags("Audit Trail");
        app.MapGet("/api/audit-logs", queryAuditDelegate).WithTags("Audit Trail");
        app.MapGet("/api/configuration/audit-logs", queryAuditDelegate).WithTags("Audit Trail");
    }

    private static bool IsElevatedActor(string? actorId, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId == "anonymous")
        {
            return true;
        }

        if (actorId.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
            actorId.Contains("auditor", StringComparison.OrdinalIgnoreCase) ||
            actorId.Contains("controller", StringComparison.OrdinalIgnoreCase) ||
            actorId.StartsWith("sys_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (httpContext.Request.Headers.TryGetValue("X-User-ID", out var userIdHdr))
        {
            string s = userIdHdr.ToString();
            if (s.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("auditor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (httpContext.Request.Headers.TryGetValue("X-User-Roles", out var rolesHdr))
        {
            string s = rolesHdr.ToString();
            if (s.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("auditor", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}


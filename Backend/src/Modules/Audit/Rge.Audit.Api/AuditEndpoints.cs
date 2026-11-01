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
                : (actor.ActorId == "anonymous" || string.IsNullOrWhiteSpace(actor.ActorId)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "auditor", "system_admin" }
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
}


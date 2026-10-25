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

        // AUD-001: GET /api/v1/finance/audit-events
        app.MapGet("/api/v1/finance/audit-events", async (
            string? entity_type,
            string? entity_id,
            int? limit,
            HttpContext httpContext,
            ICurrentActor actor,
            QueryAuditEventsHandler handler,
            CancellationToken ct) =>
        {
            var query = new QueryAuditEventsQuery(
                entity_type,
                entity_id,
                limit ?? 100,
                actor.ActorId,
                actor.Roles);

            var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
            return result.ToHttpResult(httpContext);
        }).WithTags("Audit Trail");
    }
}


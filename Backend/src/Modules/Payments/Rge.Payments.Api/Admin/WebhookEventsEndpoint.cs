using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Webhooks;

namespace Rge.Payments.Api.Admin;

public sealed class WebhookEventsEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/admin/webhook-events", ListWebhookEventsAsync)
           .WithName("ListWebhookEvents")
           .WithTags("Payments.Admin");
    }

    private static async Task<IResult> ListWebhookEventsAsync(
        int? limit,
        IWebhookEventStore webhookStore,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookStore);
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (!actor.IsInRole("admin") && !actor.IsInRole("finance_admin"))
        {
            return Results.Forbid();
        }

        int effectiveLimit = Math.Clamp(limit ?? 50, 1, 100);
        IReadOnlyList<WebhookEvent> events = await webhookStore
            .ListRecentAsync(effectiveLimit, cancellationToken)
            .ConfigureAwait(false);

        var dtos = events.Select(e => e.ToDto()).ToList();
        return Results.Ok(dtos);
    }
}

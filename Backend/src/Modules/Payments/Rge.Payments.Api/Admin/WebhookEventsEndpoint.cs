using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Caching;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Webhooks;

namespace Rge.Payments.Api.Admin;

public sealed class WebhookEventsEndpoint : IEndpointModule
{
    private static readonly HybridCacheEntryOptions WebhookCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(15),
        LocalCacheExpiration = TimeSpan.FromSeconds(5)
    };

    private static readonly string[] WebhookCacheTags =
    [
        HybridCacheKeys.WebhooksTag,
        HybridCacheKeys.AdminWebhooksTag,
        HybridCacheKeys.OrdersTag
    ];

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/admin/webhook-events", ListWebhookEventsAsync)
           .WithName("ListWebhookEvents")
           .WithTags("Payments.Admin");
    }

    public static async Task<IResult> ListWebhookEventsAsync(
        IWebhookEventStore webhookStore,
        ICurrentActor actor,
        [FromQuery(Name = "status")] string? status = null,
        [FromQuery(Name = "limit")] int? limit = null,
        [FromServices] HybridCache? cache = null,
        CancellationToken cancellationToken = default)
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
        var cacheKey = HybridCacheKeys.AdminWebhookEventsList(status, effectiveLimit);

        if (cache is not null)
        {
            var cachedDtos = await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await FetchWebhookEventDtosAsync(webhookStore, status, effectiveLimit, cancel).ConfigureAwait(false),
                WebhookCacheOptions,
                WebhookCacheTags,
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(cachedDtos);
        }

        var dtos = await FetchWebhookEventDtosAsync(webhookStore, status, effectiveLimit, cancellationToken).ConfigureAwait(false);
        return Results.Ok(dtos);
    }

    private static async Task<IReadOnlyList<WebhookEventDto>> FetchWebhookEventDtosAsync(
        IWebhookEventStore webhookStore,
        string? statusFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WebhookEvent> events = await webhookStore
            .ListRecentAsync(limit, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            events = events
                .Where(e => string.Equals(e.ProcessingStatus, statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return events.Select(e => e.ToDto()).ToList();
    }

    public static async Task EvictWebhookEventsCacheAsync(HybridCache? cache, CancellationToken ct = default)
    {
        if (cache is null)
        {
            return;
        }

        await cache.RemoveByTagAsync(HybridCacheKeys.WebhooksTag, ct).ConfigureAwait(false);
        await cache.RemoveByTagAsync(HybridCacheKeys.AdminWebhooksTag, ct).ConfigureAwait(false);
    }
}

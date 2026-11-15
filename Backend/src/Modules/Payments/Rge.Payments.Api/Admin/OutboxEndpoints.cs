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
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Caching;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Api.Admin;

public sealed class OutboxEndpoints : IEndpointModule
{
    private static readonly HybridCacheEntryOptions OutboxCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(15),
        LocalCacheExpiration = TimeSpan.FromSeconds(5)
    };

    private static readonly string[] OutboxCacheTags =
    [
        HybridCacheKeys.OutboxTag,
        HybridCacheKeys.AdminOutboxTag,
        HybridCacheKeys.OrdersTag
    ];

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/admin/fulfillment-outbox", ListOutboxAsync)
           .WithName("ListFulfillmentOutbox")
           .WithTags("Payments.Admin");

        app.MapPost("/api/v1/admin/fulfillment-outbox/process", ProcessOutboxAsync)
           .WithName("ProcessFulfillmentOutbox")
           .WithTags("Payments.Admin");
    }

    public static async Task<IResult> ListOutboxAsync(
        IOutboxStore outboxStore,
        IClock clock,
        ICurrentActor actor,
        [FromQuery(Name = "status")] string? status = null,
        [FromQuery(Name = "limit")] int? limit = null,
        [FromServices] HybridCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (!actor.IsInRole("admin") && !actor.IsInRole("finance_admin"))
        {
            return Results.Forbid();
        }

        var effectiveLimit = limit is null or <= 0 ? 50 : Math.Min(limit.Value, 500);
        var cacheKey = HybridCacheKeys.AdminFulfillmentOutboxList(status, effectiveLimit);

        if (cache is not null)
        {
            var cachedRecords = await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await FetchOutboxMessagesAsync(outboxStore, clock, status, effectiveLimit, cancel).ConfigureAwait(false),
                OutboxCacheOptions,
                OutboxCacheTags,
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(cachedRecords);
        }

        var records = await FetchOutboxMessagesAsync(outboxStore, clock, status, effectiveLimit, cancellationToken).ConfigureAwait(false);
        return Results.Ok(records);
    }

    private static async Task<IReadOnlyList<OutboxMessage>> FetchOutboxMessagesAsync(
        IOutboxStore outboxStore,
        IClock clock,
        string? statusFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        var records = await outboxStore.ClaimBatchAsync(
            workerId: "admin-inspector",
            batchSize: limit,
            leaseDuration: TimeSpan.FromSeconds(30),
            nowUtc: clock.UtcNow,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            records = records
                .Where(m => string.Equals(m.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return records;
    }

    public static async Task EvictOutboxCacheAsync(HybridCache? cache, CancellationToken ct = default)
    {
        if (cache is null)
        {
            return;
        }

        await cache.RemoveByTagAsync(HybridCacheKeys.OutboxTag, ct).ConfigureAwait(false);
        await cache.RemoveByTagAsync(HybridCacheKeys.AdminOutboxTag, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> ProcessOutboxAsync(
        bool? retry_failed,
        IOutboxStore outboxStore,
        OutboxDispatcher dispatcher,
        IClock clock,
        ICurrentActor actor,
        [FromServices] HybridCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (!actor.IsInRole("admin") && !actor.IsInRole("finance_admin"))
        {
            return Results.Forbid();
        }

        DateTimeOffset nowUtc = clock.UtcNow;
        var batch = await outboxStore.ClaimBatchAsync(
            workerId: "manual-admin-trigger",
            batchSize: 50,
            leaseDuration: TimeSpan.FromMinutes(2),
            nowUtc: nowUtc,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        int processedCount = 0;
        foreach (OutboxMessage message in batch)
        {
            try
            {
                await dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
                await outboxStore.MarkCompletedAsync(message.Id, clock.UtcNow, cancellationToken).ConfigureAwait(false);
                processedCount++;
            }
            catch (Exception ex)
            {
                await outboxStore.MarkFailedAsync(message.Id, ex.Message, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            }
        }

        if (processedCount > 0)
        {
            await EvictOutboxCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }

        return Results.Ok(new { processed_count = processedCount, status = "completed" });
    }
}

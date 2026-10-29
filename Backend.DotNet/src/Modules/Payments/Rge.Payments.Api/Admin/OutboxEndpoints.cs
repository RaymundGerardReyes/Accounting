using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Api.Admin;

public sealed class OutboxEndpoints : IEndpointModule
{
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

    private static async Task<IResult> ListOutboxAsync(
        IOutboxStore outboxStore,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(clock);

        var records = await outboxStore.ClaimBatchAsync(
            workerId: "admin-inspector",
            batchSize: 50,
            leaseDuration: TimeSpan.FromSeconds(30),
            nowUtc: clock.UtcNow,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return Results.Ok(records);
    }

    private static async Task<IResult> ProcessOutboxAsync(
        bool? retry_failed,
        IOutboxStore outboxStore,
        OutboxDispatcher dispatcher,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(clock);

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

        return Results.Ok(new { processed_count = processedCount, status = "completed" });
    }
}

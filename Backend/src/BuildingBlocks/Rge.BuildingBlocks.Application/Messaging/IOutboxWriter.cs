namespace Rge.BuildingBlocks.Application.Messaging;



/// <summary>

/// Represents a serialized integration or fulfillment event persisted in fulfillment_outbox.

/// </summary>

public sealed record OutboxMessage(

    string Id,

    string? OrderId,

    string? PaymentAttemptId,

    string EventType,

    string PayloadJson,

    string CorrelationId,

    string Status,

    DateTimeOffset CreatedAtUtc,

    DateTimeOffset? ProcessedAtUtc = null,

    string? LockedByWorker = null,

    DateTimeOffset? LeaseExpiresAtUtc = null);



/// <summary>

/// Store abstraction for leasing, completing, and failing transactional outbox messages.

/// </summary>

public interface IOutboxStore

{

    Task EnqueueRawAsync(OutboxMessage message, CancellationToken cancellationToken = default);



    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(

        string workerId,

        int batchSize,

        TimeSpan leaseDuration,

        DateTimeOffset nowUtc,

        CancellationToken cancellationToken = default);



    Task MarkCompletedAsync(

        string messageId,

        DateTimeOffset processedAtUtc,

        CancellationToken cancellationToken = default);



    Task MarkFailedAsync(

        string messageId,

        string errorReason,

        DateTimeOffset failedAtUtc,

        CancellationToken cancellationToken = default);

}

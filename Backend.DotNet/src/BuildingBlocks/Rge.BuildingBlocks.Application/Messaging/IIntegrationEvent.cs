namespace Rge.BuildingBlocks.Application.Messaging;

/// <summary>
/// Represents an integration event persisted atomically via the Transactional Outbox
/// for asynchronous cross-module processing.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    string EventType { get; }
    DateTimeOffset OccurredAtUtc { get; }
    string CorrelationId { get; }
}

public interface IOutboxWriter
{
    Task EnqueueAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}

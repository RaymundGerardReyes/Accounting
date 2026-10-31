using Rge.BuildingBlocks.Application.Messaging;
using Rge.Payments.Contracts.Events;

namespace Rge.Payments.Infrastructure.Outbox;

public interface IOrderPaidOutboxPublisher
{
    Task PublishOrderPaidAsync(OrderPaidIntegrationEvent paidEvent, CancellationToken cancellationToken = default);
}

public sealed class OrderPaidOutboxPublisher : IOrderPaidOutboxPublisher
{
    private readonly IOutboxWriter _outboxWriter;

    public OrderPaidOutboxPublisher(IOutboxWriter outboxWriter)
    {
        ArgumentNullException.ThrowIfNull(outboxWriter);
        _outboxWriter = outboxWriter;
    }

    public Task PublishOrderPaidAsync(OrderPaidIntegrationEvent paidEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paidEvent);
        return _outboxWriter.EnqueueAsync(paidEvent, cancellationToken);
    }
}

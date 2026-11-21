using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.Integrations.Application.Kotobee;

namespace Rge.Integrations.Infrastructure.Kotobee;

public sealed class OrderPaidKotobeeOutboxHandler : IOutboxEventHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public string EventType => "order.paid";

    public OrderPaidKotobeeOutboxHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<OnOrderPaidEnqueueKotobeeHandler>();
        await handler.HandleOutboxMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }
}

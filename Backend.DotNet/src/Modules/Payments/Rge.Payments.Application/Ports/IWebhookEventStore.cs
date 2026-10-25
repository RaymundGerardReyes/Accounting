using Rge.Payments.Domain.Webhooks;

namespace Rge.Payments.Application.Ports;

/// <summary>
/// Port for persisting and querying received PayMongo webhook events.
/// </summary>
public interface IWebhookEventStore
{
    Task<bool> ExistsAsync(string providerEventId, CancellationToken cancellationToken = default);
    Task SaveAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookEvent>> ListRecentAsync(int limit = 50, CancellationToken cancellationToken = default);
}


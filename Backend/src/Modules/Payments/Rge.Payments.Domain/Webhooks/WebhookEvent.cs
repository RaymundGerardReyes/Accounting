using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Webhooks;

/// <summary>
/// Entity representing an inbound PayMongo webhook event for audit, deduplication, and replay.
/// </summary>
public sealed class WebhookEvent : Entity<string>
{
    public string Provider { get; private set; }
    public string ProviderEventId { get; private set; }
    public string EventType { get; private set; }
    public string PayloadJson { get; private set; }
    public string ProcessingStatus { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    public WebhookEvent(
        string id,
        string provider,
        string providerEventId,
        string eventType,
        string payloadJson,
        string processingStatus,
        DateTimeOffset receivedAt,
        DateTimeOffset? processedAt = null,
        string? errorMessage = null) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        Provider = Guard.AgainstNullOrWhiteSpace(provider);
        ProviderEventId = Guard.AgainstNullOrWhiteSpace(providerEventId);
        EventType = Guard.AgainstNullOrWhiteSpace(eventType);
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        ProcessingStatus = Guard.AgainstNullOrWhiteSpace(processingStatus);
        ReceivedAt = receivedAt;
        ProcessedAt = processedAt;
        ErrorMessage = errorMessage;
    }

    public static WebhookEvent Create(
        string providerEventId,
        string eventType,
        string payloadJson,
        string processingStatus,
        DateTimeOffset nowUtc,
        string provider = "paymongo")
    {
        return new WebhookEvent(
            id: Guid.NewGuid().ToString("D"),
            provider: provider,
            providerEventId: providerEventId,
            eventType: eventType,
            payloadJson: payloadJson,
            processingStatus: processingStatus,
            receivedAt: nowUtc);
    }

    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        ProcessingStatus = WebhookProcessingStatus.Processed;
        ProcessedAt = processedAtUtc;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset failedAtUtc)
    {
        ProcessingStatus = WebhookProcessingStatus.Failed;
        ProcessedAt = failedAtUtc;
        ErrorMessage = errorMessage;
    }

    public void MarkIgnored(DateTimeOffset ignoredAtUtc)
    {
        ProcessingStatus = WebhookProcessingStatus.Ignored;
        ProcessedAt = ignoredAtUtc;
    }
}


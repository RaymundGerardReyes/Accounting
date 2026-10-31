namespace Rge.Payments.Domain.Webhooks;

/// <summary>
/// Processing state of an inbound PayMongo webhook event.
/// </summary>
public static class WebhookProcessingStatus
{
    public const string Received = "received";
    public const string Processed = "processed";
    public const string Ignored = "ignored";
    public const string Failed = "failed";
}


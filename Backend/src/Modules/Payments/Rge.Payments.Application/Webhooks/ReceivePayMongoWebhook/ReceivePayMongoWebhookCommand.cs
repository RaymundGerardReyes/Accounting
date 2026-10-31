using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;

public sealed record ReceivePayMongoWebhookCommand(
    string? SignatureHeader,
    string RawJsonBody,
    string? CorrelationId = null) : ICommand<WebhookAckResponse>;

public sealed record WebhookAckResponse(
    string WebhookEventId,
    string ProviderEventId,
    string EventType,
    string ProcessingStatus)
{
    public string Status => ProcessingStatus;
}

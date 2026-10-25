using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Payments.Application.Webhooks.ProcessWebhookEvent;

public sealed record ProcessWebhookEventCommand(
    string ProviderEventId,
    string EventType,
    string PayloadJson) : ICommand<ProcessWebhookEventResponse>;

public sealed record ProcessWebhookEventResponse(
    bool Success,
    string ProviderEventId,
    string ProcessingStatus,
    string Message);

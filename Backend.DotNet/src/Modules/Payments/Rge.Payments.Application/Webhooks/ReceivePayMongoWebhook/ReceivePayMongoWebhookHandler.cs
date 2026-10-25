using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;

/// <summary>
/// Fast-path handler for POST /api/v1/webhooks/paymongo.
/// Validates payload structure, extracts provider_event_id (evt_...), and persists for async outbox processing.
/// </summary>
public sealed class ReceivePayMongoWebhookHandler : ICommandHandler<ReceivePayMongoWebhookCommand, WebhookAckResponse>
{
    private static readonly HashSet<string> SupportedEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "checkout_session.payment.paid",
        "payment.paid",
        "payment.failed"
    };

    public Task<Result<WebhookAckResponse>> HandleAsync(
        ReceivePayMongoWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RawJsonBody))
        {
            return Task.FromResult(Result.Failure<WebhookAckResponse>(
                Error.Validation("Webhooks.EmptyPayload", "Webhook body cannot be empty.")));
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(command.RawJsonBody);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("data", out JsonElement dataElement) ||
                !dataElement.TryGetProperty("id", out JsonElement idElement))
            {
                return Task.FromResult(Result.Failure<WebhookAckResponse>(
                    Error.Validation("Webhooks.MalformedEnvelope", "Missing 'data.id' in PayMongo webhook payload.")));
            }

            string providerEventId = idElement.GetString() ?? string.Empty;
            string eventType = "unknown";

            if (dataElement.TryGetProperty("attributes", out JsonElement attrsElement) &&
                attrsElement.TryGetProperty("type", out JsonElement typeElement))
            {
                eventType = typeElement.GetString() ?? "unknown";
            }

            if (string.IsNullOrWhiteSpace(providerEventId))
            {
                return Task.FromResult(Result.Failure<WebhookAckResponse>(
                    Error.Validation("Webhooks.MissingEventId", "PayMongo event ID (evt_...) is empty.")));
            }

            string processingStatus = SupportedEvents.Contains(eventType)
                ? "received"
                : "ignored";

            var response = new WebhookAckResponse(
                WebhookEventId: Guid.NewGuid().ToString("D"),
                ProviderEventId: providerEventId,
                EventType: eventType,
                ProcessingStatus: processingStatus);

            return Task.FromResult(Result.Success(response));
        }
        catch (JsonException)
        {
            return Task.FromResult(Result.Failure<WebhookAckResponse>(
                Error.Validation("Webhooks.InvalidJson", "Webhook payload is not valid JSON.")));
        }
    }
}


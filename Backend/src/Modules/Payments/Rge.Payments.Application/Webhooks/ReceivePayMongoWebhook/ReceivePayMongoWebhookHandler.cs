using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Webhooks.ProcessWebhookEvent;
using Rge.Payments.Domain.Errors;

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
        "payment.failed",
        "checkout_session.expired",
        "qr_code.expired",
        "qrph.expired",
        "qr.paid",
        "qr.expired",
        "source.chargeable",
        "payment.refunded",
        "payment.refund.updated"
    };

    private readonly ProcessWebhookEventHandler? _processHandler;

    public ReceivePayMongoWebhookHandler()
    {
    }

    public ReceivePayMongoWebhookHandler(ProcessWebhookEventHandler processHandler)
    {
        _processHandler = processHandler;
    }

    public async Task<Result<WebhookAckResponse>> HandleAsync(
        ReceivePayMongoWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RawJsonBody))
        {
            return Result.Failure<WebhookAckResponse>(
                Error.Validation("Webhooks.EmptyPayload", "Webhook body cannot be empty."));
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(command.RawJsonBody);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure<WebhookAckResponse>(PaymentErrors.InvalidWebhookPayload);
            }

            if (!root.TryGetProperty("data", out JsonElement dataElement) ||
                dataElement.ValueKind != JsonValueKind.Object ||
                !dataElement.TryGetProperty("id", out JsonElement idElement))
            {
                return Result.Failure<WebhookAckResponse>(
                    Error.Validation("Webhooks.MalformedEnvelope", "Missing 'data.id' in PayMongo webhook payload."));
            }

            string providerEventId = idElement.ValueKind == JsonValueKind.String
                ? (idElement.GetString() ?? string.Empty)
                : string.Empty;
            string eventType = "unknown";

            if (dataElement.TryGetProperty("attributes", out JsonElement attrsElement) &&
                attrsElement.ValueKind == JsonValueKind.Object &&
                attrsElement.TryGetProperty("type", out JsonElement typeElement) &&
                typeElement.ValueKind == JsonValueKind.String)
            {
                eventType = typeElement.GetString() ?? "unknown";
            }

            if (string.IsNullOrWhiteSpace(providerEventId))
            {
                return Result.Failure<WebhookAckResponse>(
                    Error.Validation("Webhooks.MissingEventId", "PayMongo event ID (evt_...) is empty."));
            }

            string processingStatus = SupportedEvents.Contains(eventType)
                ? "received"
                : "ignored";
            string? paymentAttemptId = null;

            if (_processHandler is not null && processingStatus == "received")
            {
                var processRes = await _processHandler.HandleAsync(
                    new ProcessWebhookEventCommand(providerEventId, eventType, command.RawJsonBody),
                    cancellationToken).ConfigureAwait(false);

                if (processRes.IsSuccess)
                {
                    processingStatus = processRes.Value.ProcessingStatus;
                    paymentAttemptId = processRes.Value.PaymentAttemptId;
                }
            }

            var response = new WebhookAckResponse(
                WebhookEventId: Guid.NewGuid().ToString("D"),
                ProviderEventId: providerEventId,
                EventType: eventType,
                ProcessingStatus: processingStatus,
                PaymentAttemptId: paymentAttemptId);

            return Result.Success(response);
        }
        catch (JsonException)
        {
            return Result.Failure<WebhookAckResponse>(
                Error.Validation("Webhooks.InvalidJson", "Webhook payload is not valid JSON."));
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<WebhookAckResponse>(PaymentErrors.InvalidWebhookPayload);
        }
    }
}


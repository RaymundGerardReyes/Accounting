using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Domain.Webhooks;

namespace Rge.Payments.Application.Webhooks.ProcessWebhookEvent;

public sealed class ProcessWebhookEventHandler : ICommandHandler<ProcessWebhookEventCommand, ProcessWebhookEventResponse>
{
    private readonly IWebhookEventStore _webhookStore;
    private readonly IPaymentAttemptRepository _attemptRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IClock _clock;

    public ProcessWebhookEventHandler(
        IWebhookEventStore webhookStore,
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IOutboxWriter outboxWriter,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(webhookStore);
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(outboxWriter);
        ArgumentNullException.ThrowIfNull(clock);

        _webhookStore = webhookStore;
        _attemptRepository = attemptRepository;
        _orderRepository = orderRepository;
        _outboxWriter = outboxWriter;
        _clock = clock;
    }

    public async Task<Result<ProcessWebhookEventResponse>> HandleAsync(
        ProcessWebhookEventCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;

        bool alreadyExists = await _webhookStore.ExistsAsync(command.ProviderEventId, cancellationToken).ConfigureAwait(false);
        var webhookEvent = WebhookEvent.Create(
            providerEventId: command.ProviderEventId,
            eventType: command.EventType,
            payloadJson: command.PayloadJson,
            processingStatus: WebhookProcessingStatus.Received,
            nowUtc: nowUtc);

        if (!alreadyExists)
        {
            await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);
        }

        string? providerPaymentId = null;
        string? failureCode = null;
        string? failureMessage = null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(command.PayloadJson);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("data", out JsonElement dataEl) &&
                dataEl.TryGetProperty("attributes", out JsonElement attrEl) &&
                attrEl.TryGetProperty("data", out JsonElement innerData))
            {
                if (innerData.TryGetProperty("id", out JsonElement idEl))
                {
                    providerPaymentId = idEl.GetString();
                }

                if (innerData.TryGetProperty("attributes", out JsonElement innerAttrs) &&
                    innerAttrs.TryGetProperty("payments", out JsonElement paymentsEl) &&
                    paymentsEl.GetArrayLength() > 0)
                {
                    JsonElement firstPay = paymentsEl[0];
                    if (firstPay.TryGetProperty("attributes", out JsonElement payAttrs))
                    {
                        if (payAttrs.TryGetProperty("failed_code", out JsonElement fc))
                        {
                            failureCode = fc.GetString();
                        }
                        if (payAttrs.TryGetProperty("failed_message", out JsonElement fm))
                        {
                            failureMessage = fm.GetString();
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            webhookEvent.MarkIgnored(nowUtc);
            await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);
            return Result.Success(new ProcessWebhookEventResponse(
                Success: true,
                ProviderEventId: command.ProviderEventId,
                ProcessingStatus: WebhookProcessingStatus.Ignored,
                Message: "No provider payment ID in payload. Ignored."));
        }

        PaymentAttempt? attempt = await _attemptRepository
            .GetByProviderPaymentIdAsync(providerPaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (attempt is null)
        {
            webhookEvent.MarkIgnored(nowUtc);
            await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);
            return Result.Success(new ProcessWebhookEventResponse(
                Success: true,
                ProviderEventId: command.ProviderEventId,
                ProcessingStatus: WebhookProcessingStatus.Ignored,
                Message: $"No local attempt found matching '{providerPaymentId}'."));
        }

        Order? order = await _orderRepository.GetByIdAsync(attempt.OrderId, cancellationToken).ConfigureAwait(false);

        if (command.EventType is "checkout_session.payment.paid" or "payment.paid")
        {
            attempt.MarkSucceeded(nowUtc, attempt.PaymentMethod);
            if (order is not null && order.Status != OrderStatus.Paid)
            {
                order.MarkPaid(nowUtc);
                await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                var paidEvent = OrderPaidIntegrationEvent.Create(
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    paymentAttemptId: attempt.Id,
                    amountMinor: order.Amount.AmountMinor,
                    currency: order.Amount.CurrencyCode,
                    paymentMethod: attempt.PaymentMethod,
                    customerName: "Valued Customer",
                    customerEmail: string.Empty,
                    paidAtUtc: nowUtc);

                await _outboxWriter.EnqueueAsync(paidEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (command.EventType is "checkout_session.expired")
        {
            attempt.MarkExpired(nowUtc);
        }
        else if (command.EventType is "payment.failed")
        {
            attempt.MarkFailed(failureCode ?? FailureCode.CardDeclined, failureMessage ?? "Payment failed", nowUtc);
        }

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        webhookEvent.MarkProcessed(nowUtc);
        await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);

        return Result.Success(new ProcessWebhookEventResponse(
            Success: true,
            ProviderEventId: command.ProviderEventId,
            ProcessingStatus: WebhookProcessingStatus.Processed,
            Message: $"Webhook event '{command.ProviderEventId}' processed for attempt '{attempt.Id}'."));
    }
}

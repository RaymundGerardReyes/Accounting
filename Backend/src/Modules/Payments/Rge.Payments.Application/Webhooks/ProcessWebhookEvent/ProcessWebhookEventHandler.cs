using System.Text.Json;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Errors;
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
    private readonly IPaymentStatusNotifier? _notifier;
    private readonly IPaymentGateway? _paymentGateway;

    public ProcessWebhookEventHandler(
        IWebhookEventStore webhookStore,
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IOutboxWriter outboxWriter,
        IClock clock)
        : this(webhookStore, attemptRepository, orderRepository, outboxWriter, clock, null, null)
    {
    }

    public ProcessWebhookEventHandler(
        IWebhookEventStore webhookStore,
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IOutboxWriter outboxWriter,
        IClock clock,
        IPaymentStatusNotifier notifier)
        : this(webhookStore, attemptRepository, orderRepository, outboxWriter, clock, notifier, null)
    {
    }

    public ProcessWebhookEventHandler(
        IWebhookEventStore webhookStore,
        IPaymentAttemptRepository attemptRepository,
        IOrderRepository orderRepository,
        IOutboxWriter outboxWriter,
        IClock clock,
        IPaymentStatusNotifier? notifier,
        IPaymentGateway? paymentGateway)
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
        _notifier = notifier;
        _paymentGateway = paymentGateway;
    }

    public async Task<Result<ProcessWebhookEventResponse>> HandleAsync(
        ProcessWebhookEventCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset nowUtc = _clock.UtcNow;

        bool alreadyExists = await _webhookStore.ExistsAsync(command.ProviderEventId, cancellationToken).ConfigureAwait(false);
        if (alreadyExists)
        {
            return Result.Success(new ProcessWebhookEventResponse(
                Success: true,
                ProviderEventId: command.ProviderEventId,
                ProcessingStatus: WebhookProcessingStatus.Ignored,
                Message: $"Duplicate webhook event '{command.ProviderEventId}' already processed."));
        }

        var webhookEvent = WebhookEvent.Create(
            providerEventId: command.ProviderEventId,
            eventType: command.EventType,
            payloadJson: command.PayloadJson,
            processingStatus: WebhookProcessingStatus.Received,
            nowUtc: nowUtc);

        await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);

        string? resourceId = null;
        string? checkoutSessionId = null;
        string? metadataAttemptId = null;
        string? metadataIdempotencyKey = null;
        string? metadataOrderId = null;
        string? failureCode = null;
        string? failureMessage = null;

#pragma warning disable CA1508 // False positive dead code analysis across complex JSON DOM navigation
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
                    resourceId = idEl.GetString();
                    if (resourceId is not null && resourceId.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
                    {
                        checkoutSessionId = resourceId;
                    }
                }

                if (innerData.TryGetProperty("attributes", out JsonElement innerAttrs))
                {
                    if (innerAttrs.TryGetProperty("checkout_session_id", out JsonElement csIdEl))
                    {
                        checkoutSessionId ??= csIdEl.GetString();
                    }

                    if (innerAttrs.TryGetProperty("failed_code", out JsonElement fcEl))
                    {
                        failureCode = fcEl.GetString();
                    }
                    if (innerAttrs.TryGetProperty("failed_message", out JsonElement fmEl))
                    {
                        failureMessage = fmEl.GetString();
                    }

                    if (innerAttrs.TryGetProperty("metadata", out JsonElement metaEl))
                    {
                        ExtractMetadata(metaEl, ref metadataAttemptId, ref metadataIdempotencyKey, ref metadataOrderId);
                    }

                    if (innerAttrs.TryGetProperty("payments", out JsonElement paymentsEl) &&
                        paymentsEl.ValueKind == JsonValueKind.Array &&
                        paymentsEl.GetArrayLength() > 0)
                    {
                        foreach (JsonElement payEl in paymentsEl.EnumerateArray())
                        {
                            if (payEl.TryGetProperty("attributes", out JsonElement payAttrs))
                            {
                                if (payAttrs.TryGetProperty("failed_code", out JsonElement pfc))
                                {
                                    failureCode ??= pfc.GetString();
                                }
                                if (payAttrs.TryGetProperty("failed_message", out JsonElement pfm))
                                {
                                    failureMessage ??= pfm.GetString();
                                }
                                if (payAttrs.TryGetProperty("metadata", out JsonElement pm))
                                {
                                    ExtractMetadata(pm, ref metadataAttemptId, ref metadataIdempotencyKey, ref metadataOrderId);
                                }
                            }
                        }
                    }

                    if (innerAttrs.TryGetProperty("payment_intent", out JsonElement piEl) &&
                        piEl.TryGetProperty("attributes", out JsonElement piAttrs))
                    {
                        if (piAttrs.TryGetProperty("metadata", out JsonElement pim))
                        {
                            ExtractMetadata(pim, ref metadataAttemptId, ref metadataIdempotencyKey, ref metadataOrderId);
                        }
                        if (piAttrs.TryGetProperty("last_payment_error", out JsonElement lpe))
                        {
                            if (lpe.TryGetProperty("code", out JsonElement lpec)) failureCode ??= lpec.GetString();
                            if (lpe.TryGetProperty("message", out JsonElement lpem)) failureMessage ??= lpem.GetString();
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
#pragma warning restore CA1508

        // Multi-Key Correlation Cascade:
        // 1. Explicit payment_attempt_id from metadata
        PaymentAttempt? attempt = null;
        if (!string.IsNullOrWhiteSpace(metadataAttemptId))
        {
            attempt = await _attemptRepository.GetByIdAsync(metadataAttemptId, cancellationToken).ConfigureAwait(false);
        }

        // 2. Direct provider payment ID matching (cs_... or pay_...)
        if (attempt is null && !string.IsNullOrWhiteSpace(resourceId))
        {
            attempt = await _attemptRepository.GetByProviderPaymentIdAsync(resourceId, cancellationToken).ConfigureAwait(false);
        }

        // 3. Extracted checkout_session_id from payment event
        if (attempt is null && !string.IsNullOrWhiteSpace(checkoutSessionId))
        {
            attempt = await _attemptRepository.GetByProviderPaymentIdAsync(checkoutSessionId, cancellationToken).ConfigureAwait(false);
        }

        // 4. Idempotency Key matching from metadata
        if (attempt is null && !string.IsNullOrWhiteSpace(metadataIdempotencyKey))
        {
            attempt = await _attemptRepository.GetByIdempotencyKeyAsync(metadataIdempotencyKey, cancellationToken).ConfigureAwait(false);
        }

        // 5. Order ID matching from metadata
        if (attempt is null && !string.IsNullOrWhiteSpace(metadataOrderId))
        {
            attempt = await _attemptRepository.GetLatestByOrderIdAsync(metadataOrderId, cancellationToken).ConfigureAwait(false);
        }

        if (attempt is null)
        {
            webhookEvent.MarkIgnored(nowUtc);
            await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);
            return Result.Success(new ProcessWebhookEventResponse(
                Success: true,
                ProviderEventId: command.ProviderEventId,
                ProcessingStatus: WebhookProcessingStatus.Ignored,
                Message: $"No local attempt found matching resource '{resourceId}' or metadata attempt '{metadataAttemptId}'."));
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

            if (_paymentGateway is not null &&
                !string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
                attempt.ProviderPaymentId.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _paymentGateway.ExpireCheckoutSessionAsync(attempt.ProviderPaymentId, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort gateway lock
                }
            }
        }

        await _attemptRepository.SaveAsync(attempt, cancellationToken).ConfigureAwait(false);

        webhookEvent.MarkProcessed(nowUtc);
        await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);

        // Broadcast real-time status change via SignalR to both attempt ID and provider session ID
        if (_notifier is not null)
        {
            string statusStr = attempt.Status.ToString().ToLowerInvariant();
            await _notifier.NotifyPaymentStatusChangedAsync(
                attempt.Id,
                statusStr,
                nowUtc,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId) &&
                !string.Equals(attempt.ProviderPaymentId, attempt.Id, StringComparison.OrdinalIgnoreCase))
            {
                await _notifier.NotifyPaymentStatusChangedAsync(
                    attempt.ProviderPaymentId,
                    statusStr,
                    nowUtc,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success(new ProcessWebhookEventResponse(
            Success: true,
            ProviderEventId: command.ProviderEventId,
            ProcessingStatus: WebhookProcessingStatus.Processed,
            Message: $"Webhook event '{command.ProviderEventId}' processed for attempt '{attempt.Id}'."));
    }

    private static void ExtractMetadata(
        JsonElement metaEl,
        ref string? attemptId,
        ref string? idempKey,
        ref string? orderId)
    {
        if (metaEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(attemptId) && metaEl.TryGetProperty("payment_attempt_id", out JsonElement aEl))
        {
            attemptId = aEl.GetString();
        }
        if (string.IsNullOrWhiteSpace(idempKey) && metaEl.TryGetProperty("idempotency_key", out JsonElement iEl))
        {
            idempKey = iEl.GetString();
        }
        if (string.IsNullOrWhiteSpace(orderId) && metaEl.TryGetProperty("order_id", out JsonElement oEl))
        {
            orderId = oEl.GetString();
        }
    }
}

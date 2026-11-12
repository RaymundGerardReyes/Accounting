using System.Globalization;
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> SettledOrders = new(StringComparer.OrdinalIgnoreCase);

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
        string? topDataId = null;
        string? checkoutSessionId = null;
        string? extractedSourceId = null;
        string? extractedPaymentIntentId = null;
        string? extractedPaymentId = null;
        string? extractedReferenceNumber = null;
        string? metadataAttemptId = null;
        string? metadataIdempotencyKey = null;
        string? metadataOrderId = null;
        string? failureCode = null;
        string? failureMessage = null;
        string? actualPaymentMethod = null;
        DateTimeOffset? extractedPaidAt = null;

#pragma warning disable CA1508 // False positive dead code analysis across complex JSON DOM navigation
        try
        {
            using JsonDocument doc = JsonDocument.Parse(command.PayloadJson);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                webhookEvent.MarkIgnored(nowUtc);
                await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);
                return Result.Success(new ProcessWebhookEventResponse(
                    Success: true,
                    ProviderEventId: command.ProviderEventId,
                    ProcessingStatus: WebhookProcessingStatus.Ignored,
                    Message: $"Webhook payload root is not a JSON object ({root.ValueKind}). Event ignored."));
            }

            JsonElement targetAttrs = default;

            if (root.TryGetProperty("data", out JsonElement dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                topDataId = GetStringProperty(dataEl, "id");

                if (dataEl.TryGetProperty("attributes", out JsonElement attrEl) && attrEl.ValueKind == JsonValueKind.Object)
                {
                    if (attrEl.TryGetProperty("data", out JsonElement innerData) && innerData.ValueKind == JsonValueKind.Object)
                    {
                        resourceId = GetStringProperty(innerData, "id");
                        if (innerData.TryGetProperty("attributes", out JsonElement innerAttrs) && innerAttrs.ValueKind == JsonValueKind.Object)
                        {
                            targetAttrs = innerAttrs;
                        }
                    }
                    else
                    {
                        targetAttrs = attrEl;
                    }
                }
            }

            resourceId ??= topDataId;

            if (resourceId is not null)
            {
                if (resourceId.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
                    checkoutSessionId ??= resourceId;
                else if (resourceId.StartsWith("src_", StringComparison.OrdinalIgnoreCase))
                    extractedSourceId ??= resourceId;
                else if (resourceId.StartsWith("pi_", StringComparison.OrdinalIgnoreCase))
                    extractedPaymentIntentId ??= resourceId;
                else if (resourceId.StartsWith("pay_", StringComparison.OrdinalIgnoreCase))
                    extractedPaymentId ??= resourceId;
            }

            if (topDataId is not null && !topDataId.StartsWith("evt_", StringComparison.OrdinalIgnoreCase))
            {
                if (topDataId.StartsWith("cs_", StringComparison.OrdinalIgnoreCase))
                    checkoutSessionId ??= topDataId;
                else if (topDataId.StartsWith("src_", StringComparison.OrdinalIgnoreCase))
                    extractedSourceId ??= topDataId;
                else if (topDataId.StartsWith("pi_", StringComparison.OrdinalIgnoreCase))
                    extractedPaymentIntentId ??= topDataId;
                else if (topDataId.StartsWith("pay_", StringComparison.OrdinalIgnoreCase))
                    extractedPaymentId ??= topDataId;
            }

            if (targetAttrs.ValueKind == JsonValueKind.Object)
            {
                checkoutSessionId ??= GetStringProperty(targetAttrs, "checkout_session_id");
                if (string.IsNullOrWhiteSpace(checkoutSessionId) && targetAttrs.TryGetProperty("checkout_session", out JsonElement csObj))
                {
                    if (csObj.ValueKind == JsonValueKind.String)
                        checkoutSessionId = csObj.GetString()?.Trim();
                    else if (csObj.ValueKind == JsonValueKind.Object)
                        checkoutSessionId = GetStringProperty(csObj, "id");
                }

                extractedPaymentId ??= GetStringProperty(targetAttrs, "payment_id");
                if (string.IsNullOrWhiteSpace(extractedPaymentId) && targetAttrs.TryGetProperty("payment", out JsonElement payObj))
                {
                    if (payObj.ValueKind == JsonValueKind.String)
                        extractedPaymentId = payObj.GetString()?.Trim();
                    else if (payObj.ValueKind == JsonValueKind.Object)
                        extractedPaymentId = GetStringProperty(payObj, "id");
                }

                extractedSourceId ??= ExtractSourceId(targetAttrs);

                extractedPaymentIntentId ??= ExtractPaymentIntentId(targetAttrs);

                if (string.IsNullOrWhiteSpace(metadataOrderId))
                {
                    metadataOrderId = GetStringProperty(targetAttrs, "order_id");
                }

                if (string.IsNullOrWhiteSpace(extractedReferenceNumber))
                {
                    extractedReferenceNumber = GetStringProperty(targetAttrs, "external_reference_number")
                        ?? GetStringProperty(targetAttrs, "order_number")
                        ?? GetStringProperty(targetAttrs, "reference_number");
                }

                failureCode ??= GetStringProperty(targetAttrs, "failed_code") ?? GetStringProperty(targetAttrs, "failure_code");
                failureMessage ??= GetStringProperty(targetAttrs, "failed_message") ?? GetStringProperty(targetAttrs, "failure_message");

                actualPaymentMethod ??= GetStringProperty(targetAttrs, "payment_method_used");

                if (targetAttrs.TryGetProperty("paid_at", out JsonElement paidAtEl))
                {
                    extractedPaidAt = SafeParseUnixTimestamp(paidAtEl);
                }

                if (targetAttrs.TryGetProperty("metadata", out JsonElement metaEl))
                {
                    ExtractMetadata(metaEl, ref metadataAttemptId, ref metadataIdempotencyKey, ref metadataOrderId, ref extractedReferenceNumber);
                }

                if (string.IsNullOrWhiteSpace(extractedReferenceNumber))
                {
                    extractedReferenceNumber = GetStringProperty(targetAttrs, "description");
                }

                if (targetAttrs.TryGetProperty("payments", out JsonElement paymentsEl) &&
                    paymentsEl.ValueKind == JsonValueKind.Array &&
                    paymentsEl.GetArrayLength() > 0)
                {
                    foreach (JsonElement payEl in paymentsEl.EnumerateArray())
                    {
                        if (payEl.ValueKind != JsonValueKind.Object) continue;

                        extractedPaymentId ??= GetStringProperty(payEl, "id");
                        extractedSourceId ??= ExtractSourceId(payEl);
                        extractedPaymentIntentId ??= ExtractPaymentIntentId(payEl);

                        if (payEl.TryGetProperty("attributes", out JsonElement payAttrs) &&
                            payAttrs.ValueKind == JsonValueKind.Object)
                        {
                            extractedPaymentId ??= GetStringProperty(payAttrs, "id");
                            extractedSourceId ??= ExtractSourceId(payAttrs);
                            extractedPaymentIntentId ??= ExtractPaymentIntentId(payAttrs);

                            failureCode ??= GetStringProperty(payAttrs, "failed_code") ?? GetStringProperty(payAttrs, "failure_code");
                            failureMessage ??= GetStringProperty(payAttrs, "failed_message") ?? GetStringProperty(payAttrs, "failure_message");

                            actualPaymentMethod ??= GetStringProperty(payAttrs, "payment_method_used");

                            if (extractedPaidAt is null && payAttrs.TryGetProperty("paid_at", out JsonElement pPaidAt))
                            {
                                extractedPaidAt = SafeParseUnixTimestamp(pPaidAt);
                            }

                            if (payAttrs.TryGetProperty("metadata", out JsonElement pm))
                            {
                                ExtractMetadata(pm, ref metadataAttemptId, ref metadataIdempotencyKey, ref metadataOrderId, ref extractedReferenceNumber);
                            }
                        }
                    }
                }

                if (targetAttrs.TryGetProperty("payment_intent", out JsonElement piElOuter) &&
                    piElOuter.ValueKind == JsonValueKind.Object)
                {
                    extractedPaymentIntentId ??= GetStringProperty(piElOuter, "id");

                    if (piElOuter.TryGetProperty("attributes", out JsonElement piAttrs) &&
                        piAttrs.ValueKind == JsonValueKind.Object)
                    {
                        extractedPaymentIntentId ??= ExtractPaymentIntentId(piAttrs);
                        extractedSourceId ??= ExtractSourceId(piAttrs);

                        if (piAttrs.TryGetProperty("metadata", out JsonElement pim))
                        {
                            ExtractMetadata(pim, ref metadataAttemptId, ref metadataIdempotencyKey, ref metadataOrderId, ref extractedReferenceNumber);
                        }
                        if (piAttrs.TryGetProperty("last_payment_error", out JsonElement lpe) &&
                            lpe.ValueKind == JsonValueKind.Object)
                        {
                            failureCode ??= GetStringProperty(lpe, "code") ?? GetStringProperty(lpe, "failed_code");
                            failureMessage ??= GetStringProperty(lpe, "message") ?? GetStringProperty(lpe, "failed_message");
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
        }
#pragma warning restore CA1508

        // Multi-Key Correlation Cascade:
        // 1. Explicit attempt_id / payment_attempt_id from metadata
        PaymentAttempt? attempt = null;
        if (!string.IsNullOrWhiteSpace(metadataAttemptId))
        {
            attempt = await _attemptRepository.GetByIdAsync(metadataAttemptId, cancellationToken).ConfigureAwait(false);
        }

        // 2. Direct provider payment ID matching (resourceId)
        if (attempt is null && !string.IsNullOrWhiteSpace(resourceId))
        {
            attempt = await _attemptRepository.GetByProviderPaymentIdAsync(resourceId, cancellationToken).ConfigureAwait(false);
        }

        // 3. Extracted checkout_session_id from payment event
        if (attempt is null && !string.IsNullOrWhiteSpace(checkoutSessionId))
        {
            attempt = await _attemptRepository.GetByProviderPaymentIdAsync(checkoutSessionId, cancellationToken).ConfigureAwait(false);
        }

        // 4. Extracted payment_id from payment event or payments array
        if (attempt is null && !string.IsNullOrWhiteSpace(extractedPaymentId))
        {
            attempt = await _attemptRepository.GetByProviderPaymentIdAsync(extractedPaymentId, cancellationToken).ConfigureAwait(false);
        }

        // 5. Extracted source ID (src_...) from payment resource, payments array, or source object/string
        if (attempt is null && !string.IsNullOrWhiteSpace(extractedSourceId))
        {
            attempt = await _attemptRepository.GetByProviderPaymentIdAsync(extractedSourceId, cancellationToken).ConfigureAwait(false);
        }

        // 6. Extracted payment intent ID (pi_...) from payment resource or payment_intent object/string
        if (attempt is null && !string.IsNullOrWhiteSpace(extractedPaymentIntentId))
        {
            attempt = await _attemptRepository.GetByProviderPaymentIdAsync(extractedPaymentIntentId, cancellationToken).ConfigureAwait(false);
        }

        // 7. Idempotency Key matching from metadata
        if (attempt is null && !string.IsNullOrWhiteSpace(metadataIdempotencyKey))
        {
            attempt = await _attemptRepository.GetByIdempotencyKeyAsync(metadataIdempotencyKey, cancellationToken).ConfigureAwait(false);
        }

        // 8. Order ID matching from metadata
        if (attempt is null && !string.IsNullOrWhiteSpace(metadataOrderId))
        {
            attempt = await _attemptRepository.GetLatestByOrderIdAsync(metadataOrderId, cancellationToken).ConfigureAwait(false);
        }

        // 9. Order Number / Reference matching from order_number, external_reference_number, description
        if (attempt is null && !string.IsNullOrWhiteSpace(extractedReferenceNumber))
        {
            Order? refOrder = await _orderRepository.GetByOrderNumberAsync(extractedReferenceNumber.Trim(), cancellationToken).ConfigureAwait(false);
            if (refOrder is not null)
            {
                attempt = await _attemptRepository.GetLatestByOrderIdAsync(refOrder.Id, cancellationToken).ConfigureAwait(false);
            }
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
        if (order is null && !string.IsNullOrWhiteSpace(metadataOrderId))
        {
            order = await _orderRepository.GetByIdAsync(metadataOrderId, cancellationToken).ConfigureAwait(false);
        }
        if (order is null && !string.IsNullOrWhiteSpace(extractedReferenceNumber))
        {
            order = await _orderRepository.GetByOrderNumberAsync(extractedReferenceNumber.Trim(), cancellationToken).ConfigureAwait(false);
        }

        if (command.EventType is "checkout_session.payment.paid" or "payment.paid" or "qr.paid")
        {
            DateTimeOffset paidAtUtc = extractedPaidAt ?? nowUtc;
            string paymentMethod = actualPaymentMethod ?? attempt.PaymentMethod;
            attempt.MarkSucceeded(paidAtUtc, paymentMethod);

            if (order is not null && order.Status != OrderStatus.Paid)
            {
                if (SettledOrders.TryAdd(order.Id, 0))
                {
                    order.MarkPaid(paidAtUtc);
                    await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);

                    var paidEvent = OrderPaidIntegrationEvent.Create(
                        orderId: order.Id,
                        orderNumber: order.OrderNumber,
                        paymentAttemptId: attempt.Id,
                        amountMinor: order.Amount.AmountMinor,
                        currency: order.Amount.CurrencyCode,
                        paymentMethod: attempt.PaymentMethod,
                        customerName: string.Empty,
                        customerEmail: string.Empty,
                        paidAtUtc: paidAtUtc,
                        metadataJson: order.MetadataJson);

                    await _outboxWriter.EnqueueAsync(paidEvent, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        else if (command.EventType is "source.chargeable")
        {
            if (attempt.Status != PaymentAttemptStatus.Succeeded)
            {
                attempt.MarkProcessing(nowUtc);
            }
        }
        else if (command.EventType is "checkout_session.expired" or "qrph.expired" or "qr_code.expired" or "qr.expired")
        {
            if (attempt.Status != PaymentAttemptStatus.Succeeded)
            {
                attempt.MarkExpired(nowUtc, failureCode ?? "SESSION_EXPIRED", failureMessage ?? "Checkout session expired before payment completion.");

                if (_paymentGateway is not null &&
                    order?.Status != OrderStatus.Paid &&
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
        }
        else if (command.EventType is "payment.failed")
        {
            if (attempt.Status != PaymentAttemptStatus.Succeeded)
            {
                attempt.MarkFailed(failureCode ?? FailureCode.CardDeclined, failureMessage ?? "Payment failed", nowUtc);

                if (_paymentGateway is not null &&
                    order?.Status != OrderStatus.Paid &&
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
        }
        else if (command.EventType is "payment.refunded" or "payment.refund.updated")
        {
            if (order is not null && order.Status != OrderStatus.Refunded)
            {
                order.MarkRefunded(nowUtc);
                await _orderRepository.SaveAsync(order, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            webhookEvent.MarkIgnored(nowUtc);
            await _webhookStore.SaveAsync(webhookEvent, cancellationToken).ConfigureAwait(false);
            return Result.Success(new ProcessWebhookEventResponse(
                Success: true,
                ProviderEventId: command.ProviderEventId,
                ProcessingStatus: WebhookProcessingStatus.Ignored,
                Message: $"Unhandled event type '{command.EventType}' safely ignored."));
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
            Message: $"Webhook event '{command.ProviderEventId}' processed for attempt '{attempt.Id}'.",
            PaymentAttemptId: attempt.Id));
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                string? value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            else if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt64(out long intVal))
                {
                    return intVal.ToString(CultureInfo.InvariantCulture);
                }
                return prop.GetRawText().Trim();
            }
        }
        return null;
    }

    private static string? ExtractSourceId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? sid = GetStringProperty(element, "source_id");
        if (!string.IsNullOrWhiteSpace(sid))
        {
            return sid;
        }

        if (element.TryGetProperty("source", out JsonElement srcEl))
        {
            if (srcEl.ValueKind == JsonValueKind.String)
            {
                string? val = srcEl.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val.Trim();
                }
            }
            else if (srcEl.ValueKind == JsonValueKind.Object)
            {
                string? nestedSid = GetStringProperty(srcEl, "id");
                if (!string.IsNullOrWhiteSpace(nestedSid))
                {
                    return nestedSid;
                }
            }
        }

        return null;
    }

    private static string? ExtractPaymentIntentId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? piId = GetStringProperty(element, "payment_intent_id");
        if (!string.IsNullOrWhiteSpace(piId))
        {
            return piId;
        }

        if (element.TryGetProperty("payment_intent", out JsonElement piEl))
        {
            if (piEl.ValueKind == JsonValueKind.String)
            {
                string? val = piEl.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val.Trim();
                }
            }
            else if (piEl.ValueKind == JsonValueKind.Object)
            {
                string? nestedPiId = GetStringProperty(piEl, "id");
                if (!string.IsNullOrWhiteSpace(nestedPiId))
                {
                    return nestedPiId;
                }
            }
        }

        return null;
    }

    private static void ExtractMetadata(
        JsonElement metaEl,
        ref string? attemptId,
        ref string? idempKey,
        ref string? orderId,
        ref string? orderNumber)
    {
        if (metaEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(attemptId))
        {
            attemptId = GetStringProperty(metaEl, "attempt_id")
                ?? GetStringProperty(metaEl, "payment_attempt_id")
                ?? GetStringProperty(metaEl, "attemptId")
                ?? GetStringProperty(metaEl, "paymentAttemptId");
        }

        if (string.IsNullOrWhiteSpace(idempKey))
        {
            idempKey = GetStringProperty(metaEl, "idempotency_key")
                ?? GetStringProperty(metaEl, "idempotencyKey");
        }

        if (string.IsNullOrWhiteSpace(orderId))
        {
            orderId = GetStringProperty(metaEl, "order_id")
                ?? GetStringProperty(metaEl, "orderId");
        }

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            orderNumber = GetStringProperty(metaEl, "order_number")
                ?? GetStringProperty(metaEl, "orderNumber")
                ?? GetStringProperty(metaEl, "external_reference_number")
                ?? GetStringProperty(metaEl, "externalReferenceNumber")
                ?? GetStringProperty(metaEl, "reference_number")
                ?? GetStringProperty(metaEl, "referenceNumber");
        }
    }

    private static DateTimeOffset? SafeParseUnixTimestamp(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out long val) || val <= 0)
        {
            return null;
        }

        try
        {
            if (val > 253_402_300_799L)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(val);
            }

            return DateTimeOffset.FromUnixTimeSeconds(val);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.Integrations.Application.Ports;
using Rge.Payments.Contracts.Abstractions;
using Rge.Payments.Contracts.Events;

namespace Rge.Integrations.Application.Kotobee;

public sealed class OnOrderPaidEnqueueKotobeeHandler
{
    private readonly IKotobeeFulfillmentQueue _queue;
    private readonly IOrderLookup? _orderLookup;
    private readonly ILogger<OnOrderPaidEnqueueKotobeeHandler> _logger;

    public OnOrderPaidEnqueueKotobeeHandler(
        IKotobeeFulfillmentQueue queue,
        ILogger<OnOrderPaidEnqueueKotobeeHandler> logger,
        IOrderLookup? orderLookup = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orderLookup = orderLookup;
    }

    /// <summary>
    /// Direct in-memory event dispatch handler.
    /// </summary>
    public async Task HandleAsync(OrderPaidIntegrationEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var skus = await ResolveSkusForOrderAsync(evt.OrderId, null, ct).ConfigureAwait(false);
        var job = new KotobeeFulfillmentJob(
            OrderId: evt.OrderId,
            BuyerEmail: evt.CustomerEmail,
            BuyerName: evt.CustomerName,
            PurchasedProductSkus: skus
        );

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Enqueueing Kotobee fulfillment job for Order {OrderId} ({SkuCount} SKUs).", evt.OrderId, skus.Count);
        }
        await _queue.EnqueueAsync(job, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Outbox message dispatch handler for 'order.paid' integration events.
    /// </summary>
    public async Task HandleOutboxMessageAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        string? orderId = message.OrderId;
        string customerEmail = string.Empty;
        string customerName = "Valued Customer";

        if (!string.IsNullOrWhiteSpace(message.PayloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(message.PayloadJson);
                var root = doc.RootElement;
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    orderId = TryGetString(root, "OrderId", "order_id", "orderId");
                }
                customerEmail = TryGetString(root, "CustomerEmail", "customer_email", "email", "buyer_email") ?? string.Empty;
                customerName = TryGetString(root, "CustomerName", "customer_name", "name", "buyer_name") ?? "Valued Customer";
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not parse OutboxMessage payload JSON for Kotobee enqueue.");
            }
        }

        if (string.IsNullOrWhiteSpace(orderId))
        {
            _logger.LogWarning("Outbox message {MessageId} missing OrderId; skipping Kotobee fulfillment.", message.Id);
            return;
        }

        var skus = await ResolveSkusForOrderAsync(orderId, message.PayloadJson, ct).ConfigureAwait(false);

        var job = new KotobeeFulfillmentJob(
            OrderId: orderId,
            BuyerEmail: customerEmail,
            BuyerName: customerName,
            PurchasedProductSkus: skus
        );

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Enqueueing Kotobee fulfillment job from outbox for Order {OrderId} ({SkuCount} SKUs).", orderId, skus.Count);
        }
        await _queue.EnqueueAsync(job, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ResolveSkusForOrderAsync(string orderId, string? payloadJson, CancellationToken ct)
    {
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Try order lookup in database
        if (_orderLookup is not null)
        {
            try
            {
                var order = await _orderLookup.GetOrderByIdAsync(orderId, ct).ConfigureAwait(false);
                if (order is not null)
                {
                    if (!string.IsNullOrWhiteSpace(order.ReferenceCode))
                    {
                        skus.Add(order.ReferenceCode.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(order.Title))
                    {
                        skus.Add(order.Title.Trim());
                    }

                    ExtractSkusFromJson(order.MetadataJson, skus);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed querying IOrderLookup for Order {OrderId}.", orderId);
            }
        }

        // 2. Extract any SKUs in payload JSON
        ExtractSkusFromJson(payloadJson, skus);

        return skus.ToList();
    }

    private static void ExtractSkusFromJson(string? json, HashSet<string> target)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("skus", out var skusProp) && skusProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in skusProp.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) target.Add(s.Trim());
                }
            }

            if (root.TryGetProperty("line_items", out var lineItemsProp) && lineItemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in lineItemsProp.EnumerateArray())
                {
                    var s = TryGetString(item, "product_code", "productCode", "sku", "id", "name");
                    if (!string.IsNullOrWhiteSpace(s)) target.Add(s.Trim());
                }
            }

            var singleSku = TryGetString(root, "product_code", "productCode", "sku", "product_sku");
            if (!string.IsNullOrWhiteSpace(singleSku))
            {
                target.Add(singleSku.Trim());
            }
        }
        catch
        {
            // Ignore format errors
        }
    }

    private static string? TryGetString(JsonElement el, params string[] propertyNames)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var propName in propertyNames)
        {
            if (el.TryGetProperty(propName, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString();
            }
        }
        return null;
    }
}

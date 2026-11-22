using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;
using Rge.Payments.Contracts.Abstractions;

namespace Rge.Integrations.Infrastructure.Kotobee;

public sealed class OrderKotobeeEligibilityChecker : IKotobeeOrderEligibilityChecker
{
    private readonly IOrderLookup? _orderLookup;
    private readonly IKotobeeCatalogMappingResolver _mappingResolver;
    private readonly ILogger<OrderKotobeeEligibilityChecker> _logger;

    public OrderKotobeeEligibilityChecker(
        IKotobeeCatalogMappingResolver mappingResolver,
        ILogger<OrderKotobeeEligibilityChecker> logger,
        IOrderLookup? orderLookup = null)
    {
        _mappingResolver = mappingResolver ?? throw new ArgumentNullException(nameof(mappingResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orderLookup = orderLookup;
    }

    public async Task<bool> OrderHasMappedKotobeeItemsAsync(string orderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return false;
        }

        if (_orderLookup is null)
        {
            // If order lookup is not registered, assume potentially eligible so we don't prematurely hide
            return true;
        }

        try
        {
            var order = await _orderLookup.GetOrderByIdAsync(orderId.Trim(), ct).ConfigureAwait(false);
            if (order is null)
            {
                order = await _orderLookup.GetOrderByOrderNumberAsync(orderId.Trim(), ct).ConfigureAwait(false);
            }

            if (order is null)
            {
                _logger.LogWarning("Order {OrderId} not found while checking Kotobee eligibility.", orderId);
                return false;
            }

            var candidateSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(order.ReferenceCode))
            {
                candidateSkus.Add(order.ReferenceCode.Trim());
            }
            if (!string.IsNullOrWhiteSpace(order.Title))
            {
                candidateSkus.Add(order.Title.Trim());
            }

            ExtractSkusFromJson(order.MetadataJson, candidateSkus);

            var resolved = _mappingResolver.ResolveBooksForOrder(candidateSkus);
            return resolved.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Kotobee eligibility for Order {OrderId}.", orderId);
            return true; // Fall back to true on error so we don't hide improperly
        }
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

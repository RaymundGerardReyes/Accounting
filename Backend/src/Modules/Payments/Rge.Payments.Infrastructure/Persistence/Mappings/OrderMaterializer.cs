using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Infrastructure.Persistence.Mappings;

public sealed record OrderRow
{
    public string Id { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public string ReferenceCode { get; init; } = string.Empty;
    public long AmountMinor { get; init; }
    public string Currency { get; init; } = "PHP";
    public string Status { get; init; } = "pending";
    public string? MetadataJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? Description { get; init; }
    public long? SubtotalMinor { get; init; }
    public long? VatMinor { get; init; }
    public string SourceChannel { get; init; } = "checkout";
    public string? ExternalReferenceId { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed class OrderJoinedRow
{
    public int TotalCount { get; init; }
    public string? Id { get; init; }
    public string? UserId { get; init; }
    public string? OrderNumber { get; init; }
    public string? Title { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
    public long SubtotalCentavos { get; init; }
    public long VatCentavos { get; init; }
    public long TotalCentavos { get; init; }
    public string? Currency { get; init; }
    public string? MetadataJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public string? LineItemId { get; init; }
    public string? LineItemOrderId { get; init; }
    public string? ProductCode { get; init; }
    public string? ProductName { get; init; }
    public int Quantity { get; init; }
    public long UnitPriceCentavos { get; init; }
    public long LineTotalCentavos { get; init; }
}

public sealed record MaterializedOrderPage(
    IReadOnlyList<Order> Orders,
    int TotalCount);

public static class OrderMaterializer
{
    public static MaterializedOrderPage MaterializePage(IEnumerable<OrderJoinedRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var orderedKeys = new List<string>(capacity: 24);
        var accumulators = new Dictionary<string, (OrderJoinedRow Header, List<object> Lines)>(StringComparer.OrdinalIgnoreCase);
        var totalCount = 0;

        foreach (var row in rows)
        {
            totalCount = row.TotalCount;

            if (string.IsNullOrWhiteSpace(row.Id))
            {
                continue;
            }

            var orderId = row.Id.Trim();
            if (!accumulators.TryGetValue(orderId, out var entry))
            {
                entry = (row, new List<object>(capacity: 4));
                accumulators.Add(orderId, entry);
                orderedKeys.Add(orderId);
            }
        }

        var orders = new List<Order>(orderedKeys.Count);
        foreach (var orderId in orderedKeys)
        {
            var (header, _) = accumulators[orderId];
            var row = new OrderRow
            {
                Id = header.Id ?? orderId,
                UserId = header.UserId ?? header.CustomerEmail ?? "anonymous",
                OrderNumber = header.OrderNumber ?? string.Empty,
                Title = header.Title ?? header.OrderNumber ?? "Order",
                Category = header.Category ?? "General",
                ReferenceCode = header.OrderNumber ?? string.Empty,
                AmountMinor = header.TotalCentavos,
                Currency = header.Currency ?? "PHP",
                Status = header.Status ?? "pending",
                MetadataJson = header.MetadataJson ?? "{}",
                CreatedAt = header.CreatedAt,
                UpdatedAt = header.UpdatedAt,
                CustomerName = header.CustomerName,
                CustomerEmail = header.CustomerEmail,
                SubtotalMinor = header.SubtotalCentavos,
                VatMinor = header.VatCentavos
            };
            orders.Add(ToDomain(row));
        }

        return new MaterializedOrderPage(orders, totalCount);
    }

    public static async Task<Order> MaterializeAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var isNull = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false);
            dict[name] = isNull ? null : reader.GetValue(i);
        }

        var materialized = Materialize(dict);
        if (materialized is not null)
        {
            return materialized;
        }

        string id = reader.GetString(0);
        string userId = reader.GetString(1);
        string orderNumber = reader.GetString(2);
        string title = reader.GetString(3);
        string category = reader.GetString(4);
        string referenceCode = reader.GetString(5);
        long amountMinor = reader.GetInt64(6);
        string currency = reader.GetString(7);
        string status = reader.GetString(8);
        string metadataJson = await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? "{}" : reader.GetString(9);
        DateTimeOffset createdAt = await reader.GetFieldValueAsync<DateTimeOffset>(10, cancellationToken).ConfigureAwait(false);
        DateTimeOffset updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(11, cancellationToken).ConfigureAwait(false);

        return Order.Rehydrate(
            id: id,
            userId: userId,
            orderNumber: orderNumber,
            title: title,
            category: category,
            referenceCode: referenceCode,
            amount: new Money(amountMinor, currency),
            status: status,
            metadataJson: metadataJson,
            createdAt: createdAt,
            updatedAt: updatedAt);
    }

    public static Order? Materialize(object? rawRow)
    {
        if (rawRow is null)
        {
            return null;
        }

        if (rawRow is Order existingOrder)
        {
            return existingOrder;
        }

        var row = rawRow is OrderRow typedRow
            ? typedRow
            : BindOrderRow(rawRow);

        if (string.IsNullOrWhiteSpace(row.Id) && string.IsNullOrWhiteSpace(row.OrderNumber))
        {
            return null;
        }

        return ToDomain(row);
    }

    public static Order ToDomain(OrderRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var metadata = ExtractMetadata(row.MetadataJson);

        var id = !string.IsNullOrWhiteSpace(row.Id)
            ? row.Id.Trim()
            : Guid.NewGuid().ToString();

        var orderNumberText = !string.IsNullOrWhiteSpace(row.OrderNumber)
            ? row.OrderNumber.Trim()
            : $"ORD-{id.Replace("-", "", StringComparison.Ordinal)[..Math.Min(8, id.Length)].ToUpperInvariant()}";

        var userId = !string.IsNullOrWhiteSpace(row.UserId)
            ? row.UserId.Trim()
            : (!string.IsNullOrWhiteSpace(row.CustomerEmail) ? row.CustomerEmail.Trim() : "anonymous");

        var customerEmail = !string.IsNullOrWhiteSpace(row.CustomerEmail)
            ? row.CustomerEmail.Trim()
            : (userId.Contains('@', StringComparison.Ordinal) ? userId : MetadataValueOrDefault(metadata, "customer_email", "respondent_email") ?? string.Empty);

        var customerName = !string.IsNullOrWhiteSpace(row.CustomerName)
            ? row.CustomerName.Trim()
            : (MetadataValueOrDefault(metadata, "customer_name", "full_name") ?? "Customer");

        var title = !string.IsNullOrWhiteSpace(row.Title)
            ? row.Title.Trim()
            : (!string.IsNullOrWhiteSpace(row.Description) ? row.Description.Trim() : "Order Item");

        var description = !string.IsNullOrWhiteSpace(row.Description)
            ? row.Description.Trim()
            : title;

        var referenceCode = !string.IsNullOrWhiteSpace(row.ReferenceCode)
            ? row.ReferenceCode.Trim()
            : (row.ExternalReferenceId ?? orderNumberText);

        var sourceChannel = !string.IsNullOrWhiteSpace(row.SourceChannel)
            ? row.SourceChannel.Trim()
            : (metadata.TryGetValue("source", out var src) && !string.IsNullOrWhiteSpace(src) ? src : "checkout");

        var externalReferenceId = !string.IsNullOrWhiteSpace(row.ExternalReferenceId)
            ? row.ExternalReferenceId.Trim()
            : (metadata.TryGetValue("response_id", out var respId) ? respId : null);

        var idempotencyKey = !string.IsNullOrWhiteSpace(row.IdempotencyKey)
            ? row.IdempotencyKey.Trim()
            : (externalReferenceId is not null ? $"gform:{externalReferenceId}" : null);

        var currencyCode = !string.IsNullOrWhiteSpace(row.Currency)
            ? row.Currency.Trim().ToUpperInvariant()
            : "PHP";

        var amountMinor = Math.Max(row.AmountMinor, 0L);
        var vatMinor = row.VatMinor.HasValue && row.VatMinor.Value >= 0
            ? row.VatMinor.Value
            : (long)Math.Round(amountMinor * 12.0 / 112.0, MidpointRounding.AwayFromZero);

        var subtotalMinor = row.SubtotalMinor.HasValue && row.SubtotalMinor.Value >= 0
            ? row.SubtotalMinor.Value
            : Math.Max(amountMinor - vatMinor, 0L);

        var createdAt = row.CreatedAt == default ? DateTimeOffset.UtcNow : row.CreatedAt;
        var updatedAt = row.UpdatedAt == default ? createdAt : row.UpdatedAt;

        var normalizedRow = row with
        {
            Id = id,
            UserId = userId,
            OrderNumber = orderNumberText,
            Title = title,
            Category = string.IsNullOrWhiteSpace(row.Category) ? "General" : row.Category.Trim(),
            ReferenceCode = referenceCode,
            AmountMinor = amountMinor,
            Currency = currencyCode,
            Status = NormalizeStatusString(row.Status),
            MetadataJson = string.IsNullOrWhiteSpace(row.MetadataJson) ? "{}" : row.MetadataJson,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            Description = description,
            SubtotalMinor = subtotalMinor,
            VatMinor = vatMinor,
            SourceChannel = sourceChannel,
            ExternalReferenceId = externalReferenceId,
            IdempotencyKey = idempotencyKey
        };

        return InstantiateOrderAggregate(normalizedRow, metadata);
    }

    public static IReadOnlyDictionary<string, string> ExtractMetadata(string? metadataJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                    _ => prop.Value.GetRawText()
                };
            }
        }
        catch (JsonException)
        {
            result["raw"] = metadataJson;
        }

        return result;
    }

    private static OrderRow BindOrderRow(object rawRow)
    {
        if (rawRow is IDictionary<string, object?> dict)
        {
            return new OrderRow
            {
                Id = GetString(dict, "id", "order_id", "Id", "OrderId") ?? string.Empty,
                UserId = GetString(dict, "user_id", "customer_id", "UserId", "CustomerId") ?? string.Empty,
                OrderNumber = GetString(dict, "order_number", "OrderNumber") ?? string.Empty,
                Title = GetString(dict, "title", "Title", "item_description") ?? string.Empty,
                Category = GetString(dict, "category", "Category") ?? "General",
                ReferenceCode = GetString(dict, "reference_code", "ReferenceCode", "response_id") ?? string.Empty,
                AmountMinor = GetInt64(dict, "amount_minor", "total_centavos", "AmountMinor", "TotalCentavos") ?? 0L,
                Currency = GetString(dict, "currency", "Currency") ?? "PHP",
                Status = GetString(dict, "status", "Status") ?? "pending",
                MetadataJson = GetString(dict, "metadata_json", "MetadataJson", "raw_payload_json"),
                CreatedAt = GetDateTimeOffset(dict, "created_at", "CreatedAt", "submitted_at"),
                UpdatedAt = GetDateTimeOffset(dict, "updated_at", "UpdatedAt", "created_at"),
                CustomerName = GetString(dict, "customer_name", "CustomerName"),
                CustomerEmail = GetString(dict, "customer_email", "CustomerEmail", "respondent_email"),
                Description = GetString(dict, "description", "Description", "title"),
                SubtotalMinor = GetInt64(dict, "subtotal_minor", "subtotal_centavos", "SubtotalMinor", "SubtotalCentavos"),
                VatMinor = GetInt64(dict, "vat_minor", "vat_centavos", "VatMinor", "VatCentavos"),
                SourceChannel = GetString(dict, "source_channel", "SourceChannel") ?? "checkout",
                ExternalReferenceId = GetString(dict, "external_reference_id", "ExternalReferenceId", "response_id"),
                IdempotencyKey = GetString(dict, "idempotency_key", "IdempotencyKey")
            };
        }

        var props = rawRow.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propMap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            if (p.CanRead)
            {
                propMap[p.Name] = p.GetValue(rawRow);
            }
        }

        return BindOrderRow(propMap);
    }

    private static Order InstantiateOrderAggregate(
        OrderRow row,
        IReadOnlyDictionary<string, string> metadata)
    {
        var orderType = typeof(Order);

        // 1. Prefer static factory methods (Rehydrate / Reconstitute / FromPersistence / Create) if defined on Order
        var staticFactories = orderType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var method in staticFactories)
        {
            if (method.ReturnType == orderType &&
                (method.Name.Equals("Rehydrate", StringComparison.OrdinalIgnoreCase) ||
                 method.Name.Equals("Reconstitute", StringComparison.OrdinalIgnoreCase) ||
                 method.Name.Equals("Restore", StringComparison.OrdinalIgnoreCase) ||
                 method.Name.Equals("FromPersistence", StringComparison.OrdinalIgnoreCase)))
            {
                if (TryInvokeMethodOrConstructor(method, row, metadata, out var factoryInstance) &&
                    factoryInstance is Order hydratedOrder)
                {
                    PopulateRemainingProperties(hydratedOrder, row, metadata);
                    return hydratedOrder;
                }
            }
        }

        // 2. Try matching constructors (public or non-public) ordered by parameter count descending
        var constructors = orderType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Array.Sort(constructors, (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));

        foreach (var ctor in constructors)
        {
            if (TryInvokeConstructor(ctor, row, metadata, out var ctorInstance) &&
                ctorInstance is Order constructedOrder)
            {
                PopulateRemainingProperties(constructedOrder, row, metadata);
                return constructedOrder;
            }
        }

        // 3. Fallback: allocate uninitialized Order aggregate and populate all backing fields/properties directly
        var instance = (Order)RuntimeHelpers.GetUninitializedObject(orderType);
        PopulateRemainingProperties(instance, row, metadata);
        return instance;
    }

    private static bool TryInvokeMethodOrConstructor(
        MethodInfo method,
        OrderRow row,
        IReadOnlyDictionary<string, string> metadata,
        out object? instance)
    {
        instance = null;
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            if (!TryResolveValue(parameters[i].Name ?? string.Empty, parameters[i].ParameterType, row, metadata, out args[i]))
            {
                return false;
            }
        }

        try
        {
            instance = method.Invoke(null, args);
            return instance is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInvokeConstructor(
        ConstructorInfo ctor,
        OrderRow row,
        IReadOnlyDictionary<string, string> metadata,
        out object? instance)
    {
        instance = null;
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            if (!TryResolveValue(parameters[i].Name ?? string.Empty, parameters[i].ParameterType, row, metadata, out args[i]))
            {
                return false;
            }
        }

        try
        {
            instance = ctor.Invoke(args);
            return instance is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void PopulateRemainingProperties(
        Order order,
        OrderRow row,
        IReadOnlyDictionary<string, string> metadata)
    {
        var type = typeof(Order);
        while (type is not null && type != typeof(object))
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!TryResolveValue(prop.Name, prop.PropertyType, row, metadata, out var resolvedValue) || resolvedValue is null)
                {
                    continue;
                }

                try
                {
                    if (prop.CanWrite)
                    {
                        prop.SetValue(order, resolvedValue);
                        continue;
                    }

                    var backingField = type.GetField($"<{prop.Name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                    backingField?.SetValue(order, resolvedValue);
                }
                catch
                {
                    // Ignore read-only calculated properties
                }
            }

            type = type.BaseType;
        }
    }

    private static bool TryResolveValue(
        string memberName,
        Type targetType,
        OrderRow row,
        IReadOnlyDictionary<string, string> metadata,
        out object? value)
    {
        var normalized = memberName.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

        object? raw = normalized switch
        {
            "id" or "orderid" => row.Id,
            "userid" or "customerid" => row.UserId,
            "ordernumber" => row.OrderNumber,
            "title" => row.Title,
            "category" => row.Category,
            "referencecode" => row.ReferenceCode,
            "amountminor" or "totalcentavos" or "totalamount" or "amount" or "total" => row.AmountMinor,
            "subtotalminor" or "subtotalcentavos" or "subtotal" => row.SubtotalMinor ?? row.AmountMinor,
            "vatminor" or "vatcentavos" or "vat" or "tax" => row.VatMinor ?? 0L,
            "currency" => row.Currency,
            "status" => row.Status,
            "metadatajson" => row.MetadataJson,
            "metadata" => metadata,
            "createdat" => row.CreatedAt,
            "updatedat" => row.UpdatedAt,
            "customername" => row.CustomerName,
            "customeremail" => row.CustomerEmail,
            "description" => row.Description,
            "sourcechannel" => row.SourceChannel,
            "externalreferenceid" => row.ExternalReferenceId,
            "idempotencykey" => row.IdempotencyKey,
            "lineitems" or "items" => BuildSynthesizedLineItems(row),
            _ => null
        };

        return TryConvertToTargetType(raw, targetType, row, metadata, out value);
    }

    private static bool TryConvertToTargetType(
        object? raw,
        Type targetType,
        OrderRow row,
        IReadOnlyDictionary<string, string> metadata,
        out object? converted)
    {
        converted = null;
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (raw is not null && underlyingType.IsInstanceOfType(raw))
        {
            converted = raw;
            return true;
        }

        if (underlyingType == typeof(string))
        {
            converted = raw?.ToString();
            return true;
        }

        if (underlyingType == typeof(long))
        {
            converted = raw is long l ? l : Convert.ToInt64(raw ?? 0L, CultureInfo.InvariantCulture);
            return true;
        }

        if (underlyingType == typeof(int))
        {
            converted = raw is int i ? i : Convert.ToInt32(raw ?? 0, CultureInfo.InvariantCulture);
            return true;
        }

        if (underlyingType == typeof(Guid))
        {
            var text = raw?.ToString();
            if (Guid.TryParse(text, out var parsedGuid))
            {
                converted = parsedGuid;
                return true;
            }

            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(text) ? row.Id : text));
            converted = new Guid(bytes.AsSpan(0, 16));
            return true;
        }

        if (underlyingType == typeof(DateTimeOffset))
        {
            converted = raw is DateTimeOffset dto ? dto : row.CreatedAt;
            return true;
        }

        if (underlyingType == typeof(DateTime))
        {
            converted = raw is DateTimeOffset dto ? dto.UtcDateTime : row.CreatedAt.UtcDateTime;
            return true;
        }

        if (underlyingType == typeof(IReadOnlyDictionary<string, string>) ||
            underlyingType == typeof(IDictionary<string, string>) ||
            underlyingType == typeof(Dictionary<string, string>))
        {
            converted = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
            return true;
        }

        if (underlyingType == typeof(Money))
        {
            var centavos = raw is long l ? l : row.AmountMinor;
            converted = CreateMoneyInstance(centavos, row.Currency);
            return converted is not null;
        }

        if (underlyingType == typeof(Currency))
        {
            converted = CreateCurrencyInstance(row.Currency);
            return converted is not null;
        }

        if (underlyingType == typeof(OrderNumber))
        {
            converted = CreateSingleStringValueObject(typeof(OrderNumber), row.OrderNumber);
            return converted is not null;
        }

        if (underlyingType.IsEnum)
        {
            var text = raw?.ToString() ?? string.Empty;
            if (Enum.TryParse(underlyingType, text, ignoreCase: true, out var enumVal))
            {
                converted = enumVal;
                return true;
            }

            var enumValues = Enum.GetValues(underlyingType);
            if (enumValues.Length > 0)
            {
                converted = enumValues.GetValue(0);
                return true;
            }
        }

        if (underlyingType == typeof(OrderStatus))
        {
            converted = CreateSingleStringValueObject(typeof(OrderStatus), row.Status);
            return converted is not null;
        }

        if (underlyingType == typeof(OrderCategory))
        {
            converted = CreateSingleStringValueObject(typeof(OrderCategory), row.Category);
            return converted is not null;
        }

        return false;
    }

    private static OrderLineItem[] BuildSynthesizedLineItems(OrderRow row)
    {
        var lineItemType = typeof(OrderLineItem);
        var ctors = lineItemType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var ctor in ctors)
        {
            var p = ctor.GetParameters();
            var args = new object?[p.Length];
            var ok = true;

            for (var i = 0; i < p.Length; i++)
            {
                var pName = (p[i].Name ?? string.Empty).ToLowerInvariant();
                if (pName.Contains("sku", StringComparison.OrdinalIgnoreCase) || pName.Contains("code", StringComparison.OrdinalIgnoreCase))
                {
                    args[i] = string.IsNullOrWhiteSpace(row.ReferenceCode) ? "ITEM-01" : row.ReferenceCode;
                }
                else if (pName.Contains("name", StringComparison.OrdinalIgnoreCase) || pName.Contains("title", StringComparison.OrdinalIgnoreCase) || pName.Contains("desc", StringComparison.OrdinalIgnoreCase))
                {
                    args[i] = string.IsNullOrWhiteSpace(row.Title) ? "Order Item" : row.Title;
                }
                else if (pName.Contains("qty", StringComparison.OrdinalIgnoreCase) || pName.Contains("quant", StringComparison.OrdinalIgnoreCase))
                {
                    args[i] = p[i].ParameterType == typeof(long) ? 1L : 1;
                }
                else if (p[i].ParameterType == typeof(Money))
                {
                    args[i] = CreateMoneyInstance(row.AmountMinor, row.Currency);
                }
                else if (p[i].ParameterType == typeof(long))
                {
                    args[i] = row.AmountMinor;
                }
                else if (p[i].ParameterType == typeof(string))
                {
                    args[i] = row.Title;
                }
                else
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                try
                {
                    if (ctor.Invoke(args) is OrderLineItem item)
                    {
                        return [item];
                    }
                }
                catch
                {
                    // Continue trying next constructor
                }
            }
        }

        return [];
    }

    private static object? CreateMoneyInstance(long amountMinor, string currencyCode)
    {
        var moneyType = typeof(Money);

        foreach (var method in moneyType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != moneyType) continue;
            var p = method.GetParameters();
            if (p.Length == 2 && p[0].ParameterType == typeof(long) && p[1].ParameterType == typeof(string))
            {
                return method.Invoke(null, new object?[] { amountMinor, currencyCode });
            }
            if (p.Length == 2 && p[0].ParameterType == typeof(long) && p[1].ParameterType == typeof(Currency))
            {
                var curr = CreateCurrencyInstance(currencyCode);
                if (curr is not null)
                {
                    return method.Invoke(null, new[] { amountMinor, curr });
                }
            }
            if (p.Length == 1 && p[0].ParameterType == typeof(long))
            {
                return method.Invoke(null, new object?[] { amountMinor });
            }
        }

        foreach (var ctor in moneyType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var p = ctor.GetParameters();
            if (p.Length == 2 && p[0].ParameterType == typeof(long) && p[1].ParameterType == typeof(string))
            {
                return ctor.Invoke(new object?[] { amountMinor, currencyCode });
            }
            if (p.Length == 2 && p[0].ParameterType == typeof(long) && p[1].ParameterType == typeof(Currency))
            {
                var curr = CreateCurrencyInstance(currencyCode);
                if (curr is not null)
                {
                    return ctor.Invoke(new[] { amountMinor, curr });
                }
            }
        }

        return null;
    }

    private static object? CreateCurrencyInstance(string currencyCode)
    {
        return CreateSingleStringValueObject(typeof(Currency), string.IsNullOrWhiteSpace(currencyCode) ? "PHP" : currencyCode);
    }

    private static object? CreateSingleStringValueObject(Type targetType, string rawValue)
    {
        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, rawValue, ignoreCase: true, out var parsedEnum))
            {
                return parsedEnum;
            }

            var all = Enum.GetValues(targetType);
            return all.Length > 0 ? all.GetValue(0) : null;
        }

        var staticProp = targetType.GetProperty(rawValue, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (staticProp is not null && staticProp.PropertyType == targetType)
        {
            return staticProp.GetValue(null);
        }

        foreach (var method in targetType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var p = method.GetParameters();
            if (p.Length == 1 && p[0].ParameterType == typeof(string))
            {
                try
                {
                    var result = method.Invoke(null, new object?[] { rawValue });
                    if (result is not null && targetType.IsInstanceOfType(result))
                    {
                        return result;
                    }
                }
                catch
                {
                    // Ignore and try constructor
                }
            }
        }

        foreach (var ctor in targetType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var p = ctor.GetParameters();
            if (p.Length == 1 && p[0].ParameterType == typeof(string))
            {
                try
                {
                    return ctor.Invoke(new object?[] { rawValue });
                }
                catch
                {
                    // Ignore
                }
            }
        }

        return null;
    }

    private static string NormalizeStatusString(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return "pending";
        }

        return rawStatus.Trim().ToLowerInvariant() switch
        {
            "paid" or "succeeded" or "completed" => "paid",
            "failed" or "error" => "failed",
            "expired" => "expired",
            "cancelled" or "canceled" => "cancelled",
            "refunded" => "refunded",
            _ => rawStatus.Trim().ToLowerInvariant()
        };
    }

    private static string? MetadataValueOrDefault(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (metadata.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                return val.Trim();
            }
        }

        return null;
    }

    private static string? GetString(IDictionary<string, object?> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && kvp.Value is not null)
                {
                    var s = Convert.ToString(kvp.Value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }
                }
            }
        }

        return null;
    }

    private static long? GetInt64(IDictionary<string, object?> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && kvp.Value is not null)
                {
                    if (kvp.Value is long l) return l;
                    if (kvp.Value is int i) return i;
                    if (kvp.Value is decimal d) return (long)d;
                    if (long.TryParse(Convert.ToString(kvp.Value, CultureInfo.InvariantCulture), out var parsed))
                    {
                        return parsed;
                    }
                }
            }
        }

        return null;
    }

    private static DateTimeOffset GetDateTimeOffset(IDictionary<string, object?> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && kvp.Value is not null)
                {
                    if (kvp.Value is DateTimeOffset dto) return dto;
                    if (kvp.Value is DateTime dt)
                    {
                        var utc = dt.Kind == DateTimeKind.Unspecified
                            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                            : dt.ToUniversalTime();
                        return new DateTimeOffset(utc);
                    }
                    if (DateTimeOffset.TryParse(
                            Convert.ToString(kvp.Value, CultureInfo.InvariantCulture),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out var parsed))
                    {
                        return parsed;
                    }
                }
            }
        }

        return DateTimeOffset.UtcNow;
    }
}

namespace Rge.Payments.Contracts.Abstractions;

public sealed record OrderLookupDto(
    string OrderId,
    string OrderNumber,
    string Title,
    string Category,
    string ReferenceCode,
    long AmountMinor,
    string Currency,
    string Status,
    string MetadataJson,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Cross-module contract for querying Order records by identifier or order number.
/// </summary>
public interface IOrderLookup
{
    Task<OrderLookupDto?> GetOrderByIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<OrderLookupDto?> GetOrderByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
}


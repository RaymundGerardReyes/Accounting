using System.Globalization;
using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Orders;

/// <summary>
/// Value object representing an authoritative order or invoice reference number (ORD-YYYYMM-XXXX or INV-YYYYMM-XXXX).
/// </summary>
public sealed class OrderNumber : ValueObject
{
    public string Value { get; }

    public OrderNumber(string value)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(value).ToUpperInvariant();
        if (!cleaned.StartsWith("ORD-", StringComparison.Ordinal) && !cleaned.StartsWith("INV-", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid order number '{value}'. Expected prefix 'ORD-' or 'INV-'.",
                nameof(value));
        }

        Value = cleaned;
    }

    public static OrderNumber Create(string category, DateTimeOffset nowUtc, long sequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(category);
        Guard.AgainstNonPositive(sequenceNumber, nameof(sequenceNumber));
        string prefix = category.Equals(OrderCategory.ProductOrder, StringComparison.OrdinalIgnoreCase) ? "ORD" : "INV";
        string ym = nowUtc.ToString("yyyyMM", CultureInfo.InvariantCulture);
        string seq = sequenceNumber.ToString("D4", CultureInfo.InvariantCulture);
        return new OrderNumber($"{prefix}-{ym}-{seq}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

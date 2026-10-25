using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Orders;

/// <summary>
/// Represents the commercial or internal classification of an order.
/// </summary>
public sealed class OrderCategory : ValueObject
{
    public const string ProductOrder = "product_order";
    public const string DepartmentInvoice = "department_invoice";

    public string Value { get; }

    public OrderCategory(string value)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(value).ToLowerInvariant();
        if (cleaned != ProductOrder && cleaned != DepartmentInvoice)
        {
            throw new ArgumentException(
                $"Invalid order category '{value}'. Allowed: '{ProductOrder}', '{DepartmentInvoice}'.",
                nameof(value));
        }

        Value = cleaned;
    }

    public static OrderCategory Product() => new(ProductOrder);
    public static OrderCategory Department() => new(DepartmentInvoice);

    public bool IsProduct => Value == ProductOrder;
    public bool IsDepartmentInvoice => Value == DepartmentInvoice;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}


using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Orders;

/// <summary>
/// Immutable line item associated with an Order or commercial checkout session.
/// </summary>
public sealed class OrderLineItem : ValueObject
{
    public string Name { get; }
    public int Quantity { get; }
    public Money UnitPrice { get; }
    public Money TotalPrice { get; }
    public string? Description { get; }

    public OrderLineItem(string name, int quantity, Money unitPrice, string? description = null)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name);
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        Guard.AgainstNegative(unitPrice.AmountMinor, nameof(unitPrice));

        Quantity = quantity;
        UnitPrice = unitPrice;
        TotalPrice = unitPrice * quantity;
        Description = description;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return Quantity;
        yield return UnitPrice;
        yield return Description;
    }
}


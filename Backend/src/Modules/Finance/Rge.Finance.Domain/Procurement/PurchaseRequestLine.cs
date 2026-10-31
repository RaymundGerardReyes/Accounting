using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Procurement;

/// <summary>
/// Line item within a Purchase Request specifying quantity, unit price, and total amount.
/// </summary>
public sealed class PurchaseRequestLine : Entity<string>
{
    public string PurchaseRequestId { get; private set; }
    public string Description { get; private set; }
    public long Quantity { get; private set; }
    public Money UnitAmount { get; private set; }
    public Money TotalAmount { get; private set; }
    public string? AccountCode { get; private set; }

    public PurchaseRequestLine(
        string id,
        string purchaseRequestId,
        string description,
        long quantity,
        Money unitAmount,
        string? accountCode) : base(Guard.AgainstNullOrWhiteSpace(id))
    {
        PurchaseRequestId = Guard.AgainstNullOrWhiteSpace(purchaseRequestId);
        Description = Guard.AgainstNullOrWhiteSpace(description);
        Quantity = Guard.AgainstNonPositive(quantity, nameof(quantity));
        UnitAmount = unitAmount;
        TotalAmount = new Money(checked(quantity * unitAmount.AmountMinor), unitAmount.CurrencyCode);
        AccountCode = accountCode;
    }

    public static PurchaseRequestLine Create(
        string purchaseRequestId,
        string description,
        long quantity,
        Money unitAmount,
        string? accountCode)
    {
        return new PurchaseRequestLine(
            id: Guid.NewGuid().ToString("D"),
            purchaseRequestId: purchaseRequestId,
            description: description,
            quantity: quantity,
            unitAmount: unitAmount,
            accountCode: accountCode);
    }
}

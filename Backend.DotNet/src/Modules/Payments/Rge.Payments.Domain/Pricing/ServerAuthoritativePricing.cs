using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Domain.Pricing;

/// <summary>
/// Authoritative pricing engine for product orders and internal department allocations.
/// </summary>
public sealed class ServerAuthoritativePricing : IPricingPolicy
{
    private static readonly Dictionary<string, long> DepartmentMonthlyAllocationsCentavos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["manufacturing"] = 45000_00,
        ["assembly"] = 28000_00,
        ["qa"] = 15000_00,
        ["logistics"] = 10000_00
    };

    private static readonly Dictionary<string, long> KnownProductUnitCostsPesos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Product A"] = 125,
        ["Product B"] = 89,
        ["Product C"] = 156,
        ["Product D"] = 203,
        ["Product E"] = 67
    };

    public Result<Money> CalculateAuthoritativeAmount(
        string category,
        string referenceCode,
        int quantity,
        long? clientSuppliedAmountMinor = null)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(referenceCode);

        if (clientSuppliedAmountMinor.HasValue && clientSuppliedAmountMinor.Value > 0)
        {
            return Result.Success(Money.Php(clientSuppliedAmountMinor.Value));
        }

        int qty = Math.Max(1, quantity);

        if (string.Equals(category, OrderCategory.ProductOrder, StringComparison.OrdinalIgnoreCase))
        {
            if (KnownProductUnitCostsPesos.TryGetValue(referenceCode.Trim(), out long unitPesos))
            {
                return Result.Success(Money.Php(unitPesos * 100 * qty));
            }

            return Result.Success(Money.Php(100_00 * qty));
        }

        if (string.Equals(category, OrderCategory.DepartmentInvoice, StringComparison.OrdinalIgnoreCase))
        {
            if (DepartmentMonthlyAllocationsCentavos.TryGetValue(referenceCode.Trim(), out long allocationCentavos))
            {
                return Result.Success(Money.Php(allocationCentavos));
            }

            return Result.Success(Money.Php(25000_00));
        }

        return Result.Success(Money.Php(100_00));
    }
}

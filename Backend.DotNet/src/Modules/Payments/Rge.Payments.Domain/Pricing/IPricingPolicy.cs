using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Payments.Domain.Pricing;

/// <summary>
/// Evaluates server-authoritative pricing for orders and invoices to prevent client tampering.
/// </summary>
public interface IPricingPolicy
{
    Result<Money> CalculateAuthoritativeAmount(
        string category,
        string referenceCode,
        int quantity,
        long? clientSuppliedAmountMinor = null);
}


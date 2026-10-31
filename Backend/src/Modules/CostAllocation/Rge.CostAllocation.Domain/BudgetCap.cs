using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.CostAllocation.Domain;

public sealed class BudgetCap : ValueObject
{
    public Money Limit { get; }

    public BudgetCap(Money limit)
    {
        Guard.AgainstNonPositive(limit.AmountMinor, nameof(limit));
        Limit = limit;
    }

    public bool IsExceededBy(Money actualSpend) => actualSpend > Limit;

    public Money CalculateVariance(Money actualSpend) => Limit - actualSpend;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Limit;
    }
}


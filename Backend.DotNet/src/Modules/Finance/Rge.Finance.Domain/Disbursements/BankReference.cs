using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Disbursements;

/// <summary>
/// Value object representing a bank transaction reference code.
/// </summary>
public sealed class BankReference : ValueObject
{
    public string Value { get; }

    public BankReference(string value)
    {
        Value = Guard.AgainstNullOrWhiteSpace(value).Trim().ToUpperInvariant();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

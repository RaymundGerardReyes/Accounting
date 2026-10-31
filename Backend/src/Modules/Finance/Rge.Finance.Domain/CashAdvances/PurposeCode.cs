using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.CashAdvances;

/// <summary>
/// Purpose code classification for a cash advance.
/// </summary>
public sealed class PurposeCode : ValueObject
{
    public const string Travel = "travel";
    public const string FieldOperations = "field_operations";
    public const string Emergency = "emergency";
    public const string Procurement = "procurement";
    public const string EventOperations = "event_operations";

    public string Value { get; }

    public PurposeCode(string value)
    {
        Value = Guard.AgainstNullOrWhiteSpace(value).Trim().ToLowerInvariant();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.Webhooks;

/// <summary>
/// Value object representing a PayMongo event ID (evt_...).
/// </summary>
public sealed class WebhookEventKey : ValueObject
{
    public string Value { get; }

    public WebhookEventKey(string value)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(value).Trim();
        Value = cleaned;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}


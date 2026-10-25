using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Payments.Domain.PaymentAttempts;

/// <summary>
/// Value object representing an idempotency key to prevent duplicate payment attempts.
/// </summary>
public sealed class IdempotencyKey : ValueObject
{
    public string Value { get; }

    public IdempotencyKey(string value)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(value).Trim();
        if (cleaned.Length > 128)
        {
            throw new ArgumentException("Idempotency key cannot exceed 128 characters.", nameof(value));
        }

        Value = cleaned;
    }

    public static IdempotencyKey Generate() =>
        new(Guid.NewGuid().ToString("D"));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}


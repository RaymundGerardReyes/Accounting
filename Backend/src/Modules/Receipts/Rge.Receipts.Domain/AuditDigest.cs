using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Receipts.Domain;

/// <summary>
/// Tamper-evident cryptographic digest formatted as sha256:v2:{hex64}.
/// </summary>
public sealed class AuditDigest : ValueObject
{
    public const string Prefix = "sha256:v2:";

    public string Value { get; }

    public AuditDigest(string value)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(value).ToLowerInvariant();
        if (!cleaned.StartsWith(Prefix, StringComparison.Ordinal) || cleaned.Length != Prefix.Length + 64)
        {
            throw new ArgumentException(
                $"Invalid AuditDigest '{value}'. Must start with '{Prefix}' followed by 64 hex characters.",
                nameof(value));
        }

        Value = cleaned;
    }

    public string ShortHash8 => Value.Substring(Prefix.Length, 8).ToUpperInvariant();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}


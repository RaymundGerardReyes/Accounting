using System.Globalization;
using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.CashAdvances;

/// <summary>
/// Value object representing an authoritative Cash Advance reference number (CA-YYYYMM-XXXX).
/// </summary>
public sealed class AdvanceNumber : ValueObject
{
    public string Value { get; }

    public AdvanceNumber(string value)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(value).ToUpperInvariant();
        if (!cleaned.StartsWith("CA-", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid cash advance number '{value}'. Expected prefix 'CA-'.",
                nameof(value));
        }

        Value = cleaned;
    }

    public static AdvanceNumber Create(DateTimeOffset nowUtc, long sequenceNumber)
    {
        Guard.AgainstNonPositive(sequenceNumber, nameof(sequenceNumber));
        string ym = nowUtc.ToString("yyyyMM", CultureInfo.InvariantCulture);
        string seq = sequenceNumber.ToString("D4", CultureInfo.InvariantCulture);
        return new AdvanceNumber($"CA-{ym}-{seq}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

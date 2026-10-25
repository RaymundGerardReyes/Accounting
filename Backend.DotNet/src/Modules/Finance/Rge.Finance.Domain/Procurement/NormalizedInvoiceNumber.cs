using System.Text.RegularExpressions;
using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Procurement;

/// <summary>
/// Value object representing an alphanumeric normalized invoice number used to prevent duplicate payables.
/// </summary>
public sealed class NormalizedInvoiceNumber : ValueObject
{
    private static readonly Regex NonAlphanumeric = new(@"[^A-Z0-9]", RegexOptions.Compiled);

    public string RawValue { get; }
    public string NormalizedValue { get; }

    public NormalizedInvoiceNumber(string rawValue)
    {
        RawValue = Guard.AgainstNullOrWhiteSpace(rawValue).Trim();
        string upper = RawValue.ToUpperInvariant();
        string cleaned = NonAlphanumeric.Replace(upper, string.Empty);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ArgumentException($"Invoice number '{rawValue}' must contain at least one alphanumeric character.", nameof(rawValue));
        }

        NormalizedValue = cleaned;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return NormalizedValue;
    }

    public override string ToString() => NormalizedValue;
}

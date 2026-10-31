using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Liquidation;

/// <summary>
/// Deterministic SHA-256 fingerprint value object for detecting duplicate expense receipts and invoices.
/// </summary>
public sealed class DuplicateFingerprint : ValueObject
{
    public string Value { get; }

    public DuplicateFingerprint(string value)
    {
        Value = Guard.AgainstNullOrWhiteSpace(value).Trim().ToLowerInvariant();
    }

    public static DuplicateFingerprint Create(
        string expenseDate,
        string category,
        long claimedAmountMinor,
        string receiptOrInvoiceNo)
    {
        ArgumentNullException.ThrowIfNull(expenseDate);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(receiptOrInvoiceNo);

        string normalized = string.Join('|',
            expenseDate.Trim().ToUpperInvariant(),
            category.Trim().ToUpperInvariant(),
            claimedAmountMinor.ToString(CultureInfo.InvariantCulture),
            receiptOrInvoiceNo.Trim().ToUpperInvariant());

        byte[] bytes = Encoding.UTF8.GetBytes(normalized);
        byte[] hash = SHA256.HashData(bytes);
        return new DuplicateFingerprint(Convert.ToHexStringLower(hash));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

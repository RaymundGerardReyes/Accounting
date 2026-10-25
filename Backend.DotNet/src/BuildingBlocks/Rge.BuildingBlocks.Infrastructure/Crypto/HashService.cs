using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rge.BuildingBlocks.Infrastructure.Crypto;

/// <summary>
/// Provides SHA-256 hashing for BIR Official Receipt digests (sha256:v2),
/// document integrity verification, and duplicate expense line fingerprinting.
/// </summary>
public static class HashService
{
    public static string ComputeSha256Hex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeBirReceiptDigestV2(string canonicalPayload) =>
        $"sha256:v2:{ComputeSha256Hex(canonicalPayload)}";

    /// <summary>
    /// Computes the duplicate detection fingerprint_key for an ExpenseLine
    /// from (date, category, amount_minor, receipt_or_invoice_no).
    /// </summary>
    public static string ComputeExpenseFingerprint(
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

        return ComputeSha256Hex(normalized);
    }
}

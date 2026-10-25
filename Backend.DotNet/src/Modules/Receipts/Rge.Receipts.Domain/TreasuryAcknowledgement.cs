using System.Globalization;
using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Receipts.Domain;

/// <summary>
/// Represents a Treasury Acknowledgement number (ACK-YYYYMM-XXXX-HASH8) paired with an Official Receipt.
/// </summary>
public sealed class TreasuryAcknowledgement : ValueObject
{
    public string AcknowledgementNumber { get; }
    public DateTimeOffset AcknowledgedAtUtc { get; }

    public TreasuryAcknowledgement(string acknowledgementNumber, DateTimeOffset acknowledgedAtUtc)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(acknowledgementNumber).ToUpperInvariant();
        if (!cleaned.StartsWith("ACK-", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid Treasury Acknowledgement number '{acknowledgementNumber}'. Expected prefix 'ACK-'.",
                nameof(acknowledgementNumber));
        }

        AcknowledgementNumber = cleaned;
        AcknowledgedAtUtc = acknowledgedAtUtc;
    }

    public static TreasuryAcknowledgement Create(
        DateTimeOffset issuedAtUtc,
        long sequenceNumber,
        string hash8)
    {
        Guard.AgainstNonPositive(sequenceNumber, nameof(sequenceNumber));
        string cleanHash = Guard.AgainstNullOrWhiteSpace(hash8).ToUpperInvariant();
        string hashSegment = cleanHash.Length >= 8 ? cleanHash[..8] : cleanHash.PadRight(8, '0');
        string yearMonth = issuedAtUtc.ToString("yyyyMM", CultureInfo.InvariantCulture);
        string seqSegment = sequenceNumber.ToString("D4", CultureInfo.InvariantCulture);

        return new TreasuryAcknowledgement($"ACK-{yearMonth}-{seqSegment}-{hashSegment}", issuedAtUtc);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AcknowledgementNumber;
        yield return AcknowledgedAtUtc;
    }

    public override string ToString() => AcknowledgementNumber;
}


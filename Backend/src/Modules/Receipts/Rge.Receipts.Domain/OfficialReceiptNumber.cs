using System.Globalization;
using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;

namespace Rge.Receipts.Domain;

/// <summary>
/// Represents a collision-safe BIR Official Receipt number formatted as OR-YYYYMM-XXXX-HASH8.
/// </summary>
public sealed class OfficialReceiptNumber : ValueObject
{
    public string Value { get; }

    public OfficialReceiptNumber(string value)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(value).ToUpperInvariant();
        if (!cleaned.StartsWith("OR-", StringComparison.Ordinal) || cleaned.Length < 7)
        {
            throw new ArgumentException(
                $"Invalid Official Receipt number '{value}'. Expected format: OR-YYYYMM-XXXX-HASH8 or prefixed with 'OR-'.",
                nameof(value));
        }

        Value = cleaned;
    }

    public static Result<OfficialReceiptNumber> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<OfficialReceiptNumber>(Error.Validation("OfficialReceiptNumber.Empty", "Official Receipt number cannot be empty."));
        }
        try
        {
            return Result.Success(new OfficialReceiptNumber(value));
        }
        catch (Exception ex)
        {
            return Result.Failure<OfficialReceiptNumber>(Error.Validation("OfficialReceiptNumber.Invalid", ex.Message));
        }
    }

    public static OfficialReceiptNumber Create(
        DateTimeOffset issuedAtUtc,
        long sequenceNumber,
        string hash8)
    {
        Guard.AgainstNonPositive(sequenceNumber, nameof(sequenceNumber));
        string cleanHash = Guard.AgainstNullOrWhiteSpace(hash8).ToUpperInvariant();
        string hashSegment = cleanHash.Length >= 8 ? cleanHash[..8] : cleanHash.PadRight(8, '0');
        string yearMonth = issuedAtUtc.ToString("yyyyMM", CultureInfo.InvariantCulture);
        string seqSegment = sequenceNumber.ToString("D4", CultureInfo.InvariantCulture);

        return new OfficialReceiptNumber($"OR-{yearMonth}-{seqSegment}-{hashSegment}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}


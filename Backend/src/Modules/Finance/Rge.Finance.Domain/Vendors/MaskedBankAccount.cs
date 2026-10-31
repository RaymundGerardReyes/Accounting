using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Vendors;

/// <summary>
/// Value object that protects bank account confidentiality by masking all but the final 4 digits.
/// </summary>
public sealed class MaskedBankAccount : ValueObject
{
    public string MaskedValue { get; }

    public MaskedBankAccount(string rawAccountNumber)
    {
        string cleaned = Guard.AgainstNullOrWhiteSpace(rawAccountNumber)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cleaned.Length <= 4)
        {
            MaskedValue = "****" + cleaned;
        }
        else
        {
            MaskedValue = new string('*', cleaned.Length - 4) + cleaned[^4..];
        }
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaskedValue;
    }

    public override string ToString() => MaskedValue;
}

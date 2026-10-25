namespace Rge.BuildingBlocks.SharedKernel.Money;

/// <summary>
/// Supported ISO-4217 currencies. Defaults to Philippine Peso (PHP).
/// </summary>
public sealed record Currency
{
    public static readonly Currency Php = new("PHP");
    public static readonly Currency Usd = new("USD");

    public string Code { get; }

    private Currency(string code)
    {
        Code = code;
    }

    public static Currency FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Php;
        }

        return code.Trim().ToUpperInvariant() switch
        {
            "PHP" => Php,
            "USD" => Usd,
            var unsupported => throw new ArgumentException($"Unsupported currency code: '{unsupported}'.", nameof(code))
        };
    }

    public override string ToString() => Code;
}


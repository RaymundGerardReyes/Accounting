using Rge.BuildingBlocks.SharedKernel.Money;

namespace Rge.Tests.Common.Builders;

/// <summary>
/// Fluent helper for constructing edge-case Money values for financial invariant testing.
/// </summary>
public static class MoneyBuilder
{
    public static Money Php(long centavos) => Money.Php(centavos);

    public static Money Zero(string currency = "PHP") => new(0, currency);

    public static Money OneCentavo(string currency = "PHP") => new(1, currency);

    public static Money HalfCentavoBoundary() => Money.Php(112_00); // 112.00 / 1.12 = 100.00 exact

    public static Money OddCentavo(long centavos = 99_99) => Money.Php(centavos);

    public static Money LargeSafeAmount() => Money.Php(100_000_000_000_00L); // PHP 100 Billion in centavos
}


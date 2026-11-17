using Rge.BuildingBlocks.Infrastructure.Crypto;
using Rge.BuildingBlocks.SharedKernel.Money;
using Xunit;

namespace Rge.Receipts.Domain.Tests;

public sealed class OfficialReceiptDomainTests
{
    [Theory]
    [InlineData(11_200L, 10_000L, 1_200L)]
    [InlineData(10_000L, 8_929L, 1_071L)]
    [InlineData(150_000L, 133_929L, 16_071L)]
    [InlineData(1L, 1L, 0L)]
    public void VatInclusiveSplitConservesEveryCentavo(
        long grossCentavos,
        long expectedVatableCentavos,
        long expectedVatCentavos)
    {
        Money gross = Money.Php(grossCentavos);
        (Money vatableSales, Money vatAmount) = VatCalculator.ComputeVatInclusive(gross);

        Assert.Equal(expectedVatableCentavos, vatableSales.AmountMinor);
        Assert.Equal(expectedVatCentavos, vatAmount.AmountMinor);
        Assert.Equal(gross.AmountMinor, vatableSales.AmountMinor + vatAmount.AmountMinor);
    }

    [Fact]
    public void ReceiptDigestIsDeterministicAndChangesWhenPayloadChanges()
    {
        const string canonicalPayload = "OR-2026-0001|ord_001|pay_001|PHP|150000|133929|16071";

        string digest = HashService.ComputeBirReceiptDigestV2(canonicalPayload);

        Assert.StartsWith("sha256:v2:", digest, StringComparison.Ordinal);
        Assert.Equal(digest, HashService.ComputeBirReceiptDigestV2(canonicalPayload));
        Assert.NotEqual(digest, HashService.ComputeBirReceiptDigestV2(canonicalPayload + "1"));
    }
}

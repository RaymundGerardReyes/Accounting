using System.Globalization;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;
using Rge.Receipts.Infrastructure.Pdf;
using Xunit;

namespace Rge.Contract.Parity.Tests;

/// <summary>
/// Empirical adversarial challenger tests for Milestone 1: Domain & View-Model Foundation.
/// Stress-tests:
/// 1. Money.ToPhilippinePesoString() across extreme boundaries, negatives, and culture independence.
/// 2. VatBreakdown mathematical invariant under 10,000+ centavo values and fuzzing.
/// 3. Dual PST (UTC+8) and UTC timestamp calculation across leap years, rollover boundaries, and zero-drift verification.
/// 4. ReceiptDocumentViewModel and ReceiptLayout backward compatibility and structure invariants.
/// </summary>
public sealed class ReceiptsDomainAdversarialChallengerTests
{
    private static OfficialReceipt CreateSampleReceipt(
        long centavos = 100_00,
        DateTimeOffset? issuedAtUtc = null)
    {
        var timestamp = issuedAtUtc ?? new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
        var digest = new AuditDigest("sha256:v2:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        var result = OfficialReceipt.Issue(
            orderId: "ord_challenger_001",
            orderNumber: "ORD-CHALLENGER-01",
            paymentAttemptId: "pay_challenger_01",
            sequenceNumber: 1,
            customerName: "Empirical Challenger",
            customerEmail: "challenger@example.com",
            description: "Adversarial Verification Line Item",
            paymentMethod: "qrph",
            grossAmount: new Money(centavos, "PHP"),
            digest: digest,
            issuedAtUtc: timestamp);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    // =========================================================================
    // 1. Money.ToPhilippinePesoString() Adversarial Boundary Testing
    // =========================================================================

    [Theory]
    [InlineData(0, "₱0.00")]
    [InlineData(1, "₱0.01")]
    [InlineData(99, "₱0.99")]
    [InlineData(100, "₱1.00")]
    [InlineData(105, "₱1.05")]
    [InlineData(999, "₱9.99")]
    [InlineData(1000, "₱10.00")]
    [InlineData(10000, "₱100.00")]
    [InlineData(100000, "₱1,000.00")]
    [InlineData(123456, "₱1,234.56")]
    [InlineData(-123456, "-₱1,234.56")]
    [InlineData(-1, "-₱0.01")]
    [InlineData(-99, "-₱0.99")]
    [InlineData(-100, "-₱1.00")]
    [InlineData(100_000_000_000L, "₱1,000,000,000.00")]
    [InlineData(1_000_000_000_000_00L, "₱1,000,000,000,000.00")]
    [InlineData(92_233_720_368_547_758_07L, "₱92,233,720,368,547,758.07")]
    public void Money_ToPhilippinePesoString_ExtremeValues_FormatsCorrectly(long centavos, string expected)
    {
        var money = new Money(centavos, "PHP");
        string formatted = money.ToPhilippinePesoString();
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Money_ToPhilippinePesoString_CultureInvariance_ProducesIdenticalOutputAcrossCultures()
    {
        // Many European and Middle Eastern cultures use comma for decimals and dot/space for thousands
        var testCultures = new[]
        {
            CultureInfo.InvariantCulture,
            new CultureInfo("en-US"),
            new CultureInfo("en-PH"),
            new CultureInfo("fr-FR"),
            new CultureInfo("de-DE"),
            new CultureInfo("es-ES"),
            new CultureInfo("ar-SA"),
            new CultureInfo("ja-JP")
        };

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            foreach (var culture in testCultures)
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;

                var positiveMoney = new Money(123456789L, "PHP"); // ₱1,234,567.89
                var negativeMoney = new Money(-123456789L, "PHP"); // -₱1,234,567.89
                var zeroMoney = new Money(0L, "PHP"); // ₱0.00

                Assert.Equal("₱1,234,567.89", positiveMoney.ToPhilippinePesoString());
                Assert.Equal("-₱1,234,567.89", negativeMoney.ToPhilippinePesoString());
                Assert.Equal("₱0.00", zeroMoney.ToPhilippinePesoString());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    // =========================================================================
    // 2. VatBreakdown Mathematical Invariant Fuzzing & Invariant Testing
    // =========================================================================

    [Fact]
    public void VatBreakdown_MathematicalInvariant_StrictlyHoldsAcross10000Values()
    {
        // Test deterministic boundary points
        long[] boundaryCentavos =
        [
            0, 1, 2, 3, 4, 5, 10, 50, 99, 100, 101, 199, 200,
            333, 999, 1000, 1001, 1234, 12345, 123456, 370000, 1000000, 100000000
        ];

        foreach (long centavos in boundaryCentavos)
        {
            var gross = new Money(centavos, "PHP");
            var breakdown = VatBreakdown.FromVatInclusiveGross(gross);

            Assert.Equal(centavos, breakdown.GrossAmount.AmountMinor);
            Assert.Equal(
                breakdown.GrossAmount.AmountMinor,
                breakdown.VatableSales.AmountMinor +
                breakdown.VatAmount.AmountMinor +
                breakdown.VatExemptSales.AmountMinor +
                breakdown.ZeroRatedSales.AmountMinor);

            Assert.Equal(breakdown.GrossAmount.ToPhilippinePesoString(), breakdown.FormattedGrossAmount);
            Assert.Equal(breakdown.VatableSales.ToPhilippinePesoString(), breakdown.FormattedVatableSales);
            Assert.Equal(breakdown.VatAmount.ToPhilippinePesoString(), breakdown.FormattedVatAmount);
            Assert.Equal(breakdown.VatExemptSales.ToPhilippinePesoString(), breakdown.FormattedVatExemptSales);
            Assert.Equal(breakdown.ZeroRatedSales.ToPhilippinePesoString(), breakdown.FormattedZeroRatedSales);
        }

        // Fuzz across 10,000 distinct values with odd, prime, and fractional centavos
        for (int i = 0; i < 10000; i++)
        {
            long centavos = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 100_000_000); // 0 to 1,000,000.00 PHP
            var gross = new Money(centavos, "PHP");
            var breakdown = VatBreakdown.FromVatInclusiveGross(gross);

            // Invariant 1: GrossAmount minor units must equal input minor units
            Assert.Equal(centavos, breakdown.GrossAmount.AmountMinor);

            // Invariant 2: GrossAmount must equal sum of all parts
            long sum = breakdown.VatableSales.AmountMinor +
                       breakdown.VatAmount.AmountMinor +
                       breakdown.VatExemptSales.AmountMinor +
                       breakdown.ZeroRatedSales.AmountMinor;
            Assert.Equal(centavos, sum);
        }
    }

    [Fact]
    public void VatBreakdown_VatExempt_MathematicalInvariant_Holds()
    {
        for (long centavos = 0; centavos <= 500; centavos += 25)
        {
            var gross = new Money(centavos, "PHP");
            var breakdown = VatBreakdown.CreateVatExempt(gross);

            Assert.Equal(0, breakdown.VatableSales.AmountMinor);
            Assert.Equal(0, breakdown.VatAmount.AmountMinor);
            Assert.Equal(centavos, breakdown.VatExemptSales.AmountMinor);
            Assert.Equal(0, breakdown.ZeroRatedSales.AmountMinor);
            Assert.Equal(centavos, breakdown.GrossAmount.AmountMinor);
        }
    }

    // =========================================================================
    // 3. Dual PST / UTC Timestamp Accuracy & Zero Drift
    // =========================================================================

    [Theory]
    // Standard daytime: 03:00 UTC -> 11:00 PST (same day)
    [InlineData("2026-11-08T03:00:00Z", "2026-11-08 03:00:00 UTC", "2026-11-08 11:00:00 PST (UTC+8)", "2026-11-08 11:00:00 PST / 2026-11-08 03:00:00 UTC")]
    // Midnight rollover: 20:00 UTC -> 04:00 PST next day
    [InlineData("2026-11-08T20:00:00Z", "2026-11-08 20:00:00 UTC", "2026-11-09 04:00:00 PST (UTC+8)", "2026-11-09 04:00:00 PST / 2026-11-08 20:00:00 UTC")]
    // Month rollover (non-leap year Feb 28 -> Mar 01)
    [InlineData("2026-02-28T22:30:00Z", "2026-02-28 22:30:00 UTC", "2026-03-01 06:30:00 PST (UTC+8)", "2026-03-01 06:30:00 PST / 2026-02-28 22:30:00 UTC")]
    // Leap year leap day rollover (Feb 28 2028 -> Feb 29 2028)
    [InlineData("2028-02-28T20:00:00Z", "2028-02-28 20:00:00 UTC", "2028-02-29 04:00:00 PST (UTC+8)", "2028-02-29 04:00:00 PST / 2028-02-28 20:00:00 UTC")]
    // Leap year leap day end rollover (Feb 29 2028 -> Mar 01 2028)
    [InlineData("2028-02-29T20:00:00Z", "2028-02-29 20:00:00 UTC", "2028-03-01 04:00:00 PST (UTC+8)", "2028-03-01 04:00:00 PST / 2028-02-29 20:00:00 UTC")]
    // Year rollover (Dec 31 2026 -> Jan 01 2027)
    [InlineData("2026-12-31T20:00:00Z", "2026-12-31 20:00:00 UTC", "2027-01-01 04:00:00 PST (UTC+8)", "2027-01-01 04:00:00 PST / 2026-12-31 20:00:00 UTC")]
    public void DualTimestamps_OffsetAndRollover_CalculatedAccuratelyWithoutDrift(
        string utcIso,
        string expectedUtcFormatted,
        string expectedPstFormatted,
        string expectedDualFormatted)
    {
        var issuedUtc = DateTimeOffset.Parse(utcIso, CultureInfo.InvariantCulture);
        var receipt = CreateSampleReceipt(100_00, issuedUtc);

        var vm = ReceiptDocumentViewModel.FromReceipt(receipt);

        Assert.Equal(TimeSpan.FromHours(8), vm.IssuedAtPst.Offset);
        Assert.Equal(TimeSpan.Zero, vm.IssuedAtUtc.Offset);
        Assert.Equal(issuedUtc, vm.IssuedAtUtc);
        Assert.Equal(issuedUtc.ToUniversalTime(), vm.IssuedAtPst.ToUniversalTime());

        Assert.Equal(expectedUtcFormatted, vm.FormattedIssuedAtUtc);
        Assert.Equal(expectedPstFormatted, vm.FormattedIssuedAtPst);
        Assert.Equal(expectedDualFormatted, vm.FormattedDualTimestamp);
    }

    // =========================================================================
    // 4. ReceiptDocumentViewModel and ReceiptLayout Compatibility
    // =========================================================================

    [Fact]
    public void ReceiptLayout_BuildReceiptLines_And_BuildLines_AreIdenticalAndComplete()
    {
        var receipt = CreateSampleReceipt(370_000L); // ₱3,700.00
        var lines1 = ReceiptLayout.BuildLines(receipt);
        var lines2 = ReceiptLayout.BuildReceiptLines(receipt);

        Assert.NotNull(lines1);
        Assert.NotNull(lines2);
        Assert.Equal(lines1.Count, lines2.Count);

        for (int i = 0; i < lines1.Count; i++)
        {
            Assert.Equal(lines1[i], lines2[i]);
        }

        // Verify key statutory lines
        Assert.Contains(lines1, l => l.Contains(BirRmc77_2024Policy.IssuerRegisteredName, StringComparison.Ordinal));
        Assert.Contains(lines1, l => l.Contains(BirRmc77_2024Policy.IssuerTin, StringComparison.Ordinal));
        Assert.Contains(lines1, l => l.Contains("DATE ISSUED (PST)", StringComparison.Ordinal));
        Assert.Contains(lines1, l => l.Contains("DATE ISSUED (UTC)", StringComparison.Ordinal));
        Assert.Contains(lines1, l => l.Contains("TOTAL AMOUNT PAID   : ₱3,700.00", StringComparison.Ordinal));
        Assert.Contains(lines1, l => l.Contains("Cryptographic Digest: sha256:v2:", StringComparison.Ordinal));
        Assert.Contains(lines1, l => l.Contains("Compliant with BIR RMC No. 77-2024 & RA 11976", StringComparison.Ordinal));
    }

    [Fact]
    public void ReceiptLayout_BuildLines_NullReceipt_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ReceiptLayout.BuildLines(null!));
        Assert.Throws<ArgumentNullException>(() => ReceiptLayout.BuildReceiptLines(null!));
        Assert.Throws<ArgumentNullException>(() => ReceiptLayout.ToViewModel(null!));
    }
}

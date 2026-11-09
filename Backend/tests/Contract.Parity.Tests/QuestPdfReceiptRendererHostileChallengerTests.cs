using System.Diagnostics;
using System.Globalization;
using System.Text;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Pdf;
using Xunit;

namespace Rge.Contract.Parity.Tests;

/// <summary>
/// Empirical adversarial stress test harness for QuestPdfReceiptRenderer:
/// 1. Null receipt rejection.
/// 2. Concurrency and cache eviction stress (32 threads, 2048 receipts exceeding MaxCachedPdfs).
/// 3. Boundary, zero, and extreme money amounts (up to 1 trillion pesos).
/// 4. Hostile strings (extreme lengths, multi-page overflow, Unicode, Baybayin, Arabic RTL, Emojis).
/// 5. PDF header (%PDF-1.4) and trailer (%%EOF) integrity.
/// 6. Rendering latency SLA (< 50ms).
/// </summary>
public sealed class QuestPdfReceiptRendererHostileChallengerTests
{
    private static OfficialReceipt CreateReceipt(
        long centavos = 370_000,
        string customerName = "Maria Clara Ñañez",
        string customerEmail = "maria.clara@example.ph",
        string description = "Adversarial Stress Line Item",
        string orderNumber = "ORD-CHALLENGER-001",
        string orderId = "ord_challenger_001",
        string paymentAttemptId = "att_challenger_001",
        string paymentMethod = "qrph",
        DateTimeOffset? issuedAtUtc = null)
    {
        var timestamp = issuedAtUtc ?? new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);
        var digest = new AuditDigest("sha256:v2:" + new string('e', 64));

        var result = OfficialReceipt.Issue(
            orderId: orderId,
            orderNumber: orderNumber,
            paymentAttemptId: paymentAttemptId,
            sequenceNumber: 999,
            customerName: customerName,
            customerEmail: customerEmail,
            description: description,
            paymentMethod: paymentMethod,
            grossAmount: new Money(centavos, "PHP"),
            digest: digest,
            issuedAtUtc: timestamp);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private static OfficialReceipt CreateZeroAmountReceipt(
        string customerName = "Zero Amount Customer",
        string description = "Zero Centavo Line Item")
    {
        var timestamp = new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);
        var digest = new AuditDigest("sha256:v2:" + new string('0', 64));
        var orNumber = OfficialReceiptNumber.Create(timestamp, 1, digest.ShortHash8);
        var ack = TreasuryAcknowledgement.Create(timestamp, 1, digest.ShortHash8);
        var zeroMoney = new Money(0, "PHP");
        var vatBreakdown = new VatBreakdown(zeroMoney, zeroMoney, zeroMoney, zeroMoney);

        return new OfficialReceipt(
            id: Guid.NewGuid().ToString("D"),
            orderId: "ord_zero_001",
            orderNumber: "ORD-ZERO-001",
            paymentAttemptId: "att_zero_001",
            receiptNumber: orNumber,
            acknowledgement: ack,
            customerName: customerName,
            customerEmail: "zero@example.ph",
            description: description,
            paymentMethod: "promo_waiver",
            vatBreakdown: vatBreakdown,
            digest: digest,
            issuedAtUtc: timestamp);
    }

    // =========================================================================
    // 1. Null Receipt Handling
    // =========================================================================

    [Fact]
    public void RenderOfficialReceiptPdf_NullReceipt_ThrowsArgumentNullException()
    {
        var renderer = new QuestPdfReceiptRenderer();
        Assert.Throws<ArgumentNullException>(() => renderer.RenderOfficialReceiptPdf(null!));
    }

    // =========================================================================
    // 2. Zero & Extreme Money Values
    // =========================================================================

    [Fact]
    public void RenderOfficialReceiptPdf_ZeroAmountReceipt_RendersValidPdf()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var zeroReceipt = CreateZeroAmountReceipt();

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(zeroReceipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 250);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfBytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 15, 15), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1L)]                      // ₱0.01 (1 centavo)
    [InlineData(100L)]                    // ₱1.00
    [InlineData(10_000_00L)]              // ₱10,000.00
    [InlineData(100_000_000_00L)]          // ₱100,000,000.00 (100M PHP)
    [InlineData(1_000_000_000_00L)]        // ₱1,000,000,000.00 (1B PHP)
    [InlineData(100_000_000_000_00L)]      // ₱100,000,000,000.00 (100B PHP)
    [InlineData(1_000_000_000_000_00L)]    // ₱1,000,000,000,000.00 (1 Trillion PHP)
    public void RenderOfficialReceiptPdf_ExtremeMoneyAmounts_RendersValidPdf(long centavos)
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt(centavos: centavos);

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 250);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfBytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 15, 15), StringComparison.Ordinal);
    }

    // =========================================================================
    // 3. Hostile Strings, Lengths, and Character Encodings
    // =========================================================================

    [Fact]
    public void RenderOfficialReceiptPdf_LongCustomerNameAndDetails_RendersValidPdf()
    {
        var renderer = new QuestPdfReceiptRenderer();

        // 500-character customer name, long order IDs, long emails
        string longName = string.Join(" ", Enumerable.Repeat("De La Cruz-Santillan", 25));
        string longEmail = "very.long.enterprise.accounting.customer.contact.department@" + new string('a', 50) + ".example.com";
        string longOrderNumber = "ORD-" + new string('9', 120);
        string longOrderId = "ord_" + new string('f', 120);
        string longAttemptId = "att_" + new string('e', 120);

        var receipt = CreateReceipt(
            customerName: longName,
            customerEmail: longEmail,
            orderNumber: longOrderNumber,
            orderId: longOrderId,
            paymentAttemptId: longAttemptId);

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 250);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfBytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 15, 15), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderOfficialReceiptPdf_MultiPageLongDescription_RendersWithoutInfiniteLoopOrLayoutCrash()
    {
        var renderer = new QuestPdfReceiptRenderer();

        // Multi-paragraph 4,000-character description forcing multi-page rendering
        var sb = new StringBuilder();
        for (int i = 1; i <= 30; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Paragraph {i}: Comprehensive statutory reconciliation services under BIR RMC No. 77-2024, RA 11976 compliance audit, VAT returns inspection, and electronic bookkeeping ledger certification.");
        }
        string longDescription = sb.ToString();

        var receipt = CreateReceipt(description: longDescription);

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 1000);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfBytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 15, 15), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderOfficialReceiptPdf_UnicodeAndSpecialScripts_RendersGracefully()
    {
        var renderer = new QuestPdfReceiptRenderer();

        // Spanish accents, Tagalog Baybayin, Chinese, Japanese, Arabic RTL, Emojis
        string diverseCustomer = "María José Peña-Ñañez (ᜋᜎᜒᜌ / 玛利亚 / マリア / مريم) 🇵🇭✨";
        string diverseDescription = "Konsultasyon sa Pagbubuwis & Cross-Border Advisory 🧾🚀";

        var receipt = CreateReceipt(
            customerName: diverseCustomer,
            description: diverseDescription);

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 250);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfBytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 15, 15), StringComparison.Ordinal);
    }

    // =========================================================================
    // 4. Concurrency & Cache Eviction Stress
    // =========================================================================

    [Fact]
    public void RenderOfficialReceiptPdf_HighConcurrency_And_CacheEviction_MaintainsIntegrity()
    {
        var renderer = new QuestPdfReceiptRenderer();
        const int totalReceipts = 2048; // Exceeds MaxCachedPdfs (1024) to trigger cache clearing under load

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, totalReceipts, new ParallelOptions { MaxDegreeOfParallelism = 32 }, i =>
        {
            try
            {
                var receipt = CreateReceipt(
                    centavos: 10_000 + (i * 100),
                    orderId: $"ord_stress_{i:D6}",
                    orderNumber: $"ORD-STRESS-{i:D6}",
                    paymentAttemptId: $"att_stress_{i:D6}");

                byte[] bytes1 = renderer.RenderOfficialReceiptPdf(receipt);
                byte[] bytes2 = renderer.RenderOfficialReceiptPdf(receipt);

                if (bytes1 is null || bytes2 is null)
                {
                    throw new InvalidOperationException($"Null PDF returned for receipt {i}");
                }

                if (bytes1.Length < 250)
                {
                    throw new InvalidOperationException($"PDF too short ({bytes1.Length} bytes) for receipt {i}");
                }

                if (Encoding.ASCII.GetString(bytes1, 0, 8) != "%PDF-1.4")
                {
                    throw new InvalidOperationException($"Invalid header for receipt {i}");
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    public void RenderOfficialReceiptPdf_CacheHit_ReturnsExactSameReference()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt();

        byte[] firstCall = renderer.RenderOfficialReceiptPdf(receipt);
        byte[] secondCall = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.Same(firstCall, secondCall);
    }

    // =========================================================================
    // 5. Performance Budget Verification
    // =========================================================================

    [Fact]
    public void RenderOfficialReceiptPdf_AverageRenderingLatency_IsSub50Milliseconds()
    {
        var renderer = new QuestPdfReceiptRenderer();

        // Warm up
        var warmupReceipt = CreateReceipt(centavos: 50_000);
        _ = renderer.RenderOfficialReceiptPdf(warmupReceipt);

        // Pre-create receipts so measurement isolates PDF rendering from domain instantiation
        const int iterations = 20;
        var receipts = new OfficialReceipt[iterations];
        for (int i = 0; i < iterations; i++)
        {
            receipts[i] = CreateReceipt(
                centavos: 100_000 + (i * 5_000),
                orderId: $"ord_perf_{Guid.NewGuid():N}");
        }

        // Measure pure uncached PDF rendering time
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            byte[] pdf = renderer.RenderOfficialReceiptPdf(receipts[i]);
            Assert.NotNull(pdf);
        }

        sw.Stop();
        double avgMilliseconds = sw.Elapsed.TotalMilliseconds / iterations;

        // Must complete each render in under 50ms (average) on developer workstation
        Assert.True(
            avgMilliseconds < 50.0,
            FormattableString.Invariant($"Average rendering time was {avgMilliseconds:F2}ms, which exceeds the 50ms budget."));
    }
}

using System.Globalization;
using System.Text;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Pdf;
using Xunit;

namespace Rge.Contract.Parity.Tests;

/// <summary>
/// Adversarial challenger test harness verifying callers and contract parity for Milestone 1:
/// 1. ReceiptLayout.BuildReceiptLines(receipt) and ReceiptLayout.BuildLines(receipt) return identical output.
/// 2. QuestPdfReceiptRenderer continues to render valid %PDF-1.4 output without regression.
/// 3. Concurrency, caching, and structural integrity of rendered output.
/// </summary>
public sealed class ReceiptCallerParityChallengerTests
{
    private static OfficialReceipt CreateReceipt(
        long centavos = 370_000,
        string customerName = "Maria Clara Ñañez",
        string customerEmail = "maria.clara@example.ph",
        string paymentMethod = "qrph",
        DateTimeOffset? issuedAtUtc = null)
    {
        var timestamp = issuedAtUtc ?? new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);
        var digest = new AuditDigest("sha256:v2:" + new string('f', 64));

        var result = OfficialReceipt.Issue(
            orderId: $"ord_{Guid.NewGuid():N}",
            orderNumber: "ORD-CALLER-001",
            paymentAttemptId: $"att_{Guid.NewGuid():N}",
            sequenceNumber: 77,
            customerName: customerName,
            customerEmail: customerEmail,
            description: "Caller Parity Verification Line Item",
            paymentMethod: paymentMethod,
            grossAmount: new Money(centavos, "PHP"),
            digest: digest,
            issuedAtUtc: timestamp);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public void BuildReceiptLines_And_BuildLines_Return_Identical_Output_For_All_Receipts()
    {
        OfficialReceipt[] receipts =
        [
            CreateReceipt(370_000, "Juan dela Cruz", "juan@example.ph", "qrph"),
            CreateReceipt(1, "Minimal Customer", "mini@example.ph", "card"),
            CreateReceipt(5_000_000_000L, "Corporate Client Inc.", "corp@example.ph", "paymaya"),
            CreateReceipt(125_500, "María José Peña-Ñañez", "maria@example.ph", "gcash")
        ];

        foreach (var receipt in receipts)
        {
            IReadOnlyList<string> linesFromBuildLines = ReceiptLayout.BuildLines(receipt);
            IReadOnlyList<string> linesFromBuildReceiptLines = ReceiptLayout.BuildReceiptLines(receipt);

            Assert.Equal(linesFromBuildLines.Count, linesFromBuildReceiptLines.Count);
            Assert.Equal(20, linesFromBuildLines.Count);

            for (int i = 0; i < linesFromBuildLines.Count; i++)
            {
                Assert.Equal(linesFromBuildLines[i], linesFromBuildReceiptLines[i]);
            }
        }
    }

    [Fact]
    public void QuestPdfReceiptRenderer_Renders_Valid_Pdf_Without_Regression()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt(centavos: 370_000);

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 250);

        // Header check: must strictly match %PDF-1.4
        string header = Encoding.ASCII.GetString(pdfBytes, 0, 8);
        Assert.Equal("%PDF-1.4", header);

        // Trailer check: must contain %%EOF
        string tail = Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 15, 15);
        Assert.Contains("%%EOF", tail, StringComparison.Ordinal);

        // Caching verification: second call returns identical reference
        byte[] cachedBytes = renderer.RenderOfficialReceiptPdf(receipt);
        Assert.Same(pdfBytes, cachedBytes);
    }

    [Fact]
    public void QuestPdfReceiptRenderer_ConcurrentRendering_ProducesDeterministicOutputs()
    {
        var renderer = new QuestPdfReceiptRenderer();

        Parallel.For(0, 16, i =>
        {
            var receipt = CreateReceipt(centavos: 10_000 + (i * 1_000));
            byte[] bytes1 = renderer.RenderOfficialReceiptPdf(receipt);
            byte[] bytes2 = renderer.RenderOfficialReceiptPdf(receipt);

            Assert.NotNull(bytes1);
            Assert.Same(bytes1, bytes2);
            Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(bytes1, 0, 8));
        });
    }

    [Fact]
    public void ReceiptDocumentViewModel_LineItems_AreGenerated_FromReceipt()
    {
        var receipt = CreateReceipt(370_000);
        var vm = ReceiptDocumentViewModel.FromReceipt(receipt);

        Assert.Single(vm.LineItems);
        var item = vm.LineItems[0];

        Assert.Equal("Caller Parity Verification Line Item", item.Description);
        Assert.Equal(1, item.Quantity);
        Assert.Equal("₱3,700.00", item.UnitPrice);
        Assert.Equal("₱3,700.00", item.Amount);
        Assert.Equal("12% VAT Inclusive", item.TaxClassification);
        Assert.Equal("QRPH", vm.PaymentMethod);
        Assert.StartsWith("sha256:v2:", vm.AuditDigest, StringComparison.Ordinal);
    }
}

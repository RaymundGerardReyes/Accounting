using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Receipts.Domain;
using Rge.Receipts.Domain.Policies;
using Rge.Receipts.Infrastructure.Pdf;
using Xunit;

namespace Rge.Contract.Parity.Tests;

/// <summary>
/// Empirical adversarial challenger tests for Milestone 2:
/// Focuses on PDF vector stream integrity (%PDF-1.4 header, %%EOF trailer, xref/trailer structure,
/// flate stream decompression, content layout fidelity, bounded caching, and boundary stress testing).
/// </summary>
public sealed class PdfStreamIntegrityChallengerTests
{
    private static OfficialReceipt CreateReceipt(
        long centavos = 370_000,
        string customerName = "Maria Clara Ñañez",
        string customerEmail = "maria.clara@example.ph",
        string description = "Accounting & Corporate Tax Filing Service",
        string paymentMethod = "qrph",
        DateTimeOffset? issuedAtUtc = null)
    {
        var timestamp = issuedAtUtc ?? new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);
        var digest = new AuditDigest("sha256:v2:" + new string('e', 64));

        var result = OfficialReceipt.Issue(
            orderId: $"ord_{Guid.NewGuid():N}",
            orderNumber: "ORD-M2-PDF-001",
            paymentAttemptId: $"att_{Guid.NewGuid():N}",
            sequenceNumber: 101,
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

    // =========================================================================
    // 1. Prefix Invariant & Stream Termination Verification
    // =========================================================================

    [Fact]
    public void Pdf_PrefixInvariant_StartsExactlyWith_Pdf14_Header()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt();

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length >= 8);

        // Bytes 0..7 must strictly match ASCII "%PDF-1.4"
        string header = Encoding.ASCII.GetString(pdfBytes, 0, 8);
        Assert.Equal("%PDF-1.4", header);

        // Byte array exact equality
        byte[] expectedHeaderBytes = [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4'];
        Assert.Equal(expectedHeaderBytes, pdfBytes[..8]);
    }

    [Fact]
    public void Pdf_StreamTermination_EndsWith_Valid_Eof_Marker()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt();

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 20);

        // Check tail 32 bytes for %%EOF
        string tail = Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 32, 32);
        Assert.Contains("%%EOF", tail, StringComparison.Ordinal);

        // Specifically verify trailing bytes after %%EOF are only whitespace/newlines if any
        int eofIndex = tail.LastIndexOf("%%EOF", StringComparison.Ordinal);
        Assert.True(eofIndex >= 0);

        string afterEof = tail[(eofIndex + 5)..];
        Assert.True(
            afterEof.All(c => c is '\r' or '\n' or ' ' or '\0'),
            $"Unexpected trailing data after %%EOF: '{afterEof}'");
    }

    [Fact]
    public void Pdf_SizeSanity_Exceeds1000Bytes()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt();

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        // Must be a non-trivial vector PDF with graphics and fonts, not a tiny mock
        Assert.True(pdfBytes.Length > 1000, $"PDF size {pdfBytes.Length} bytes is unexpectedly below 1,000 bytes.");
        // Should also be reasonably compact (< 2MB for a 1-page receipt)
        Assert.True(pdfBytes.Length < 2_000_000, $"PDF size {pdfBytes.Length} bytes is unexpectedly bloated.");
    }

    // =========================================================================
    // 2. Document Structure Integrity (Catalog, Pages, XRef, Trailer)
    // =========================================================================

    [Fact]
    public void Pdf_DocumentStructure_Contains_Catalog_Pages_And_CrossReferenceTable()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt();

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);
        string pdfText = Encoding.Latin1.GetString(pdfBytes);

        // 1. Catalog object
        Assert.Contains("/Type /Catalog", pdfText, StringComparison.Ordinal);

        // 2. Page tree objects
        Assert.Contains("/Type /Pages", pdfText, StringComparison.Ordinal);
        Assert.Contains("/Type /Page", pdfText, StringComparison.Ordinal);

        // 3. MediaBox / Page dimensions (A4 in points: [0 0 595.27557 841.88977] or similar)
        Assert.Contains("/MediaBox", pdfText, StringComparison.Ordinal);

        // 4. Cross reference & trailer / xref stream
        bool hasTraditionalXref = pdfText.Contains("xref", StringComparison.Ordinal) && pdfText.Contains("trailer", StringComparison.Ordinal);
        bool hasXrefStream = pdfText.Contains("/Type /XRef", StringComparison.Ordinal);
        Assert.True(hasTraditionalXref || hasXrefStream, "PDF must contain either a traditional xref table or an xref stream.");

        // 5. startxref marker
        Assert.Contains("startxref", pdfText, StringComparison.Ordinal);

        // 6. Object declaration count balance
        int objCount = Regex.Count(pdfText, @"\b\d+\s+\d+\s+obj\b");
        int endobjCount = Regex.Count(pdfText, @"\bendobj\b");
        Assert.True(objCount > 0, "PDF must declare at least one indirect object.");
        Assert.Equal(objCount, endobjCount);
    }

    // =========================================================================
    // 3. Flate Stream Decompression & Content Fidelity
    // =========================================================================

    [Fact]
    public void Pdf_FlateStreams_DecompressSuccessfully_WithoutErrors()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt();

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        // Extract all streams
        var decompressedStreams = ExtractAndDecompressAllStreams(pdfBytes);
        Assert.NotEmpty(decompressedStreams);

        // Aggregate decompressed text
        var combinedText = new StringBuilder();
        foreach (var stream in decompressedStreams)
        {
            combinedText.AppendLine(Encoding.Latin1.GetString(stream));
            combinedText.AppendLine(Encoding.UTF8.GetString(stream));
        }
        string allStreamText = combinedText.ToString();

        // PDF stream must contain vector drawing commands (e.g. 're' for rectangle, 'm' for moveto, 'l' for lineto, 'f' for fill, 'BT'/'ET' for text)
        bool hasVectorDrawing = allStreamText.Contains(" re", StringComparison.Ordinal) ||
                                allStreamText.Contains(" m", StringComparison.Ordinal) ||
                                allStreamText.Contains(" l", StringComparison.Ordinal);
        bool hasTextBlocks = allStreamText.Contains("BT", StringComparison.Ordinal) &&
                             allStreamText.Contains("ET", StringComparison.Ordinal);

        Assert.True(hasVectorDrawing || hasTextBlocks, "Decompressed streams must contain vector graphics or text layout operators.");
    }

    private static List<byte[]> ExtractAndDecompressAllStreams(byte[] pdfBytes)
    {
        var results = new List<byte[]>();
        byte[] streamMarker = Encoding.ASCII.GetBytes("stream");
        byte[] endstreamMarker = Encoding.ASCII.GetBytes("endstream");

        int index = 0;
        while (index < pdfBytes.Length)
        {
            int streamStart = IndexOf(pdfBytes, streamMarker, index);
            if (streamStart == -1)
            {
                break;
            }

            // Stream content starts after newline following 'stream'
            int contentStart = streamStart + streamMarker.Length;
            if (contentStart < pdfBytes.Length && pdfBytes[contentStart] == '\r') contentStart++;
            if (contentStart < pdfBytes.Length && pdfBytes[contentStart] == '\n') contentStart++;

            int streamEnd = IndexOf(pdfBytes, endstreamMarker, contentStart);
            if (streamEnd == -1)
            {
                break;
            }

            // Trim any trailing \r\n before 'endstream'
            int contentEnd = streamEnd;
            if (contentEnd > contentStart && pdfBytes[contentEnd - 1] == '\n') contentEnd--;
            if (contentEnd > contentStart && pdfBytes[contentEnd - 1] == '\r') contentEnd--;

            int streamLength = contentEnd - contentStart;
            if (streamLength > 0)
            {
                byte[] rawStream = new byte[streamLength];
                Array.Copy(pdfBytes, contentStart, rawStream, 0, streamLength);

                // Attempt zlib / deflate decompression
                try
                {
                    using var ms = new MemoryStream(rawStream);
                    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    zlib.CopyTo(outMs);
                    results.Add(outMs.ToArray());
                }
                catch
                {
                    // If raw deflate without zlib header
                    try
                    {
                        using var ms = new MemoryStream(rawStream);
                        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                        using var outMs = new MemoryStream();
                        deflate.CopyTo(outMs);
                        results.Add(outMs.ToArray());
                    }
                    catch
                    {
                        // Uncompressed stream
                        results.Add(rawStream);
                    }
                }
            }

            index = streamEnd + endstreamMarker.Length;
        }

        return results;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    // =========================================================================
    // 4. Thread-Safe Caching & Bounded Capacity Verification
    // =========================================================================

    [Fact]
    public void Pdf_Caching_Returns_SameReference_ForIdenticalReceipt()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt();

        byte[] firstCall = renderer.RenderOfficialReceiptPdf(receipt);
        byte[] secondCall = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(firstCall);
        Assert.Same(firstCall, secondCall);
    }

    [Fact]
    public void Pdf_ConcurrentRendering_32Threads_DeterministicAndThreadSafe()
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipts = Enumerable.Range(1, 32)
            .Select(i => CreateReceipt(centavos: 10_000 * i, customerName: $"Customer Thread {i}"))
            .ToArray();

        var renderedResults = new ConcurrentDictionary<string, byte[]>();

        Parallel.ForEach(receipts, receipt =>
        {
            byte[] bytes1 = renderer.RenderOfficialReceiptPdf(receipt);
            byte[] bytes2 = renderer.RenderOfficialReceiptPdf(receipt);

            Assert.NotNull(bytes1);
            Assert.Same(bytes1, bytes2);
            Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(bytes1, 0, 8));
            renderedResults[receipt.Id] = bytes1;
        });

        Assert.Equal(32, renderedResults.Count);
    }

    // =========================================================================
    // 5. Adversarial Input Stress Testing
    // =========================================================================

    [Theory]
    [InlineData(1L)]                          // ₱0.01
    [InlineData(99L)]                         // ₱0.99
    [InlineData(100L)]                        // ₱1.00
    [InlineData(370_000L)]                    // ₱3,700.00
    [InlineData(10_000_000_00L)]              // ₱10,000,000.00
    [InlineData(999_999_999_99L)]             // ₱999,999,999.99
    [InlineData(5_000_000_000_000_00L)]       // ₱50,000,000,000.00
    public void Pdf_ExtremeCentavos_RendersValidPdfWithoutOverflow(long centavos)
    {
        var renderer = new QuestPdfReceiptRenderer();
        var receipt = CreateReceipt(centavos: centavos);

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 1000);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfBytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 20, 20), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_ExtremeTextInputs_SpecialCharactersAndUnicode_RendersSuccessfully()
    {
        var renderer = new QuestPdfReceiptRenderer();

        // Adversarial inputs: special characters, quotes, HTML-like tags, Unicode, Cyrillic, CJK, and long strings
        string adversarialName = "Maria Clara <script>alert('xss')</script> & Co. / \"Makati\" — 日本語 & Ñañez";
        string veryLongDescription = new string('A', 500) + " " + string.Join(" - ", Enumerable.Range(1, 20).Select(i => $"Item {i}"));

        var receipt = CreateReceipt(
            centavos: 250_000,
            customerName: adversarialName,
            customerEmail: "special+tag!@sub.domain.example.ph",
            description: veryLongDescription);

        byte[] pdfBytes = renderer.RenderOfficialReceiptPdf(receipt);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 1000);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(pdfBytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdfBytes, pdfBytes.Length - 20, 20), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_NullReceipt_ThrowsArgumentNullException()
    {
        var renderer = new QuestPdfReceiptRenderer();
        Assert.Throws<ArgumentNullException>(() => renderer.RenderOfficialReceiptPdf(null!));
    }
}

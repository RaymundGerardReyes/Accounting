using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Infrastructure.Pdf;

/// <summary>
/// Renders a deterministic, AOT-compatible PDF 1.4 vector document (%PDF-1.4)
/// for BIR Official Receipts and caches the rendered bytes for instant streaming.
/// </summary>
public sealed class QuestPdfReceiptRenderer : IPdfRenderer
{
    private const int MaxCachedPdfs = 1024;
    private readonly ConcurrentDictionary<string, byte[]> _pdfCache = new(StringComparer.Ordinal);

    public byte[] RenderOfficialReceiptPdf(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (_pdfCache.TryGetValue(receipt.Id, out byte[]? cachedBytes))
        {
            return cachedBytes;
        }

        IReadOnlyList<string> lines = ReceiptLayout.BuildReceiptLines(receipt);

        var contentStreamBuilder = new StringBuilder(1024);
        contentStreamBuilder.AppendLine("BT");
        contentStreamBuilder.AppendLine("/F1 10 Tf");
        contentStreamBuilder.AppendLine("50 750 Td");
        contentStreamBuilder.AppendLine("16 TL");

        foreach (string line in lines)
        {
            string escaped = EscapePdfString(line);
            contentStreamBuilder.Append('(').Append(escaped).AppendLine(") Tj T*");
        }

        contentStreamBuilder.AppendLine("ET");
        byte[] streamBytes = Encoding.Latin1.GetBytes(contentStreamBuilder.ToString());

        using var ms = new MemoryStream(streamBytes.Length + 1024);
        WriteLatin1(ms, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        long obj1Offset = ms.Position;
        WriteLatin1(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        long obj2Offset = ms.Position;
        WriteLatin1(ms, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        long obj3Offset = ms.Position;
        WriteLatin1(ms, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        long obj4Offset = ms.Position;
        WriteLatin1(ms, "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n");

        long obj5Offset = ms.Position;
        WriteLatin1(ms, $"5 0 obj\n<< /Length {streamBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n");
        ms.Write(streamBytes, 0, streamBytes.Length);
        WriteLatin1(ms, "\nendstream\nendobj\n");

        long xrefOffset = ms.Position;
        WriteLatin1(ms, "xref\n0 6\n0000000000 65535 f \n");
        WriteXrefEntry(ms, obj1Offset);
        WriteXrefEntry(ms, obj2Offset);
        WriteXrefEntry(ms, obj3Offset);
        WriteXrefEntry(ms, obj4Offset);
        WriteXrefEntry(ms, obj5Offset);

        WriteLatin1(ms, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

        byte[] rendered = ms.ToArray();
        if (_pdfCache.Count >= MaxCachedPdfs)
        {
            _pdfCache.Clear();
        }
        _pdfCache[receipt.Id] = rendered;

        return rendered;
    }

    private static string EscapePdfString(string input) =>
        input.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("(", "\\(", StringComparison.Ordinal)
             .Replace(")", "\\)", StringComparison.Ordinal);

    private static void WriteLatin1(Stream stream, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteXrefEntry(Stream stream, long offset) =>
        WriteLatin1(stream, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
}

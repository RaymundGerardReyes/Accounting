using System.Globalization;
using System.Text;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Infrastructure.Pdf;

/// <summary>
/// Renders a deterministic, AOT-compatible PDF 1.4 vector document (%PDF-1.4)
/// for BIR Official Receipts and Treasury Acknowledgements.
/// </summary>
public sealed class QuestPdfReceiptRenderer : IPdfRenderer
{
    public byte[] RenderOfficialReceiptPdf(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        IReadOnlyList<string> lines = ReceiptLayout.BuildReceiptLines(receipt);

        var contentStreamBuilder = new StringBuilder();
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
        byte[] streamBytes = Encoding.ASCII.GetBytes(contentStreamBuilder.ToString());

        using var ms = new MemoryStream();
        WriteAscii(ms, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        long obj1Offset = ms.Position;
        WriteAscii(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        long obj2Offset = ms.Position;
        WriteAscii(ms, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        long obj3Offset = ms.Position;
        WriteAscii(ms, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        long obj4Offset = ms.Position;
        WriteAscii(ms, "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>\nendobj\n");

        long obj5Offset = ms.Position;
        WriteAscii(ms, $"5 0 obj\n<< /Length {streamBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n");
        ms.Write(streamBytes, 0, streamBytes.Length);
        WriteAscii(ms, "\nendstream\nendobj\n");

        long xrefOffset = ms.Position;
        WriteAscii(ms, "xref\n0 6\n0000000000 65535 f \n");
        WriteXrefEntry(ms, obj1Offset);
        WriteXrefEntry(ms, obj2Offset);
        WriteXrefEntry(ms, obj3Offset);
        WriteXrefEntry(ms, obj4Offset);
        WriteXrefEntry(ms, obj5Offset);

        WriteAscii(ms, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

        return ms.ToArray();
    }

    private static string EscapePdfString(string input) =>
        input.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("(", "\\(", StringComparison.Ordinal)
             .Replace(")", "\\)", StringComparison.Ordinal);

    private static void WriteAscii(Stream stream, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteXrefEntry(Stream stream, long offset) =>
        WriteAscii(stream, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
}

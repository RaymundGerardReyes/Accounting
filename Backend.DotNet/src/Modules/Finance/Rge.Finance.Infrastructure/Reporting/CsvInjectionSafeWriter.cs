using System.Buffers;
using System.Text;

namespace Rge.Finance.Infrastructure.Reporting;

/// <summary>
/// Writes RFC 4180 CSV content while neutralizing spreadsheet formula injection prefixes
/// (=, +, -, @, Tab, CR) so exported CSVs are safe to open in Excel or Google Sheets.
/// </summary>
public static class CsvInjectionSafeWriter
{
    private static readonly SearchValues<char> FormulaPrefixes = SearchValues.Create(['=', '+', '-', '@', '\t', '\r']);

    public static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string sanitized = value.Trim();

        if (sanitized.AsSpan().IndexOfAny(FormulaPrefixes) == 0)
        {
            sanitized = $"'{sanitized}";
        }

        bool mustQuote = sanitized.Contains(',', StringComparison.Ordinal) ||
                         sanitized.Contains('"', StringComparison.Ordinal) ||
                         sanitized.Contains('\n', StringComparison.Ordinal) ||
                         sanitized.Contains('\r', StringComparison.Ordinal);

        if (mustQuote)
        {
            return $"\"{sanitized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return sanitized;
    }

    public static string WriteRow(params string?[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var sb = new StringBuilder();
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(EscapeCell(cells[i]));
        }
        return sb.ToString();
    }
}


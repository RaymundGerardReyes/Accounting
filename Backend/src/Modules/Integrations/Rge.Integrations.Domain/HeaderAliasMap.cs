namespace Rge.Integrations.Domain;

/// <summary>
/// Normalizes and maps Google Sheets header titles to standard domain field names.
/// </summary>
public static class HeaderAliasMap
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Timestamp"] = "submitted_at",
        ["Email Address"] = "customer_email",
        ["Email"] = "customer_email",
        ["Full Name"] = "customer_name",
        ["Name"] = "customer_name",
        ["Contact Number"] = "customer_phone",
        ["Phone Number"] = "customer_phone",
        ["Mobile Number"] = "customer_phone",
        ["Item / Product"] = "item_reference",
        ["Product"] = "item_reference",
        ["Quantity"] = "quantity",
        ["Qty"] = "quantity",
        ["Amount"] = "amount_minor",
        ["Price"] = "amount_minor",
        ["Payment Method"] = "payment_method",
        ["Delivery Address"] = "notes",
        ["Notes / Remarks"] = "notes",
        ["Student ID"] = "student_id",
        ["Instructor / Faculty"] = "instructor_credit",
        ["Book Title"] = "book_title"
    };

    public static string NormalizeHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        string trimmed = header.Trim();
        return Aliases.TryGetValue(trimmed, out string? standard) ? standard : trimmed.ToLowerInvariant().Replace(" ", "_", StringComparison.Ordinal);
    }
}


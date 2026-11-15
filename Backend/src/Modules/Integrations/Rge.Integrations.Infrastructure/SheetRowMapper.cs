using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Infrastructure;

public static class SheetRowMapper
{
    public static object?[] MapToRow(GoogleFormSubmission submission, DateTimeOffset syncTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return
        [
            submission.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            submission.ResponseId,
            submission.CustomerName,
            submission.CustomerEmail,
            submission.CustomerPhone,
            submission.ItemReference,
            submission.Quantity,
            FormatCentavosAsPhp(submission.Amount.AmountMinor),
            submission.PaymentMethod,
            "pending",
            submission.OrderId,
            submission.OrderNumber,
            submission.CheckoutUrl,
            submission.StudentId,
            submission.InstructorCredit,
            submission.BookTitle,
            submission.Notes,
            syncTimeUtc.ToString("O", CultureInfo.InvariantCulture)
        ];
    }

    private static string FormatCentavosAsPhp(long centavos)
    {
        long whole = Math.Abs(centavos) / 100;
        long remainder = Math.Abs(centavos) % 100;
        string sign = centavos < 0 ? "-" : "";
        return $"{sign}{whole:N0}.{remainder:D2}";
    }

    public static IReadOnlyList<GoogleFormSubmission> ParseCsvSubmissions(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return Array.Empty<GoogleFormSubmission>();
        }

        var rows = ParseCsvGrid(csvContent);
        if (rows.Count <= 1)
        {
            return Array.Empty<GoogleFormSubmission>();
        }

        var headers = rows[0];
        int colTimestamp = FindColumn(headers, "timestamp", "submitted_at", "created_at", "date");
        int colResponseId = FindColumn(headers, "response_id", "submission_id", "entry_id", "id");
        int colOrderId = FindColumn(headers, "order_id", "backend_order_id");
        int colOrderNumber = FindColumn(headers, "order_number", "order_no", "reference_number");
        int colName = FindColumn(headers, "customer_name", "full_name", "student_name", "enrollee_name", "name", "recipient_name");
        int colEmail = FindColumn(headers, "customer_email", "email_address", "email", "e-mail");
        int colProduct = FindColumn(headers, "product", "item", "book", "course", "description", "product_code", "sku");
        int colQty = FindColumn(headers, "quantity", "qty", "units", "count");
        int colAmount = FindColumn(headers, "amount_centavos", "amount_minor", "total_amount", "amount", "price", "total_php", "unit_price");
        int colStatus = FindColumn(headers, "status", "payment_status", "sync_status", "order_status");

        var results = new List<GoogleFormSubmission>(rows.Count - 1);

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (IsRowEmpty(row)) continue;

            var rawTimestamp = GetCell(row, colTimestamp);
            var createdAt = TryParseTimestamp(rawTimestamp, out var parsedTs)
                ? parsedTs
                : DateTimeOffset.UtcNow.AddMinutes(-(rows.Count - i));

            var customerName = GetCell(row, colName);
            var customerEmail = GetCell(row, colEmail);
            var productText = GetCell(row, colProduct);
            var qtyText = GetCell(row, colQty);
            var rawAmount = GetCell(row, colAmount);
            var rawStatus = GetCell(row, colStatus);

            if (string.IsNullOrWhiteSpace(customerName) &&
                string.IsNullOrWhiteSpace(customerEmail) &&
                string.IsNullOrWhiteSpace(productText))
            {
                continue;
            }

            int quantity = int.TryParse(qtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) && q > 0
                ? q
                : 1;

            long amountCentavos = ParseAmountCentavos(rawAmount, productText, quantity);
            if (amountCentavos <= 0)
            {
                amountCentavos = 45000L * quantity; // Fallback ₱450.00 per unit if column omitted
            }

            var responseId = GetCell(row, colResponseId);
            if (string.IsNullOrWhiteSpace(responseId))
            {
                responseId = $"gf_row_{i}_{ComputeShortHash($"{rawTimestamp}|{customerEmail}|{productText}|{i}")}";
            }

            var hashHex = ComputeShortHash(responseId);
            var orderId = GetCell(row, colOrderId);
            if (string.IsNullOrWhiteSpace(orderId))
            {
                orderId = $"ord_gf_{hashHex[..Math.Min(12, hashHex.Length)]}";
            }

            var orderNumber = GetCell(row, colOrderNumber);
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                orderNumber = $"ORD-{createdAt:yyyyMM}-{hashHex[..Math.Min(4, hashHex.Length)].ToUpperInvariant()}";
            }

            var description = !string.IsNullOrWhiteSpace(productText)
                ? $"{productText} (Qty: {quantity}) - {(string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName)}"
                : $"{orderNumber} - {(string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName)}";

            var normalizedStatus = NormalizeSheetStatus(rawStatus);

            results.Add(new GoogleFormSubmission(
                OrderId: orderId,
                OrderNumber: orderNumber,
                ResponseId: responseId,
                CustomerName: string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName,
                CustomerEmail: customerEmail ?? string.Empty,
                Description: description,
                AmountCentavos: amountCentavos,
                Status: normalizedStatus,
                CreatedAt: createdAt));
        }

        return results;
    }

    private static long ParseAmountCentavos(string? rawAmount, string? productText, int quantity)
    {
        if (!string.IsNullOrWhiteSpace(rawAmount))
        {
            var cleaned = rawAmount
                .Replace("₱", "", StringComparison.Ordinal)
                .Replace("PHP", "", StringComparison.OrdinalIgnoreCase)
                .Replace(",", "", StringComparison.Ordinal)
                .Trim();

            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) && val > 0)
            {
                // If raw value contains a decimal point or is < 100,000 without centavo suffix, convert PHP -> centavos
                if (cleaned.Contains('.', StringComparison.Ordinal) || val < 25000m)
                {
                    return (long)Math.Round(val * 100m, MidpointRounding.AwayFromZero);
                }
                return (long)val;
            }
        }

        // Extract embedded price from product string like "Auditing Theory & Practice - ₱480.00"
        if (!string.IsNullOrWhiteSpace(productText))
        {
            int pesoIdx = productText.IndexOf('₱', StringComparison.Ordinal);
            if (pesoIdx >= 0 && pesoIdx + 1 < productText.Length)
            {
                var sb = new StringBuilder();
                for (int i = pesoIdx + 1; i < productText.Length; i++)
                {
                    char c = productText[i];
                    if (char.IsDigit(c) || c == '.') sb.Append(c);
                    else if (c == ',') continue;
                    else if (sb.Length > 0) break;
                }

                if (decimal.TryParse(sb.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var unitPhp) && unitPhp > 0)
                {
                    return (long)Math.Round(unitPhp * 100m * Math.Max(1, quantity), MidpointRounding.AwayFromZero);
                }
            }
        }

        return 0L;
    }

    private static string NormalizeSheetStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "pending";
        return raw.Trim().ToLowerInvariant() switch
        {
            "paid" or "succeeded" or "completed" => "paid",
            "cancelled" or "canceled" => "cancelled",
            "expired" or "failed" or "abandoned" or "session_expired" => "expired",
            _ => "pending"
        };
    }

    private static int FindColumn(List<string> headers, params string[] candidates)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            var normalized = headers[i].Trim().ToLowerInvariant().Replace(" ", "_", StringComparison.Ordinal);
            foreach (var candidate in candidates)
            {
                if (normalized == candidate || normalized.Contains(candidate, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static string GetCell(List<string> row, int index)
    {
        if (index < 0 || index >= row.Count) return string.Empty;
        return row[index]?.Trim() ?? string.Empty;
    }

    private static bool IsRowEmpty(List<string> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(row[i])) return false;
        }
        return true;
    }

    private static bool TryParseTimestamp(string? raw, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(raw) &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }

    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<List<string>> ParseCsvGrid(string csv)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentCell.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                }
                else
                {
                    currentCell.Append(c);
                }
            }
        }

        if (currentCell.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentCell.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}

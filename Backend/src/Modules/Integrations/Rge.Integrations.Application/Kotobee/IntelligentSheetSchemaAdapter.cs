using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Rge.Integrations.Application.Kotobee;

public sealed record AppsScriptWorkbookPayload(
    string SpreadsheetId,
    string Title,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<AppsScriptSheetTab> Sheets
);

public sealed record AppsScriptSheetTab(
    string TabName,
    IReadOnlyList<string> Headers,
    int RowCount,
    IReadOnlyList<AppsScriptRowData> Rows
);

public sealed record AppsScriptRowData(
    int RowNumber,
    Dictionary<string, string> Values
);

public sealed record AdaptedDynamicBookRule(
    string SourceTabName,
    int SourceRowNumber,
    string ProductSku,
    string PublishingMode,
    string? LibraryId,
    string BookId,
    int? AccessDurationDays,
    bool IsActive,
    bool AllowAutoActivate,
    Dictionary<string, string> ExtraColumns
);

public sealed record AdaptedWorkbookResult(
    IReadOnlyList<AdaptedDynamicBookRule> Rules,
    Dictionary<string, Dictionary<string, string>> DetectedSchemaByTab,
    IReadOnlyList<string> Warnings
);

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed class IntelligentSheetSchemaAdapter
{
    private static readonly Regex SafeIdRegex = new(@"^[A-Za-z0-9_\-\.]{1,64}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string[]> CanonicalAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProductSku"] = ["productsku", "sku", "itemcode", "productcode", "product_sku", "product-sku", "product.sku", "code"],
        ["BookId"] = ["bookid", "bid", "kotobeebid", "kotobee_bid", "kotobee-bid", "ebookid", "book_id"],
        ["LibraryId"] = ["libraryid", "libid", "shelfid", "shelf_id", "library_id", "lib_id", "lib.id"],
        ["PublishingMode"] = ["publishingmode", "mode", "deliverychannel", "platform", "publishing_mode"],
        ["AccessDurationDays"] = ["accessdurationdays", "durationdays", "days", "duration", "accessexpiry", "expirationdays"],
        ["IsActive"] = ["isactive", "active", "enabled", "status", "is_active"],
        ["AllowAutoActivate"] = ["allowautoactivate", "autoactivate", "auto_activate", "instantgrant", "auto"]
    };

    public AdaptedWorkbookResult AdaptWorkbook(
        AppsScriptWorkbookPayload workbook,
        string defaultPublishingMode = "library",
        string? defaultLibraryId = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (workbook.Sheets.Count == 0)
        {
            throw new InvalidOperationException("Workbook must contain at least 1 sheet tab.");
        }

        var warnings = new List<string>();
        var detectedSchemas = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // 1. Detect schema mappings per tab
        var tabSchemas = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Sheets)
        {
            var mapping = DetectTabColumnMapping(sheet);
            tabSchemas[sheet.TabName] = mapping;
            detectedSchemas[sheet.TabName] = mapping;
        }

        // 2. Index auxiliary lookup tabs (e.g. metadata or standalone mapping tabs without full rules)
        var skuToAuxiliaryValues = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var bidToAuxiliaryValues = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Sheets)
        {
            var mapping = tabSchemas[sheet.TabName];
            mapping.TryGetValue("ProductSku", out var skuHeader);
            mapping.TryGetValue("BookId", out var bidHeader);

            foreach (var row in sheet.Rows)
            {
                ValidateRowCellFormulas(row, sheet.TabName);

                string? sku = skuHeader is not null && row.Values.TryGetValue(skuHeader, out var sVal) ? sVal.Trim() : null;
                string? bid = bidHeader is not null && row.Values.TryGetValue(bidHeader, out var bVal) ? bVal.Trim() : null;

                if (!string.IsNullOrWhiteSpace(sku))
                {
                    if (!skuToAuxiliaryValues.TryGetValue(sku, out var dict))
                    {
                        dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        skuToAuxiliaryValues[sku] = dict;
                    }
                    CopyMappedFields(row.Values, mapping, dict);
                }

                if (!string.IsNullOrWhiteSpace(bid))
                {
                    if (!bidToAuxiliaryValues.TryGetValue(bid, out var dict))
                    {
                        dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        bidToAuxiliaryValues[bid] = dict;
                    }
                    CopyMappedFields(row.Values, mapping, dict);
                }
            }
        }

        // 3. Assemble rules from tabs that define either ProductSku or BookId
        var allRules = new List<AdaptedDynamicBookRule>();

        foreach (var sheet in workbook.Sheets)
        {
            var mapping = tabSchemas[sheet.TabName];
            mapping.TryGetValue("ProductSku", out var skuHeader);
            mapping.TryGetValue("BookId", out var bidHeader);
            mapping.TryGetValue("PublishingMode", out var modeHeader);
            mapping.TryGetValue("LibraryId", out var libHeader);
            mapping.TryGetValue("AccessDurationDays", out var daysHeader);
            mapping.TryGetValue("IsActive", out var activeHeader);
            mapping.TryGetValue("AllowAutoActivate", out var autoHeader);

            // If a tab has neither sku nor bookId, warn and skip
            if (skuHeader is null && bidHeader is null)
            {
                warnings.Add($"Tab '{sheet.TabName}' has no ProductSku or BookId header; skipped as lookup-only.");
                continue;
            }

            foreach (var row in sheet.Rows)
            {
                string sku = skuHeader is not null && row.Values.TryGetValue(skuHeader, out var s) ? s.Trim() : string.Empty;
                string bid = bidHeader is not null && row.Values.TryGetValue(bidHeader, out var b) ? b.Trim() : string.Empty;

                // Attempt cross-tab enrichment if SKU or BID was missing in primary row
                if (string.IsNullOrWhiteSpace(bid) && !string.IsNullOrWhiteSpace(sku) && skuToAuxiliaryValues.TryGetValue(sku, out var auxBySku))
                {
                    if (auxBySku.TryGetValue("BookId", out var foundBid)) bid = foundBid;
                }

                if (string.IsNullOrWhiteSpace(sku) && !string.IsNullOrWhiteSpace(bid) && bidToAuxiliaryValues.TryGetValue(bid, out var auxByBid))
                {
                    if (auxByBid.TryGetValue("ProductSku", out var foundSku)) sku = foundSku;
                }

                // If still empty, skip row
                if (string.IsNullOrWhiteSpace(sku) && string.IsNullOrWhiteSpace(bid))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(sku) || !SafeIdRegex.IsMatch(sku))
                {
                    warnings.Add($"Skipping row {row.RowNumber} in tab '{sheet.TabName}': missing or invalid ProductSku.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(bid) || !SafeIdRegex.IsMatch(bid))
                {
                    warnings.Add($"Skipping row {row.RowNumber} in tab '{sheet.TabName}': missing or invalid BookId.");
                    continue;
                }

                // Resolve PublishingMode
                string mode = modeHeader is not null && row.Values.TryGetValue(modeHeader, out var mVal) && !string.IsNullOrWhiteSpace(mVal)
                    ? mVal.Trim().ToLowerInvariant()
                    : defaultPublishingMode;

                if (mode is not ("library" or "cloud"))
                {
                    throw new InvalidOperationException($"Invalid PublishingMode '{mode}' at tab '{sheet.TabName}', row {row.RowNumber}.");
                }

                // Resolve LibraryId
                string? libId = libHeader is not null && row.Values.TryGetValue(libHeader, out var lVal) && !string.IsNullOrWhiteSpace(lVal)
                    ? lVal.Trim()
                    : null;

                if (string.IsNullOrWhiteSpace(libId) && skuToAuxiliaryValues.TryGetValue(sku, out var skuAux) && skuAux.TryGetValue("LibraryId", out var auxLib))
                {
                    libId = auxLib;
                }

                if (string.IsNullOrWhiteSpace(libId))
                {
                    libId = defaultLibraryId;
                }

                if (mode == "cloud")
                {
                    libId = null; // Cloud mode strips LibraryId per Kotobee spec
                }
                else if (string.IsNullOrWhiteSpace(libId))
                {
                    throw new InvalidOperationException($"LibraryId required for library mode at tab '{sheet.TabName}', row {row.RowNumber}.");
                }
                else if (!SafeIdRegex.IsMatch(libId))
                {
                    throw new InvalidOperationException($"Invalid LibraryId format at tab '{sheet.TabName}', row {row.RowNumber}.");
                }

                // Resolve Duration Days
                int? durationDays = null;
                string? daysVal = daysHeader is not null && row.Values.TryGetValue(daysHeader, out var d) ? d.Trim() : null;
                if (string.IsNullOrWhiteSpace(daysVal) && skuToAuxiliaryValues.TryGetValue(sku, out var auxD) && auxD.TryGetValue("AccessDurationDays", out var dAux))
                {
                    daysVal = dAux;
                }

                durationDays = ParseFlexibleDurationDays(daysVal, sheet.TabName, row.RowNumber);

                // Resolve IsActive
                bool isActive = true;
                if (activeHeader is not null && row.Values.TryGetValue(activeHeader, out var actVal))
                {
                    isActive = ParseFlexibleBool(actVal);
                }

                // Resolve AllowAutoActivate
                bool allowAuto = true;
                if (autoHeader is not null && row.Values.TryGetValue(autoHeader, out var autoVal))
                {
                    allowAuto = ParseFlexibleBool(autoVal);
                }

                // Capture Extra Columns (any non-canonical custom headers)
                var extraCols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (header, val) in row.Values)
                {
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    if (!IsCanonicalHeaderMapped(header, mapping))
                    {
                        extraCols[header] = val.Trim();
                    }
                }

                allRules.Add(new AdaptedDynamicBookRule(
                    SourceTabName: sheet.TabName,
                    SourceRowNumber: row.RowNumber,
                    ProductSku: sku,
                    PublishingMode: mode,
                    LibraryId: libId,
                    BookId: bid,
                    AccessDurationDays: durationDays,
                    IsActive: isActive,
                    AllowAutoActivate: allowAuto,
                    ExtraColumns: extraCols
                ));
            }
        }

        var deduplicatedRules = new Dictionary<string, AdaptedDynamicBookRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in allRules)
        {
            string ruleKey = $"{rule.ProductSku}:{rule.BookId}";
            if (!deduplicatedRules.TryGetValue(ruleKey, out var existing))
            {
                deduplicatedRules[ruleKey] = rule;
            }
            else
            {
                foreach (var (k, v) in rule.ExtraColumns)
                {
                    if (!existing.ExtraColumns.ContainsKey(k))
                    {
                        existing.ExtraColumns[k] = v;
                    }
                }
            }
        }

        var finalRules = deduplicatedRules.Values.ToList();
        return new AdaptedWorkbookResult(finalRules, detectedSchemas, warnings);
    }

    private static int? ParseFlexibleDurationDays(string? rawValue, string tabName, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        var trimmed = rawValue.Trim();
        if (string.Equals(trimmed, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "lifetime", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.EndsWith("days", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4].Trim();
        }
        else if (trimmed.EndsWith('d') || trimmed.EndsWith('D'))
        {
            trimmed = trimmed[..^1].Trim();
        }

        if (!int.TryParse(trimmed, out var parsedDays))
        {
            throw new InvalidOperationException($"Invalid AccessDurationDays '{rawValue}' at tab '{tabName}', row {rowNumber}.");
        }

        if (parsedDays <= 0)
        {
            return null; // <= 0 means lifetime access
        }

        if (parsedDays > 36500)
        {
            throw new InvalidOperationException($"Invalid AccessDurationDays '{rawValue}' at tab '{tabName}', row {rowNumber}.");
        }

        return parsedDays;
    }

    private static Dictionary<string, string> DetectTabColumnMapping(AppsScriptSheetTab sheet)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonical, aliases) in CanonicalAliases)
        {
            // Pass 1: Exact match
            var exact = sheet.Headers.FirstOrDefault(h => string.Equals(h?.Trim(), canonical, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                mapping[canonical] = exact;
                continue;
            }

            // Pass 2: Alias match (ignoring whitespace and punctuation)
            foreach (var header in sheet.Headers)
            {
                if (string.IsNullOrWhiteSpace(header)) continue;
                var normalizedHeader = NormalizeHeader(header);
                if (aliases.Any(a => string.Equals(normalizedHeader, NormalizeHeader(a), StringComparison.OrdinalIgnoreCase)))
                {
                    mapping[canonical] = header;
                    break;
                }
            }
        }

        // Pass 3: Value-pattern inference for PublishingMode if not yet mapped
        if (!mapping.ContainsKey("PublishingMode"))
        {
            foreach (var header in sheet.Headers)
            {
                if (string.IsNullOrWhiteSpace(header) || mapping.ContainsValue(header)) continue;
                bool allModeValues = sheet.Rows.Count > 0 && sheet.Rows.All(r =>
                    r.Values.TryGetValue(header, out var v) && (string.IsNullOrWhiteSpace(v) ||
                    string.Equals(v.Trim(), "library", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.Trim(), "cloud", StringComparison.OrdinalIgnoreCase)));

                if (allModeValues)
                {
                    mapping["PublishingMode"] = header;
                    break;
                }
            }
        }

        return mapping;
    }

    private static void ValidateRowCellFormulas(AppsScriptRowData row, string tabName)
    {
        foreach (var (header, val) in row.Values)
        {
            if (string.IsNullOrEmpty(val)) continue;

            char first = val[0];
            if (first is '=' or '@' or '\t' or '\r' or '\n')
            {
                throw new InvalidOperationException(
                    $"SECURITY_VIOLATION: Formula injection detected in tab '{tabName}', row {row.RowNumber}, column '{header}'.");
            }

            if ((first is '+' or '-') && (val.Length == 1 || !char.IsAsciiDigit(val[1])))
            {
                throw new InvalidOperationException(
                    $"SECURITY_VIOLATION: Formula injection detected in tab '{tabName}', row {row.RowNumber}, column '{header}'.");
            }
        }
    }

    private static void CopyMappedFields(
        Dictionary<string, string> source,
        Dictionary<string, string> mapping,
        Dictionary<string, string> target)
    {
        foreach (var (canonical, header) in mapping)
        {
            if (source.TryGetValue(header, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                if (!target.TryGetValue(canonical, out var existingVal) || string.IsNullOrWhiteSpace(existingVal))
                {
                    target[canonical] = val.Trim();
                }
            }
        }
    }

    private static bool IsCanonicalHeaderMapped(string header, Dictionary<string, string> mapping)
        => mapping.Values.Any(mapped => string.Equals(mapped, header, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeHeader(string s)
        => Regex.Replace(s, @"[\s_\-\.]", string.Empty);

    private static bool ParseFlexibleBool(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return false;
        if (bool.TryParse(val, out var b)) return b;
        return val.Trim().ToLowerInvariant() switch
        {
            "yes" or "1" or "active" or "enabled" => true,
            "no" or "0" or "inactive" or "disabled" => false,
            _ => throw new InvalidOperationException($"Invalid boolean value '{val}'.")
        };
    }
}

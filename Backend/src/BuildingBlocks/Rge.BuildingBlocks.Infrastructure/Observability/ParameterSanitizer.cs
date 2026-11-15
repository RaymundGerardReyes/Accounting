using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;

namespace Rge.BuildingBlocks.Infrastructure.Observability;

public sealed record SqlParameterMetadata(
    IReadOnlyList<string> ParameterNames,
    IReadOnlyDictionary<string, string> ParameterTypes,
    string SanitizedValuesJson);

public static class ParameterSanitizer
{
    private const int MaxDepth = 6;
    private const int MaxCollectionItems = 50;

    // Secrets that must never be printed even in local development terminal logs
    private static readonly Regex HardSecretRegex = new(
        @"password|password_hash|private_key|signing_key|client_secret|access_token|refresh_token",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Financial/PII fields redacted in ProductionLocked mode
    private static readonly Regex ProductionSensitiveKeyRegex = new(
        @"password|password_hash|secret|token|authorization|cookie|tin|bank_account|bankaccount|card_number|cardnumber|cvv|cvc|raw_payload|rawpayload|code_verifier|csrf|private_key|signing_key",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailKeyRegex = new(@"email", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ProductionAllowedSqlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "OrderId", "OrderNumber", "AttemptId", "ReceiptId", "AdvanceId", "DisbursementId",
        "ReportId", "PayableId", "RequestId", "VendorId", "PolicyId", "Status", "Category",
        "Currency", "SourceChannel", "Limit", "Offset", "PageSize", "Workflow", "Stage", "UserId"
    };

    public static string FormatRequestParameters(object? request)
    {
        if (request is null)
        {
            return "{}";
        }

        try
        {
            using var doc = JsonSerializer.SerializeToDocument(request);
            var fullBlast = RuntimeLogEnvironmentGate.IsDevelopmentFullBlast;
            var processed = ProcessElement(doc.RootElement, propertyName: null, depth: 0, fullBlast);
            return JsonSerializer.Serialize(processed);
        }
        catch
        {
            return JsonSerializer.Serialize(new { Type = request.GetType().Name });
        }
    }

    public static string SanitizeRequestParameters(object? request) => FormatRequestParameters(request);

    public static SqlParameterMetadata ExtractSqlParameters(object? parameters)
    {
        if (parameters is null)
        {
            return new SqlParameterMetadata(Array.Empty<string>(), new Dictionary<string, string>(), "{}");
        }

        var fullBlast = RuntimeLogEnvironmentGate.IsDevelopmentFullBlast;
        var names = new List<string>();
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (parameters is DynamicParameters dynamicParams)
        {
            foreach (var name in dynamicParams.ParameterNames)
            {
                names.Add(name);
                types[name] = "DynamicParameter";
                if (fullBlast || ProductionAllowedSqlKeys.Contains(name))
                {
                    try
                    {
                        var rawVal = dynamicParams.Get<object?>(name);
                        values[name] = FormatScalarValue(name, rawVal, fullBlast);
                    }
                    catch
                    {
                        values[name] = "[Bound]";
                    }
                }
            }
        }
        else
        {
            var props = parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead)
                {
                    continue;
                }

                var name = prop.Name;
                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                names.Add(name);
                types[name] = propType.Name;

                if (fullBlast)
                {
                    // DEVELOPMENT FULL-BLAST: Include every bound SQL parameter value (except raw passwords/keys)
                    values[name] = FormatScalarValue(name, prop.GetValue(parameters), fullBlast: true);
                }
                else
                {
                    // PRODUCTION LOCKED: Redact sensitive keys and only log explicitly allowlisted keys
                    if (ProductionSensitiveKeyRegex.IsMatch(name))
                    {
                        values[name] = "[REDACTED_IN_PROD]";
                    }
                    else if (ProductionAllowedSqlKeys.Contains(name))
                    {
                        values[name] = FormatScalarValue(name, prop.GetValue(parameters), fullBlast: false);
                    }
                }
            }
        }

        return new SqlParameterMetadata(
            ParameterNames: names,
            ParameterTypes: types,
            SanitizedValuesJson: JsonSerializer.Serialize(values));
    }

    private static object? ProcessElement(JsonElement element, string? propertyName, int depth, bool fullBlast)
    {
        if (depth > MaxDepth)
        {
            return "[MAX_DEPTH]";
        }

        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            if (HardSecretRegex.IsMatch(propertyName))
            {
                return "[REDACTED_SECRET]";
            }

            if (!fullBlast)
            {
                if (ProductionSensitiveKeyRegex.IsMatch(propertyName))
                {
                    return "[REDACTED_IN_PROD]";
                }

                if (EmailKeyRegex.IsMatch(propertyName) && element.ValueKind == JsonValueKind.String)
                {
                    return MaskEmail(element.GetString());
                }
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => ProcessObject(element, depth + 1, fullBlast),
            JsonValueKind.Array => ProcessArray(element, propertyName, depth + 1, fullBlast),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object?> ProcessObject(JsonElement obj, int depth, bool fullBlast)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.EnumerateObject())
        {
            dict[prop.Name] = ProcessElement(prop.Value, prop.Name, depth, fullBlast);
        }
        return dict;
    }

    private static List<object?> ProcessArray(JsonElement arr, string? propName, int depth, bool fullBlast)
    {
        var list = new List<object?>();
        var count = 0;
        foreach (var item in arr.EnumerateArray())
        {
            if (count >= MaxCollectionItems)
            {
                list.Add("[TRUNCATED]");
                break;
            }
            list.Add(ProcessElement(item, propName, depth, fullBlast));
            count++;
        }
        return list;
    }

    private static string? FormatScalarValue(string key, object? value, bool fullBlast)
    {
        if (value is null)
        {
            return null;
        }

        if (HardSecretRegex.IsMatch(key))
        {
            return "[REDACTED_SECRET]";
        }

        if (!fullBlast)
        {
            if (ProductionSensitiveKeyRegex.IsMatch(key))
            {
                return "[REDACTED_IN_PROD]";
            }

            if (EmailKeyRegex.IsMatch(key))
            {
                return MaskEmail(value.ToString());
            }
        }

        if (value is DateTimeOffset dto)
        {
            return dto.ToString("O");
        }

        if (value is DateTime dt)
        {
            return dt.ToUniversalTime().ToString("O");
        }

        return value.ToString();
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : $"{trimmed[0]}***@{trimmed[(at + 1)..]}";
    }
}

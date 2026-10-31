using System.Text.Json;
using Rge.Payments.Application.Ports;

namespace Rge.Payments.Infrastructure.Catalog;

public sealed class JsonProductCatalog : IProductCatalog
{
    private static readonly Dictionary<string, int> StaticProductUnitCostsPesos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Product A"] = 125,
        ["Product B"] = 89,
        ["Product C"] = 156,
        ["Product D"] = 203,
        ["Product E"] = 67,
        ["COST-ACC-TEXT-2026"] = 1850,
        ["CPA-REV-AUDIT-2026"] = 1450
    };

    private readonly Dictionary<string, int> _loadedPrices = new(StringComparer.OrdinalIgnoreCase);

    public JsonProductCatalog()
    {
        foreach (var kvp in StaticProductUnitCostsPesos)
        {
            _loadedPrices[kvp.Key] = kvp.Value;
        }

        TryLoadFromFile();
    }

    private void TryLoadFromFile()
    {
        try
        {
            string? candidatePath = LocateProductDataFile();
            if (candidatePath is not null && File.Exists(candidatePath))
            {
                string json = File.ReadAllText(candidatePath);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("productData", out JsonElement array) &&
                    array.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        if (item.TryGetProperty("product", out JsonElement p) &&
                            item.TryGetProperty("unitCost", out JsonElement c) &&
                            p.GetString() is string prodName &&
                            c.TryGetInt32(out int cost))
                        {
                            _loadedPrices[prodName] = cost;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
    }

    private static string? LocateProductDataFile()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "data", "product_data.json"),
            Path.Combine(AppContext.BaseDirectory, "Backend", "data", "product_data.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Backend", "data", "product_data.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "product_data.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Backend", "data", "product_data.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Backend", "data", "product_data.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Backend", "data", "product_data.json")
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore path resolution errors
            }
        }

        DirectoryInfo? current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            string checkBackend = Path.Combine(current.FullName, "Backend", "data", "product_data.json");
            if (File.Exists(checkBackend))
            {
                return checkBackend;
            }

            string checkDirect = Path.Combine(current.FullName, "data", "product_data.json");
            if (File.Exists(checkDirect))
            {
                return checkDirect;
            }

            current = current.Parent;
        }

        return null;
    }

    public Task<int?> GetUnitPricePesosAsync(string productCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return Task.FromResult<int?>(null);
        }

        if (_loadedPrices.TryGetValue(productCode.Trim(), out int price))
        {
            return Task.FromResult<int?>(price);
        }

        return Task.FromResult<int?>(null);
    }
}

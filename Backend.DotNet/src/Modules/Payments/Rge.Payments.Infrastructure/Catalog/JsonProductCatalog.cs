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
        ["Product E"] = 67
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
            string candidatePath = Path.Combine(AppContext.BaseDirectory, "data", "product_data.json");
            if (!File.Exists(candidatePath))
            {
                candidatePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "product_data.json");
            }
            if (!File.Exists(candidatePath))
            {
                candidatePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Backend", "data", "product_data.json");
            }

            if (File.Exists(candidatePath))
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

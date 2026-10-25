using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rge.BuildingBlocks.Infrastructure.Crypto;

/// <summary>
/// Produces deterministic, key-sorted JSON strings for cryptographic hashing and audit snapshots.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string SerializeDeterministic<T>(T value)
    {
        string rawJson = JsonSerializer.Serialize(value, CompactOptions);
        JsonNode? node = JsonNode.Parse(rawJson);
        if (node is null)
        {
            return "{}";
        }

        JsonNode sorted = SortNode(node);
        return sorted.ToJsonString(CompactOptions);
    }

    private static JsonNode SortNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var sortedObj = new JsonObject();
            foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sortedObj[kvp.Key] = kvp.Value is not null ? SortNode(kvp.Value) : null;
            }
            return sortedObj;
        }

        if (node is JsonArray arr)
        {
            var sortedArr = new JsonArray();
            foreach (var item in arr)
            {
                sortedArr.Add(item is not null ? SortNode(item) : null);
            }
            return sortedArr;
        }

        return node.DeepClone();
    }
}

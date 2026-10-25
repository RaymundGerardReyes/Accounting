using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Rge.BuildingBlocks.Api.Serialization;

/// <summary>
/// System.Text.Json source generation context for high-performance, AOT-compatible serialization.
/// Pre-registers common API response models and collections.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
public sealed partial class AppJsonContext : JsonSerializerContext;


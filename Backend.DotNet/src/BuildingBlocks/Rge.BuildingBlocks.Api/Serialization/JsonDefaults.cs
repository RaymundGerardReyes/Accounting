using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rge.BuildingBlocks.Api.Serialization;

/// <summary>
/// Central JSON serialization configuration enforcing snake_case property naming
/// to match the legacy Python backend responses.
/// </summary>
public static class JsonDefaults
{
    private static readonly JsonSerializerOptions DefaultOptionsInstance = CreateOptions();

    public static JsonSerializerOptions Options => DefaultOptionsInstance;

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        ConfigureOptions(options);
        return options;
    }

    public static void ConfigureOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }
}

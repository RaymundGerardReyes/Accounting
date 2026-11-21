namespace Rge.Integrations.Application.Kotobee;

public sealed class KotobeeOptions
{
    public const string SectionName = "Integrations:Kotobee";

    /// <summary>Master switch for Kotobee integration. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base API URL. Official default: https://www.kotobee.com/api/v1</summary>
    public string BaseUrl { get; set; } = "https://www.kotobee.com/api/v1";

    /// <summary>
    /// Preferred auth method: library-scoped or cloud-ebook-scoped accesskey.
    /// Store in environment variables / SecretStoreWriter, NEVER hardcode.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>Fallback account-wide serial (only used if AccessKey is empty).</summary>
    public string? Serial { get; set; }

    /// <summary>"library" (recommended for multi-book catalog) or "cloud".</summary>
    public string PublishingMode { get; set; } = "library";

    /// <summary>Required when PublishingMode == "library".</summary>
    public string? DefaultLibraryId { get; set; }

    /// <summary>
    /// Minimum delay between Kotobee API calls.
    /// Kotobee enforces 1 req/sec; 1100ms adds a safe 100ms clock-jitter buffer.
    /// </summary>
    public int MinRequestIntervalMs { get; set; } = 1100;

    /// <summary>If true, sends active=1 so user is immediately active on creation.</summary>
    public bool ActivateUsersImmediately { get; set; } = true;

    /// <summary>Optional SKU-to-Kotobee book mapping (supports 1 SKU -> 1 book or N bundle books).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Configuration binding")]
    public Dictionary<string, List<KotobeeProductBookMapping>> ProductMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record KotobeeProductBookMapping(
    string BookId,                // Kotobee 'bid' (library mode) or 'cid' (cloud mode)
    string? LibraryId = null,     // Optional override of DefaultLibraryId
    int? AccessDurationDays = null // Null = lifetime access
);

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Application.Kotobee;

public sealed class DynamicSheetKotobeeCatalogProvider : IKotobeeCatalogMappingResolver
{
    private readonly IKotobeeSheetSnapshotStore? _snapshotStore;
    private readonly IOptionsMonitor<KotobeeOptions> _optionsMonitor;
    private readonly ILogger<DynamicSheetKotobeeCatalogProvider> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public DynamicSheetKotobeeCatalogProvider(
        IOptionsMonitor<KotobeeOptions> optionsMonitor,
        IKotobeeSheetSnapshotStore? snapshotStore = null,
        ILogger<DynamicSheetKotobeeCatalogProvider>? logger = null)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _snapshotStore = snapshotStore;
        _logger = logger ?? NullLogger<DynamicSheetKotobeeCatalogProvider>.Instance;
    }

    public IReadOnlyList<ResolvedKotobeeBookTarget> ResolveBooksForOrder(IEnumerable<string> purchasedSkus)
    {
        if (purchasedSkus is null)
        {
            return [];
        }

        var opts = _optionsMonitor.CurrentValue;
        var activeMappings = LoadEffectiveMappings(opts);

        var seenBookIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ResolvedKotobeeBookTarget>();

        foreach (var rawSku in purchasedSkus)
        {
            if (string.IsNullOrWhiteSpace(rawSku))
            {
                continue;
            }

            var sku = rawSku.Trim();
            if (!activeMappings.TryGetValue(sku, out var mappings) || mappings.Count == 0)
            {
                continue;
            }

            foreach (var map in mappings)
            {
                if (string.IsNullOrWhiteSpace(map.BookId))
                {
                    continue;
                }

                // Deduplicate in case a bundle overlaps with an individual book in the same order
                if (!seenBookIds.Add(map.BookId.Trim()))
                {
                    continue;
                }

                resolved.Add(new ResolvedKotobeeBookTarget(
                    SourceSku: sku,
                    BookId: map.BookId.Trim(),
                    LibraryId: string.IsNullOrWhiteSpace(map.LibraryId)
                        ? opts.DefaultLibraryId
                        : map.LibraryId.Trim(),
                    AccessDurationDays: map.AccessDurationDays
                ));
            }
        }

        return resolved;
    }

    private Dictionary<string, List<KotobeeProductBookMapping>> LoadEffectiveMappings(KotobeeOptions opts)
    {
        if (_snapshotStore is not null)
        {
            try
            {
                // Synchronous bridge for IKotobeeCatalogMappingResolver interface
                var snapshot = _snapshotStore.GetLatestCurrentSnapshotAsync().GetAwaiter().GetResult();
                if (snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.MappingsJson))
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, List<KotobeeProductBookMapping>>>(
                        snapshot.MappingsJson,
                        JsonOptions);

                    if (parsed is not null && parsed.Count > 0)
                    {
                        return new Dictionary<string, List<KotobeeProductBookMapping>>(parsed, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed loading dynamic Google Sheet snapshot. Falling back to static appsettings.");
            }
        }

        return new Dictionary<string, List<KotobeeProductBookMapping>>(opts.ProductMappings, StringComparer.OrdinalIgnoreCase);
    }

    public static void ValidateMassWipeGuard(int newRowCount, int? previousRowCount, double maxAllowedDropPercentage = 0.50)
    {
        if (!previousRowCount.HasValue || previousRowCount.Value <= 0)
        {
            return;
        }

        int previousCount = previousRowCount.Value;
        if (newRowCount < (previousCount * (1.0 - maxAllowedDropPercentage)))
        {
            throw new InvalidOperationException(
                $"Mass wipe prevented: incoming row count ({newRowCount}) is more than {maxAllowedDropPercentage:P0} below previous active count ({previousCount}). Manual approval required.");
        }
    }

    public static Dictionary<string, List<KotobeeProductBookMapping>> RulesToProductMappings(
        IEnumerable<AdaptedDynamicBookRule> rules,
        string? defaultLibraryId = null)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var result = new Dictionary<string, List<KotobeeProductBookMapping>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules.Where(r => r.IsActive && r.AllowAutoActivate))
        {
            if (string.IsNullOrWhiteSpace(rule.ProductSku) || string.IsNullOrWhiteSpace(rule.BookId))
            {
                continue;
            }

            var sku = rule.ProductSku.Trim();
            if (!result.TryGetValue(sku, out var list))
            {
                list = [];
                result[sku] = list;
            }

            var libId = string.IsNullOrWhiteSpace(rule.LibraryId) ? defaultLibraryId : rule.LibraryId.Trim();
            list.Add(new KotobeeProductBookMapping(
                BookId: rule.BookId.Trim(),
                LibraryId: libId,
                AccessDurationDays: rule.AccessDurationDays
            ));
        }

        return result;
    }
}

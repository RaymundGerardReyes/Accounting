using Microsoft.Extensions.Options;

namespace Rge.Integrations.Application.Kotobee;

public sealed record ResolvedKotobeeBookTarget(
    string SourceSku,
    string BookId,
    string? LibraryId,
    int? AccessDurationDays
);

public interface IKotobeeCatalogMappingResolver
{
    IReadOnlyList<ResolvedKotobeeBookTarget> ResolveBooksForOrder(IEnumerable<string> purchasedSkus);
}

public sealed class KotobeeCatalogMappingResolver : IKotobeeCatalogMappingResolver
{
    private readonly IOptionsMonitor<KotobeeOptions> _optionsMonitor;

    public KotobeeCatalogMappingResolver(IOptionsMonitor<KotobeeOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
    }

    public IReadOnlyList<ResolvedKotobeeBookTarget> ResolveBooksForOrder(IEnumerable<string> purchasedSkus)
    {
        if (purchasedSkus is null)
        {
            return [];
        }

        var opts = _optionsMonitor.CurrentValue;
        var seenBookIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ResolvedKotobeeBookTarget>();

        foreach (var rawSku in purchasedSkus)
        {
            if (string.IsNullOrWhiteSpace(rawSku))
            {
                continue;
            }

            var sku = rawSku.Trim();
            if (!opts.ProductMappings.TryGetValue(sku, out var mappings) || mappings.Count == 0)
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
}

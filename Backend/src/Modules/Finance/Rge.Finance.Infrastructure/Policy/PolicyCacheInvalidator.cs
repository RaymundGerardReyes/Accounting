namespace Rge.Finance.Infrastructure.Policy;

/// <summary>
/// Domain service invalidating cached policies upon version publication, approval, or retirement.
/// </summary>
public sealed class PolicyCacheInvalidator
{
    private readonly PolicyCache _cache;

    public PolicyCacheInvalidator(PolicyCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    public void Invalidate(string policyId)
    {
        _cache.Remove(policyId);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }
}

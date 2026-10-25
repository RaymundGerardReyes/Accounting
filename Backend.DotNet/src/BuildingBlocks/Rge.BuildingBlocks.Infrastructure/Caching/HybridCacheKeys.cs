namespace Rge.BuildingBlocks.Infrastructure.Caching;



/// <summary>

/// Centralized cache key catalog for L1/HybridCache entries.

/// Guarantees deterministic invalidation when policies, roles, or catalogs are updated.

/// </summary>

public static class HybridCacheKeys

{

    public const string ActivePoliciesPrefix = "rge:finance:policies:active";

    public const string ApprovalMatrixKey = "rge:finance:approval-matrix:v1";

    public const string ProductCatalogKey = "rge:payments:product-catalog:v1";

    public const string PhHolidayCalendarKey = "rge:finance:holiday-calendar:ph";



    public static string ActorById(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return $"rge:identity:actor:{userId.ToLowerInvariant()}";
    }

    public static string PolicyByCode(string policyCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyCode);
        return $"{ActivePoliciesPrefix}:{policyCode.ToLowerInvariant()}";
    }

}

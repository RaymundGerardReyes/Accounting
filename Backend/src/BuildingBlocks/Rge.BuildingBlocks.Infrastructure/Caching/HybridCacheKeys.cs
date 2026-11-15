namespace Rge.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Centralized cache key catalog for L1/HybridCache entries.
/// Guarantees deterministic invalidation when policies, roles, or catalogs are updated.
/// </summary>
public static class HybridCacheKeys
{
    public const string OrdersTag = "orders";
    public const string OutboxTag = "outbox";
    public const string AdminOutboxTag = "admin:outbox";
    public const string WebhooksTag = "webhooks";
    public const string AdminWebhooksTag = "admin:webhooks";
    public const string GoogleFormsTag = "google-forms";

    public const string ActivePoliciesPrefix = "rge:finance:policies:active";
    public const string ApprovalMatrixKey = "rge:finance:approval-matrix:v1";
    public const string ProductCatalogKey = "rge:payments:product-catalog:v1";
    public const string PhHolidayCalendarKey = "rge:finance:holiday-calendar:ph";

    public static string AdminFulfillmentOutboxList(string? status = null, int limit = 100)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? "all"
            : status.Trim().ToLowerInvariant();
        var clampedLimit = limit <= 0 ? 100 : Math.Min(limit, 500);
        return $"admin:fulfillment-outbox:list:{normalizedStatus}:{clampedLimit}";
    }

    public static string AdminWebhookEventsList(string? status = null, int limit = 100)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? "all"
            : status.Trim().ToLowerInvariant();
        var clampedLimit = limit <= 0 ? 100 : Math.Min(limit, 500);
        return $"admin:webhook-events:list:{normalizedStatus}:{clampedLimit}";
    }

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

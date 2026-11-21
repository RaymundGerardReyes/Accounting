using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Application.Kotobee;

[SuppressMessage("Performance", "CA1822:Mark members as static")]
public sealed class KotobeePreExecutionIsolationGuard
{
    private static readonly HashSet<string> AllowedSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "created_at_utc",
        "updated_at_utc",
        "activated_at_utc",
        "status",
        "buyer_email",
        "product_sku",
        "order_id",
        "kotobee_book_id",
        "attempt_count"
    };

    private static readonly HashSet<string> AllowedSortDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc",
        "desc"
    };

    private static readonly Regex SqlInjectionPattern = new(
        @"(;|--|/\*|\*/|xp_|drop\s+table|delete\s+from|insert\s+into|update\s+.*set|union\s+select|exec\s*\(|execute\s*\(|char\s*\(|waitfor\s+delay|'\s*or\s*['\d\w]+\s*=\s*['\d\w]+|\bor\b\s+['\d\w]+\s*=\s*['\d\w]+|\band\b\s+['\d\w]+\s*=\s*['\d\w]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public void ValidateSortParameters(string? sortBy, string? sortDirection)
    {
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            var trimmedSort = sortBy.Trim();
            if (!AllowedSortColumns.Contains(trimmedSort))
            {
                throw new ArgumentException($"Sort column '{sortBy}' is not allowlisted.", nameof(sortBy));
            }
            ValidateInputAntiSqlInjection(trimmedSort, nameof(sortBy));
        }

        if (!string.IsNullOrWhiteSpace(sortDirection))
        {
            var trimmedDir = sortDirection.Trim();
            if (!AllowedSortDirections.Contains(trimmedDir))
            {
                throw new ArgumentException($"Sort direction '{sortDirection}' is invalid. Allowed values: 'asc', 'desc'.", nameof(sortDirection));
            }
        }
    }

    public void ValidateInputAntiSqlInjection(string? input, string paramName)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (SqlInjectionPattern.IsMatch(input))
        {
            throw new ArgumentException($"Potential SQL injection detected in parameter '{paramName}'.", paramName);
        }
    }

    public void ValidateAuditContext(string? actorId, string? auditReason)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new InvalidOperationException("Audit violation: actor ID is required for this operation.");
        }

        if (string.IsNullOrWhiteSpace(auditReason) || auditReason.Trim().Length < 3)
        {
            throw new InvalidOperationException("Audit violation: audit reason is required and must be at least 3 characters long.");
        }
    }

    public void ValidateNotPhysicalDelete(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (string.Equals(operation, "DELETE", StringComparison.OrdinalIgnoreCase) ||
            operation.Contains("delete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SECURITY_VIOLATION: Physical DELETE on kotobee records is strictly blocked. Use audited revocation instead.");
        }
    }

    public void ValidateEntitlementStatusTransition(string currentStatus, string targetStatus)
    {
        if (string.Equals(currentStatus, "Active", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(targetStatus, "Pending", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(targetStatus, "Failed", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"SECURITY_VIOLATION: Illegal downgrade from Active to {targetStatus} blocked.");
        }

        if (string.Equals(currentStatus, "Revoked", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetStatus, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SECURITY_VIOLATION: Revoked entitlement cannot be flipped to {targetStatus}. Create a new paid order instead.");
        }
    }

    public void ValidateImmutableIdentity(KotobeeEntitlementRecord existing, KotobeeEntitlementRecord updated)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(updated);

        if (existing.Id != updated.Id ||
            !string.Equals(existing.OrderId, updated.OrderId, StringComparison.Ordinal) ||
            !string.Equals(existing.ProductSku, updated.ProductSku, StringComparison.Ordinal) ||
            !string.Equals(existing.BuyerEmail, updated.BuyerEmail, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existing.KotobeeBookId, updated.KotobeeBookId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"SECURITY_VIOLATION: Attempted modification of immutable transaction fields on kotobee entitlement {existing.Id}.");
        }
    }
}

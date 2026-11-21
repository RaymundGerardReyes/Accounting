using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Api;

public sealed class KotobeeEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // 1. Connectivity probe + fetch Kotobee book list
        var booksHandler = async (
            string? libId,
            IKotobeeApiClient client,
            KotobeePreExecutionIsolationGuard guard,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(libId))
            {
                guard.ValidateInputAntiSqlInjection(libId, nameof(libId));
            }

            var books = await client.ListBooksAsync(libId, ct).ConfigureAwait(false);
            return Results.Ok(new { count = books.Count, books });
        };

        app.MapGet("/api/v1/integrations/kotobee/books", booksHandler).WithTags("Kotobee");
        app.MapGet("/api/integrations/kotobee/books", booksHandler).WithTags("Kotobee");

        // 2. Read-only entitlement status (used by Payment Result page & Operations Hub)
        var entitlementsHandler = async (
            string orderId,
            string? sortBy,
            string? sortDirection,
            IKotobeeEntitlementRepository repo,
            KotobeePreExecutionIsolationGuard guard,
            CancellationToken ct) =>
        {
            guard.ValidateInputAntiSqlInjection(orderId, nameof(orderId));
            guard.ValidateSortParameters(sortBy, sortDirection);

            var records = await repo.GetByOrderIdAsync(orderId, ct).ConfigureAwait(false);
            var query = records.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(sortBy))
            {
                bool desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
                query = sortBy.ToLowerInvariant() switch
                {
                    "created_at_utc" => desc ? query.OrderByDescending(r => r.ActivatedAtUtc ?? DateTimeOffset.MinValue) : query.OrderBy(r => r.ActivatedAtUtc ?? DateTimeOffset.MinValue),
                    "status" => desc ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status),
                    "buyer_email" => desc ? query.OrderByDescending(r => r.BuyerEmail) : query.OrderBy(r => r.BuyerEmail),
                    "product_sku" => desc ? query.OrderByDescending(r => r.ProductSku) : query.OrderBy(r => r.ProductSku),
                    "attempt_count" => desc ? query.OrderByDescending(r => r.AttemptCount) : query.OrderBy(r => r.AttemptCount),
                    _ => query
                };
            }

            return Results.Ok(query.Select(r => new
            {
                productSku = r.ProductSku,
                kotobeeBookId = r.KotobeeBookId,
                status = r.Status,
                bookAccessState = r.BookAccessState,
                activatedAtUtc = r.ActivatedAtUtc,
                expiresAtUtc = r.ExpiresAtUtc,
                lastError = r.LastError,
                attemptCount = r.AttemptCount
            }));
        };

        app.MapGet("/api/v1/integrations/kotobee/orders/{orderId}/entitlements", entitlementsHandler).WithTags("Kotobee");
        app.MapGet("/api/integrations/kotobee/orders/{orderId}/entitlements", entitlementsHandler).WithTags("Kotobee");

        // 3. Admin-only single-book revocation
        var revokeHandler = async (
            string orderId,
            string bookId,
            string? actorId,
            string? auditReason,
            IKotobeeEntitlementRepository repo,
            IKotobeeApiClient client,
            KotobeePreExecutionIsolationGuard guard,
            CancellationToken ct) =>
        {
            guard.ValidateInputAntiSqlInjection(orderId, nameof(orderId));
            guard.ValidateInputAntiSqlInjection(bookId, nameof(bookId));
            guard.ValidateAuditContext(actorId ?? "admin", auditReason ?? "Administrative book revocation");

            var existing = (await repo.GetByOrderIdAsync(orderId, ct).ConfigureAwait(false))
                .FirstOrDefault(r => string.Equals(r.KotobeeBookId, bookId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                return Results.NotFound();
            }

            guard.ValidateEntitlementStatusTransition(existing.Status, "Revoked");

            var res = await client.RevokeBookAccessAsync(
                existing.BuyerEmail, existing.KotobeeUid, existing.KotobeeLibId, bookId, ct).ConfigureAwait(false);

            if (!res.Success)
            {
                return Results.Problem(res.ErrorMessage ?? "Kotobee book access revocation failed.", statusCode: 502);
            }

            await repo.MarkRevokedAsync(orderId, bookId, ct).ConfigureAwait(false);
            return Results.Ok(new { orderId, bookId, status = "Revoked" });
        };

        app.MapPost("/api/v1/integrations/kotobee/orders/{orderId}/books/{bookId}/revoke", revokeHandler).WithTags("Kotobee");
        app.MapPost("/api/integrations/kotobee/orders/{orderId}/books/{bookId}/revoke", revokeHandler).WithTags("Kotobee");
    }
}

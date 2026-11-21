using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rge.BuildingBlocks.Api.Endpoints;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Api;

public sealed class KotobeeEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // 1. Connectivity probe + fetch Kotobee book list
        var booksHandler = async (string? libId, IKotobeeApiClient client, CancellationToken ct) =>
        {
            var books = await client.ListBooksAsync(libId, ct).ConfigureAwait(false);
            return Results.Ok(new { count = books.Count, books });
        };

        app.MapGet("/api/v1/integrations/kotobee/books", booksHandler).WithTags("Kotobee");
        app.MapGet("/api/integrations/kotobee/books", booksHandler).WithTags("Kotobee");

        // 2. Read-only entitlement status (used by Payment Result page & Operations Hub)
        var entitlementsHandler = async (
            string orderId,
            IKotobeeEntitlementRepository repo,
            CancellationToken ct) =>
        {
            var records = await repo.GetByOrderIdAsync(orderId, ct).ConfigureAwait(false);
            return Results.Ok(records.Select(r => new
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
            IKotobeeEntitlementRepository repo,
            IKotobeeApiClient client,
            CancellationToken ct) =>
        {
            var existing = (await repo.GetByOrderIdAsync(orderId, ct).ConfigureAwait(false))
                .FirstOrDefault(r => string.Equals(r.KotobeeBookId, bookId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                return Results.NotFound();
            }

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

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

public sealed class PgKotobeeEntitlementRepository : IKotobeeEntitlementRepository
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly ILogger<PgKotobeeEntitlementRepository> _logger;
    private readonly ConcurrentDictionary<string, KotobeeEntitlementRecord> _fallbackCache = new(StringComparer.OrdinalIgnoreCase);

    public PgKotobeeEntitlementRepository(
        NpgsqlDataSource? dataSource = null,
        ILogger<PgKotobeeEntitlementRepository>? logger = null)
    {
        _dataSource = dataSource;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PgKotobeeEntitlementRepository>.Instance;
    }

    public async Task UpsertPendingAsync(KotobeeEntitlementRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_dataSource is null)
        {
            string key = $"{record.OrderId}:{record.KotobeeBookId}";
            _fallbackCache.AddOrUpdate(key, record, (_, existing) => record with
            {
                AttemptCount = existing.AttemptCount + 1,
                Status = string.Equals(existing.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Pending"
            });
            return;
        }

        const string sql = """
            SELECT set_config('app.audit_actor', 'kotobee-fulfillment-worker', true),
                   set_config('app.audit_reason', 'Enqueue pending book entitlement for paid order', true);

            INSERT INTO kotobee_entitlements (
                id, order_id, product_sku, buyer_email, kotobee_uid,
                kotobee_mode, kotobee_lib_id, kotobee_book_id, status,
                expires_at_utc, created_at_utc, updated_at_utc
            ) VALUES (
                @id, @order_id, @product_sku, @buyer_email, @kotobee_uid,
                @kotobee_mode, @kotobee_lib_id, @kotobee_book_id, 'Pending',
                @expires_at_utc, NOW(), NOW()
            )
            ON CONFLICT (order_id, kotobee_book_id)
            DO UPDATE SET
                kotobee_uid = COALESCE(EXCLUDED.kotobee_uid, kotobee_entitlements.kotobee_uid),
                attempt_count = kotobee_entitlements.attempt_count + 1,
                status = CASE WHEN kotobee_entitlements.status = 'Active' THEN 'Active' ELSE 'Pending' END,
                updated_at_utc = NOW();
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("id", record.Id);
            cmd.Parameters.AddWithValue("order_id", record.OrderId);
            cmd.Parameters.AddWithValue("product_sku", record.ProductSku);
            cmd.Parameters.AddWithValue("buyer_email", record.BuyerEmail);
            cmd.Parameters.AddWithValue("kotobee_uid", (object?)record.KotobeeUid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("kotobee_mode", record.KotobeeMode);
            cmd.Parameters.AddWithValue("kotobee_lib_id", (object?)record.KotobeeLibId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("kotobee_book_id", record.KotobeeBookId);
            cmd.Parameters.AddWithValue("expires_at_utc", record.ExpiresAtUtc.HasValue ? record.ExpiresAtUtc.Value : DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed persisting pending entitlement for Order {OrderId}, Book {BookId} to Postgres. Falling back to in-memory cache.", record.OrderId, record.KotobeeBookId);
            string key = $"{record.OrderId}:{record.KotobeeBookId}";
            _fallbackCache[key] = record;
        }
    }

    public async Task MarkActiveAsync(string orderId, string bookId, string? uid, string bookAccess, CancellationToken ct = default)
    {
        string key = $"{orderId}:{bookId}";
        if (_fallbackCache.TryGetValue(key, out var cached))
        {
            _fallbackCache[key] = cached with
            {
                Status = "Active",
                KotobeeUid = uid ?? cached.KotobeeUid,
                BookAccessState = bookAccess,
                ActivatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        if (_dataSource is null)
        {
            return;
        }

        const string sql = """
            SELECT set_config('app.audit_actor', 'kotobee-fulfillment-worker', true),
                   set_config('app.audit_reason', 'Active entitlement confirmed in Kotobee API', true);

            UPDATE kotobee_entitlements
            SET status = 'Active',
                kotobee_uid = COALESCE(@uid, kotobee_uid),
                book_access_state = @access,
                last_error = NULL,
                activated_at_utc = COALESCE(activated_at_utc, NOW()),
                updated_at_utc = NOW()
            WHERE order_id = @order_id AND kotobee_book_id = @book_id;
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("uid", (object?)uid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("access", bookAccess);
            cmd.Parameters.AddWithValue("order_id", orderId);
            cmd.Parameters.AddWithValue("book_id", bookId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed marking entitlement active for Order {OrderId}, Book {BookId} in Postgres.", orderId, bookId);
        }
    }

    public async Task MarkFailedAsync(string orderId, string bookId, string error, CancellationToken ct = default)
    {
        string key = $"{orderId}:{bookId}";
        if (_fallbackCache.TryGetValue(key, out var cached))
        {
            _fallbackCache[key] = cached with
            {
                Status = "Failed",
                LastError = error
            };
        }

        if (_dataSource is null)
        {
            return;
        }

        const string sql = """
            SELECT set_config('app.audit_actor', 'kotobee-fulfillment-worker', true),
                   set_config('app.audit_reason', 'Fulfillment attempt failed in Kotobee API', true);

            UPDATE kotobee_entitlements
            SET status = 'Failed',
                last_error = @error,
                updated_at_utc = NOW()
            WHERE order_id = @order_id AND kotobee_book_id = @book_id;
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("error", error);
            cmd.Parameters.AddWithValue("order_id", orderId);
            cmd.Parameters.AddWithValue("book_id", bookId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed marking entitlement failed for Order {OrderId}, Book {BookId} in Postgres.", orderId, bookId);
        }
    }

    public async Task MarkRevokedAsync(string orderId, string bookId, CancellationToken ct = default)
    {
        string key = $"{orderId}:{bookId}";
        if (_fallbackCache.TryGetValue(key, out var cached))
        {
            _fallbackCache[key] = cached with
            {
                Status = "Revoked",
                BookAccessState = "none"
            };
        }

        if (_dataSource is null)
        {
            return;
        }

        const string sql = """
            SELECT set_config('app.audit_actor', 'kotobee-admin', true),
                   set_config('app.audit_reason', 'Administrator revoked book entitlement access', true);

            UPDATE kotobee_entitlements
            SET status = 'Revoked',
                book_access_state = 'none',
                revoked_at_utc = NOW(),
                updated_at_utc = NOW()
            WHERE order_id = @order_id AND kotobee_book_id = @book_id;
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("order_id", orderId);
            cmd.Parameters.AddWithValue("book_id", bookId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed marking entitlement revoked for Order {OrderId}, Book {BookId} in Postgres.", orderId, bookId);
        }
    }

    public async Task<IReadOnlyList<KotobeeEntitlementRecord>> GetByOrderIdAsync(string orderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return [];
        }

        if (_dataSource is null)
        {
            return _fallbackCache.Values
                .Where(r => string.Equals(r.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        const string sql = """
            SELECT id, order_id, product_sku, buyer_email, kotobee_uid, kotobee_mode,
                   kotobee_lib_id, kotobee_book_id, status, book_access_state,
                   attempt_count, last_error, expires_at_utc, activated_at_utc
            FROM kotobee_entitlements
            WHERE order_id = @order_id
            ORDER BY created_at_utc ASC;
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("order_id", orderId.Trim());
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            var results = new List<KotobeeEntitlementRecord>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                Guid id = reader.GetGuid(0);
                string ordId = reader.GetString(1);
                string sku = reader.GetString(2);
                string email = reader.GetString(3);
                string? uid = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false) ? null : reader.GetString(4);
                string mode = reader.GetString(5);
                string? libId = await reader.IsDBNullAsync(6, ct).ConfigureAwait(false) ? null : reader.GetString(6);
                string bookId = reader.GetString(7);
                string status = reader.GetString(8);
                string? access = await reader.IsDBNullAsync(9, ct).ConfigureAwait(false) ? null : reader.GetString(9);
                int attempts = reader.GetInt32(10);
                string? err = await reader.IsDBNullAsync(11, ct).ConfigureAwait(false) ? null : reader.GetString(11);
                DateTimeOffset? expires = await reader.IsDBNullAsync(12, ct).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(12, ct).ConfigureAwait(false);
                DateTimeOffset? activated = await reader.IsDBNullAsync(13, ct).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(13, ct).ConfigureAwait(false);

                results.Add(new KotobeeEntitlementRecord(
                    Id: id,
                    OrderId: ordId,
                    ProductSku: sku,
                    BuyerEmail: email,
                    KotobeeUid: uid,
                    KotobeeMode: mode,
                    KotobeeLibId: libId,
                    KotobeeBookId: bookId,
                    Status: status,
                    BookAccessState: access,
                    AttemptCount: attempts,
                    LastError: err,
                    ExpiresAtUtc: expires,
                    ActivatedAtUtc: activated
                ));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres query for Order {OrderId} entitlements failed; falling back to in-memory cache.", orderId);
            return _fallbackCache.Values
                .Where(r => string.Equals(r.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}

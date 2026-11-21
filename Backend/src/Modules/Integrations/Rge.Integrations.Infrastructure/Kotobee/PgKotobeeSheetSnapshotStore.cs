using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Rge.Integrations.Application.Ports;

namespace Rge.Integrations.Infrastructure.Kotobee;

[SuppressMessage("Performance", "CA1873:Avoid unnecessary evaluation")]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
public sealed class PgKotobeeSheetSnapshotStore : IKotobeeSheetSnapshotStore
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly ILogger<PgKotobeeSheetSnapshotStore> _logger;
    private readonly ConcurrentDictionary<Guid, KotobeeSheetCatalogSnapshot> _fallbackStore = new();

    public PgKotobeeSheetSnapshotStore(
        NpgsqlDataSource? dataSource = null,
        ILogger<PgKotobeeSheetSnapshotStore>? logger = null)
    {
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<PgKotobeeSheetSnapshotStore>.Instance;
    }

    public async Task SaveSnapshotAsync(KotobeeSheetCatalogSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_dataSource is null)
        {
            if (snapshot.IsCurrent)
            {
                foreach (var (k, v) in _fallbackStore)
                {
                    if (v.IsCurrent)
                    {
                        _fallbackStore[k] = v with { IsCurrent = false };
                    }
                }
            }
            _fallbackStore[snapshot.Id] = snapshot;
            return;
        }

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            if (snapshot.IsCurrent)
            {
                await using var resetCmd = new NpgsqlCommand(
                    "UPDATE kotobee_sheet_catalog_snapshots SET is_current = FALSE WHERE is_current = TRUE;",
                    conn,
                    tx);
                await resetCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            const string insertSql = """
                INSERT INTO kotobee_sheet_catalog_snapshots (
                    id, spreadsheet_id, sheet_range, content_sha256, active_row_count,
                    mappings_json, synced_by, synced_at_utc, is_current
                ) VALUES (
                    @id, @spreadsheet_id, @sheet_range, @content_sha256, @active_row_count,
                    @mappings_json::jsonb, @synced_by, @synced_at_utc, @is_current
                );
                """;

            await using var insertCmd = new NpgsqlCommand(insertSql, conn, tx);
            insertCmd.Parameters.AddWithValue("id", snapshot.Id);
            insertCmd.Parameters.AddWithValue("spreadsheet_id", snapshot.SpreadsheetId);
            insertCmd.Parameters.AddWithValue("sheet_range", snapshot.SheetRange);
            insertCmd.Parameters.AddWithValue("content_sha256", snapshot.ContentSha256);
            insertCmd.Parameters.AddWithValue("active_row_count", snapshot.ActiveRowCount);
            insertCmd.Parameters.AddWithValue("mappings_json", snapshot.MappingsJson);
            insertCmd.Parameters.AddWithValue("synced_by", snapshot.SyncedBy);
            insertCmd.Parameters.AddWithValue("synced_at_utc", snapshot.SyncedAtUtc);
            insertCmd.Parameters.AddWithValue("is_current", snapshot.IsCurrent);

            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed saving sheet snapshot {SnapshotId} to Postgres. Storing in fallback.", snapshot.Id);
            if (snapshot.IsCurrent)
            {
                foreach (var (k, v) in _fallbackStore)
                {
                    if (v.IsCurrent)
                    {
                        _fallbackStore[k] = v with { IsCurrent = false };
                    }
                }
            }
            _fallbackStore[snapshot.Id] = snapshot;
        }
    }

    public async Task<KotobeeSheetCatalogSnapshot?> GetLatestCurrentSnapshotAsync(CancellationToken ct = default)
    {
        if (_dataSource is null)
        {
            return _fallbackStore.Values
                .Where(s => s.IsCurrent)
                .OrderByDescending(s => s.SyncedAtUtc)
                .FirstOrDefault();
        }

        const string sql = """
            SELECT id, spreadsheet_id, sheet_range, content_sha256, active_row_count,
                   mappings_json::text, synced_by, synced_at_utc, is_current
            FROM kotobee_sheet_catalog_snapshots
            WHERE is_current = TRUE
            ORDER BY synced_at_utc DESC
            LIMIT 1;
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return new KotobeeSheetCatalogSnapshot(
                    Id: reader.GetGuid(0),
                    SpreadsheetId: reader.GetString(1),
                    SheetRange: reader.GetString(2),
                    ContentSha256: reader.GetString(3),
                    ActiveRowCount: reader.GetInt32(4),
                    MappingsJson: reader.GetString(5),
                    SyncedBy: reader.GetString(6),
                    SyncedAtUtc: await reader.GetFieldValueAsync<DateTimeOffset>(7, ct).ConfigureAwait(false),
                    IsCurrent: reader.GetBoolean(8)
                );
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading latest current snapshot from Postgres. Falling back.");
            return _fallbackStore.Values
                .Where(s => s.IsCurrent)
                .OrderByDescending(s => s.SyncedAtUtc)
                .FirstOrDefault();
        }
    }

    public async Task<IReadOnlyList<KotobeeSheetCatalogSnapshot>> GetAllSnapshotsAsync(int limit = 50, CancellationToken ct = default)
    {
        if (_dataSource is null)
        {
            return _fallbackStore.Values
                .OrderByDescending(s => s.SyncedAtUtc)
                .Take(limit)
                .ToList();
        }

        const string sql = """
            SELECT id, spreadsheet_id, sheet_range, content_sha256, active_row_count,
                   mappings_json::text, synced_by, synced_at_utc, is_current
            FROM kotobee_sheet_catalog_snapshots
            ORDER BY synced_at_utc DESC
            LIMIT @limit;
            """;

        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            cmd.Parameters.AddWithValue("limit", limit);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            var list = new List<KotobeeSheetCatalogSnapshot>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new KotobeeSheetCatalogSnapshot(
                    Id: reader.GetGuid(0),
                    SpreadsheetId: reader.GetString(1),
                    SheetRange: reader.GetString(2),
                    ContentSha256: reader.GetString(3),
                    ActiveRowCount: reader.GetInt32(4),
                    MappingsJson: reader.GetString(5),
                    SyncedBy: reader.GetString(6),
                    SyncedAtUtc: await reader.GetFieldValueAsync<DateTimeOffset>(7, ct).ConfigureAwait(false),
                    IsCurrent: reader.GetBoolean(8)
                ));
            }

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading snapshots from Postgres. Falling back.");
            return _fallbackStore.Values
                .OrderByDescending(s => s.SyncedAtUtc)
                .Take(limit)
                .ToList();
        }
    }
}

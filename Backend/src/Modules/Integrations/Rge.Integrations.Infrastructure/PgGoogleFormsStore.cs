using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Infrastructure;

public sealed class PgGoogleFormsStore : IGoogleFormsStore, IGoogleFormsSubmissionStore, IDisposable
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static int _schemaInitialized;

    private readonly NpgsqlDataSource? _dataSource;
    private readonly ILogger<PgGoogleFormsStore> _logger;
    private readonly ConcurrentDictionary<string, GoogleFormSubmission> _fallbackCache = new(StringComparer.OrdinalIgnoreCase);

    private const string EnsureTableSql = """
        CREATE TABLE IF NOT EXISTS public.google_form_submissions (
            response_id       character varying(128) NOT NULL,
            order_id          character varying(36)  NOT NULL,
            order_number      character varying(64)  NOT NULL,
            customer_name     character varying(255) NOT NULL,
            customer_email    character varying(255) NOT NULL,
            description       text                   NOT NULL,
            amount_minor      bigint                 NOT NULL,
            currency          character varying(3)   NOT NULL DEFAULT 'PHP',
            status            character varying(32)  NOT NULL DEFAULT 'pending',
            email_dispatched  boolean                NOT NULL DEFAULT true,
            raw_payload_json  text                   NULL,
            created_at        timestamp with time zone NOT NULL DEFAULT now(),
            updated_at        timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT google_form_submissions_pkey PRIMARY KEY (response_id)
        );

        ALTER TABLE public.google_form_submissions
            DROP CONSTRAINT IF EXISTS google_form_submissions_order_id_fkey;

        CREATE INDEX IF NOT EXISTS ix_google_form_submissions_order_id
            ON public.google_form_submissions USING btree (order_id);

        CREATE INDEX IF NOT EXISTS ix_google_form_submissions_created_at_desc
            ON public.google_form_submissions USING btree (created_at DESC);
        """;

    public PgGoogleFormsStore(
        ILogger<PgGoogleFormsStore>? logger = null,
        NpgsqlDataSource? dataSource = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PgGoogleFormsStore>.Instance;
        _dataSource = dataSource;
    }

    public async Task<(GoogleFormSubmission Submission, bool CreatedNew)> UpsertSubmissionAsync(
        GoogleFormSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var createdNewInMemory = !_fallbackCache.ContainsKey(submission.ResponseId);
        _fallbackCache[submission.ResponseId] = submission;

        if (_dataSource is null)
        {
            return (submission, createdNewInMemory);
        }

        const string sql = """
            INSERT INTO public.google_form_submissions (
                response_id,
                order_id,
                order_number,
                customer_name,
                customer_email,
                description,
                amount_minor,
                currency,
                status,
                email_dispatched,
                created_at,
                updated_at
            )
            VALUES (
                @ResponseId,
                @OrderId,
                @OrderNumber,
                @CustomerName,
                @CustomerEmail,
                @Description,
                @AmountMinor,
                'PHP',
                @Status,
                true,
                @CreatedAt,
                @UpdatedAt
            )
            ON CONFLICT (response_id) DO UPDATE
            SET
                order_id = EXCLUDED.order_id,
                order_number = EXCLUDED.order_number,
                customer_name = COALESCE(NULLIF(EXCLUDED.customer_name, ''), public.google_form_submissions.customer_name),
                customer_email = COALESCE(NULLIF(EXCLUDED.customer_email, ''), public.google_form_submissions.customer_email),
                description = COALESCE(NULLIF(EXCLUDED.description, ''), public.google_form_submissions.description),
                amount_minor = CASE
                    WHEN EXCLUDED.amount_minor > 0 THEN EXCLUDED.amount_minor
                    ELSE public.google_form_submissions.amount_minor
                END,
                -- Never downgrade a terminal status ('paid', 'cancelled', 'expired') back to 'pending'
                status = CASE
                    WHEN LOWER(public.google_form_submissions.status) IN ('paid', 'succeeded', 'completed') THEN 'paid'
                    WHEN LOWER(EXCLUDED.status) IN ('paid', 'succeeded', 'completed') THEN 'paid'
                    WHEN LOWER(EXCLUDED.status) IN ('cancelled', 'canceled', 'expired', 'failed') THEN LOWER(EXCLUDED.status)
                    WHEN LOWER(public.google_form_submissions.status) IN ('cancelled', 'canceled', 'expired', 'failed') THEN LOWER(public.google_form_submissions.status)
                    ELSE COALESCE(NULLIF(LOWER(EXCLUDED.status), ''), 'pending')
                END,
                updated_at = EXCLUDED.updated_at;
            """;

        try
        {
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                sql,
                new
                {
                    submission.ResponseId,
                    OrderId = submission.OrderId ?? $"ord_{submission.ResponseId}",
                    OrderNumber = submission.OrderNumber ?? $"ORD-{submission.CreatedAt:yyyyMM}-{submission.ResponseId[..Math.Min(4, submission.ResponseId.Length)]}",
                    submission.CustomerName,
                    submission.CustomerEmail,
                    submission.Description,
                    AmountMinor = submission.AmountCentavos,
                    Status = NormalizeStatus(submission.Status),
                    CreatedAt = submission.CreatedAt.UtcDateTime,
                    UpdatedAt = DateTime.UtcNow
                },
                cancellationToken: cancellationToken);

            var affected = await connection.ExecuteAsync(command).ConfigureAwait(false);
            return (submission, affected > 0 && createdNewInMemory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[GOOGLE-FORMS-STORE] Database upsert fallback to memory for response_id={ResponseId}",
                submission.ResponseId);
            return (submission, createdNewInMemory);
        }
    }

    public async Task<GoogleFormsStatusSnapshot> GetStatusSnapshotAsync(
        int maxRecentLimit = 200,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(maxRecentLimit, 1, 500);

        if (_dataSource is null)
        {
            var memItems = _fallbackCache.Values.OrderByDescending(x => x.CreatedAt).ToList();
            int memTotal = memItems.Count;
            int memPaid = memItems.Count(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase));
            int memCancelled = memItems.Count(x => string.Equals(x.Status, "cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "expired", StringComparison.OrdinalIgnoreCase));
            int memPending = Math.Max(0, memTotal - memPaid - memCancelled);
            var memDtos = memItems.Take(safeLimit).Select(x => new GoogleFormSubmissionDto(
                OrderId: x.OrderId ?? $"ord_{x.ResponseId}",
                OrderNumber: x.OrderNumber ?? $"ORD-{x.CreatedAt:yyyyMM}-{x.ResponseId[..Math.Min(4, x.ResponseId.Length)]}",
                ResponseId: x.ResponseId,
                CustomerName: x.CustomerName,
                CustomerEmail: x.CustomerEmail,
                Description: x.Description,
                AmountCentavos: x.AmountCentavos,
                Status: NormalizeStatus(x.Status),
                CreatedAt: x.CreatedAt)).ToList();

            return new GoogleFormsStatusSnapshot(
                TotalSubmissions: memTotal,
                SyncedCount: memTotal,
                IngestedOrdersCount: memTotal,
                PaidOrdersCount: memPaid,
                PendingOrdersCount: memPending,
                CancelledOrdersCount: memCancelled,
                LastSyncedAt: memItems.Count > 0 ? memItems[0].CreatedAt : null,
                RecentSubmissions: memDtos);
        }

        // 1. Self-heal any google_form_submissions rows whose underlying order or latest payment_attempt has changed status
        const string reconcileSql = """
            WITH live_order_state AS (
                SELECT
                    o.id AS order_id,
                    o.order_number,
                    CASE
                        WHEN LOWER(o.status) IN ('paid', 'succeeded', 'completed')
                          OR LOWER(COALESCE(pa.status, '')) IN ('paid', 'succeeded', 'completed')
                            THEN 'paid'
                        WHEN LOWER(o.status) IN ('cancelled', 'canceled')
                          OR LOWER(COALESCE(pa.status, '')) IN ('cancelled', 'canceled')
                            THEN 'cancelled'
                        WHEN LOWER(o.status) IN ('expired', 'failed', 'abandoned')
                          OR LOWER(COALESCE(pa.status, '')) IN ('expired', 'failed', 'abandoned')
                            THEN 'expired'
                        ELSE 'pending'
                    END AS live_status
                FROM public.orders o
                LEFT JOIN LATERAL (
                    SELECT p.status
                    FROM public.payment_attempts p
                    WHERE p.order_id = o.id
                    ORDER BY p.created_at DESC
                    LIMIT 1
                ) pa ON TRUE
                WHERE o.id LIKE 'ord_gf_%' OR o.reference_code LIKE 'gf_%' OR LOWER(o.category) = 'google_forms'
            )
            UPDATE public.google_form_submissions gfs
            SET
                status = los.live_status,
                updated_at = NOW()
            FROM live_order_state los
            WHERE (gfs.order_id = los.order_id OR gfs.order_number = los.order_number)
              AND LOWER(COALESCE(gfs.status, '')) IS DISTINCT FROM los.live_status;
            """;

        // 2. Full-table aggregate counts across ALL Google Forms orders (no LIMIT 10 truncation)
        const string countsSql = """
            WITH unified_gf_orders AS (
                SELECT
                    o.id AS order_id,
                    o.created_at,
                    CASE
                        WHEN LOWER(o.status) IN ('paid', 'succeeded', 'completed')
                          OR LOWER(COALESCE(pa.status, '')) IN ('paid', 'succeeded', 'completed')
                            THEN 'paid'
                        WHEN LOWER(o.status) IN ('cancelled', 'canceled', 'expired', 'failed', 'abandoned')
                          OR LOWER(COALESCE(pa.status, '')) IN ('cancelled', 'canceled', 'expired', 'failed', 'abandoned')
                            THEN 'cancelled'
                        ELSE 'pending'
                    END AS effective_status
                FROM public.orders o
                LEFT JOIN LATERAL (
                    SELECT p.status
                    FROM public.payment_attempts p
                    WHERE p.order_id = o.id
                    ORDER BY p.created_at DESC
                    LIMIT 1
                ) pa ON TRUE
                WHERE o.id LIKE 'ord_gf_%' OR o.reference_code LIKE 'gf_%' OR LOWER(o.category) = 'google_forms'

                UNION

                SELECT
                    gfs.order_id,
                    gfs.created_at,
                    CASE
                        WHEN LOWER(gfs.status) IN ('paid', 'succeeded', 'completed') THEN 'paid'
                        WHEN LOWER(gfs.status) IN ('cancelled', 'canceled', 'expired', 'failed', 'abandoned') THEN 'cancelled'
                        ELSE 'pending'
                    END AS effective_status
                FROM public.google_form_submissions gfs
                WHERE NOT EXISTS (
                    SELECT 1 FROM public.orders o WHERE o.id = gfs.order_id OR o.order_number = gfs.order_number
                )
            )
            SELECT
                COUNT(*)::INT AS TotalSubmissions,
                COUNT(*) FILTER (WHERE effective_status = 'paid')::INT AS PaidOrdersCount,
                COUNT(*) FILTER (WHERE effective_status = 'pending')::INT AS PendingOrdersCount,
                COUNT(*) FILTER (WHERE effective_status = 'cancelled')::INT AS CancelledOrdersCount,
                MAX(created_at) AS LastSyncedAt
            FROM unified_gf_orders;
            """;

        // 3. Reconciled recent submissions list (up to safeLimit items instead of 10)
        const string recentSql = """
            WITH unified_submissions AS (
                SELECT
                    o.id AS OrderId,
                    o.order_number AS OrderNumber,
                    COALESCE(NULLIF(gfs.response_id, ''), o.reference_code, o.id) AS ResponseId,
                    COALESCE(
                        NULLIF(gfs.customer_name, ''),
                        CASE WHEN o.metadata_json LIKE '{%}' THEN (o.metadata_json::jsonb ->> 'customer_name') ELSE NULL END,
                        'Customer'
                    ) AS CustomerName,
                    COALESCE(
                        NULLIF(gfs.customer_email, ''),
                        CASE WHEN o.metadata_json LIKE '{%}' THEN (o.metadata_json::jsonb ->> 'customer_email') ELSE NULL END,
                        ''
                    ) AS CustomerEmail,
                    COALESCE(NULLIF(gfs.description, ''), o.title, o.order_number) AS Description,
                    o.amount_minor AS AmountCentavos,
                    CASE
                        WHEN LOWER(o.status) IN ('paid', 'succeeded', 'completed')
                          OR LOWER(COALESCE(pa.status, '')) IN ('paid', 'succeeded', 'completed')
                            THEN 'paid'
                        WHEN LOWER(o.status) IN ('cancelled', 'canceled')
                          OR LOWER(COALESCE(pa.status, '')) IN ('cancelled', 'canceled')
                            THEN 'cancelled'
                        WHEN LOWER(o.status) IN ('expired', 'failed', 'abandoned')
                          OR LOWER(COALESCE(pa.status, '')) IN ('expired', 'failed', 'abandoned')
                            THEN 'expired'
                        ELSE 'pending'
                    END AS Status,
                    o.created_at AS CreatedAt
                FROM public.orders o
                LEFT JOIN public.google_form_submissions gfs ON gfs.order_id = o.id OR gfs.order_number = o.order_number
                LEFT JOIN LATERAL (
                    SELECT p.status
                    FROM public.payment_attempts p
                    WHERE p.order_id = o.id
                    ORDER BY p.created_at DESC
                    LIMIT 1
                ) pa ON TRUE
                WHERE o.id LIKE 'ord_gf_%' OR o.reference_code LIKE 'gf_%' OR LOWER(o.category) = 'google_forms'

                UNION ALL

                SELECT
                    gfs.order_id AS OrderId,
                    gfs.order_number AS OrderNumber,
                    gfs.response_id AS ResponseId,
                    gfs.customer_name AS CustomerName,
                    gfs.customer_email AS CustomerEmail,
                    gfs.description AS Description,
                    gfs.amount_minor AS AmountCentavos,
                    LOWER(COALESCE(gfs.status, 'pending')) AS Status,
                    gfs.created_at AS CreatedAt
                FROM public.google_form_submissions gfs
                WHERE NOT EXISTS (
                    SELECT 1 FROM public.orders o WHERE o.id = gfs.order_id OR o.order_number = gfs.order_number
                )
            )
            SELECT
                OrderId,
                OrderNumber,
                ResponseId,
                CustomerName,
                CustomerEmail,
                Description,
                AmountCentavos,
                Status,
                CreatedAt
            FROM unified_submissions
            ORDER BY CreatedAt DESC
            LIMIT @Limit;
            """;

        try
        {
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(reconcileSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                _logger.LogDebug(ex, "google_form_submissions table not yet initialized; falling back to orders table.");
            }

            var counts = await connection.QuerySingleOrDefaultAsync<AggregateCountRow>(
                new CommandDefinition(countsSql, cancellationToken: cancellationToken)).ConfigureAwait(false)
                ?? new AggregateCountRow(0, 0, 0, 0, null);

            var recentRows = (await connection.QueryAsync<RecentSubmissionRow>(
                new CommandDefinition(
                    recentSql,
                    new { Limit = safeLimit },
                    cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

            var recentDtos = recentRows
                .Select(r => new GoogleFormSubmissionDto(
                    OrderId: r.OrderId,
                    OrderNumber: r.OrderNumber,
                    ResponseId: r.ResponseId,
                    CustomerName: r.CustomerName,
                    CustomerEmail: r.CustomerEmail,
                    Description: r.Description,
                    AmountCentavos: r.AmountCentavos,
                    Status: NormalizeStatus(r.Status),
                    CreatedAt: r.CreatedAt))
                .ToList();

            return new GoogleFormsStatusSnapshot(
                TotalSubmissions: counts.TotalSubmissions,
                SyncedCount: counts.TotalSubmissions,
                IngestedOrdersCount: counts.TotalSubmissions,
                PaidOrdersCount: counts.PaidOrdersCount,
                PendingOrdersCount: counts.PendingOrdersCount,
                CancelledOrdersCount: counts.CancelledOrdersCount,
                LastSyncedAt: counts.LastSyncedAt,
                RecentSubmissions: recentDtos);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GOOGLE-FORMS-STORE] GetStatusSnapshotAsync failed; falling back to in-memory cache.");
            var memItems = _fallbackCache.Values.OrderByDescending(x => x.CreatedAt).ToList();
            int memTotal = memItems.Count;
            int memPaid = memItems.Count(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase));
            int memCancelled = memItems.Count(x => string.Equals(x.Status, "cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "expired", StringComparison.OrdinalIgnoreCase));
            int memPending = Math.Max(0, memTotal - memPaid - memCancelled);
            var memDtos = memItems.Take(safeLimit).Select(x => new GoogleFormSubmissionDto(
                OrderId: x.OrderId ?? $"ord_{x.ResponseId}",
                OrderNumber: x.OrderNumber ?? $"ORD-{x.CreatedAt:yyyyMM}-{x.ResponseId[..Math.Min(4, x.ResponseId.Length)]}",
                ResponseId: x.ResponseId,
                CustomerName: x.CustomerName,
                CustomerEmail: x.CustomerEmail,
                Description: x.Description,
                AmountCentavos: x.AmountCentavos,
                Status: NormalizeStatus(x.Status),
                CreatedAt: x.CreatedAt)).ToList();

            return new GoogleFormsStatusSnapshot(
                TotalSubmissions: memTotal,
                SyncedCount: memTotal,
                IngestedOrdersCount: memTotal,
                PaidOrdersCount: memPaid,
                PendingOrdersCount: memPending,
                CancelledOrdersCount: memCancelled,
                LastSyncedAt: memItems.Count > 0 ? memItems[0].CreatedAt : null,
                RecentSubmissions: memDtos);
        }
    }

    public async Task<IReadOnlyList<GoogleFormSubmission>> GetRecentSubmissionsAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetStatusSnapshotAsync(limit, cancellationToken).ConfigureAwait(false);
        return snapshot.RecentSubmissions
            .Select(dto => new GoogleFormSubmission(
                ResponseId: dto.ResponseId,
                OrderId: dto.OrderId,
                OrderNumber: dto.OrderNumber,
                CustomerName: dto.CustomerName,
                CustomerEmail: dto.CustomerEmail,
                Description: dto.Description,
                AmountCentavos: dto.AmountCentavos,
                Status: dto.Status,
                CreatedAt: dto.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<GoogleFormSubmission>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetRecentSubmissionsAsync(500, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GoogleFormsStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetStatusSnapshotAsync(200, cancellationToken).ConfigureAwait(false);

        return new GoogleFormsStatusResponse(
            Enabled: true,
            TotalSubmissions: snapshot.TotalSubmissions,
            SyncedCount: snapshot.SyncedCount,
            LastSyncedAt: snapshot.LastSyncedAt,
            RecentSubmissions: snapshot.RecentSubmissions,
            SecretConfigured: true,
            Status: "active",
            IngestedOrdersCount: snapshot.IngestedOrdersCount,
            PaidOrdersCount: snapshot.PaidOrdersCount,
            PendingOrdersCount: snapshot.PendingOrdersCount);
    }

    public Task SaveSubmissionAsync(GoogleFormSubmission submission, CancellationToken cancellationToken = default) =>
        UpsertSubmissionAsync(submission, cancellationToken);

    public Task UpsertAsync(GoogleFormSubmission submission, CancellationToken cancellationToken = default) =>
        UpsertSubmissionAsync(submission, cancellationToken);

    public async Task<GoogleFormSubmission?> GetByResponseIdAsync(string responseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(responseId)) return null;

        if (_fallbackCache.TryGetValue(responseId.Trim(), out var cached))
        {
            return cached;
        }

        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(x => string.Equals(x.ResponseId, responseId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetStatusSnapshotAsync(1, cancellationToken).ConfigureAwait(false);
        return snapshot.TotalSubmissions;
    }

    private static string NormalizeStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return "pending";
        }

        return rawStatus.Trim().ToLowerInvariant() switch
        {
            "paid" or "succeeded" or "completed" or "settled" => "paid",
            "cancelled" or "canceled" => "cancelled",
            "expired" or "failed" or "abandoned" => "expired",
            _ => "pending"
        };
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _schemaInitialized) == 1 || _dataSource is null)
        {
            return;
        }

        await SchemaLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaInitialized == 1)
            {
                return;
            }

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = EnsureTableSql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _schemaInitialized, 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GOOGLE-FORMS-STORE] Schema initialization warning; proceeding with fallback resilience.");
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public void Dispose()
    {
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Dapper")]
    private sealed record AggregateCountRow(
        int TotalSubmissions,
        int PaidOrdersCount,
        int PendingOrdersCount,
        int CancelledOrdersCount,
        DateTimeOffset? LastSyncedAt);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Dapper")]
    private sealed record RecentSubmissionRow(
        string OrderId,
        string OrderNumber,
        string ResponseId,
        string CustomerName,
        string CustomerEmail,
        string Description,
        long AmountCentavos,
        string Status,
        DateTimeOffset CreatedAt);
}

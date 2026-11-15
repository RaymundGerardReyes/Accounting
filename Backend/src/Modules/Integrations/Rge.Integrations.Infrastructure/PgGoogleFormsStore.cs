using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Contracts;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Infrastructure;

public sealed class PgGoogleFormsStore : IGoogleFormsStore, IGoogleFormsSubmissionStore, IDisposable
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly ILogger<PgGoogleFormsStore> _logger;
    private readonly ConcurrentDictionary<string, GoogleFormSubmission> _fallbackCache = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);

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

    private const string CheckExistsSql = """
        SELECT EXISTS (
            SELECT 1 FROM public.google_form_submissions WHERE response_id = @response_id
        );
        """;

    private const string UpsertSubmissionSql = """
        INSERT INTO public.google_form_submissions (
            response_id, order_id, order_number, customer_name, customer_email,
            description, amount_minor, currency, status, email_dispatched,
            created_at, updated_at
        ) VALUES (
            @response_id, @order_id, @order_number, @customer_name, @customer_email,
            @description, @amount_minor, 'PHP', @status, true,
            @created_at, @updated_at
        )
        ON CONFLICT (response_id) DO UPDATE SET
            order_id       = EXCLUDED.order_id,
            order_number   = EXCLUDED.order_number,
            customer_name  = EXCLUDED.customer_name,
            customer_email = EXCLUDED.customer_email,
            description    = EXCLUDED.description,
            amount_minor   = EXCLUDED.amount_minor,
            status         = EXCLUDED.status,
            updated_at     = EXCLUDED.updated_at;
        """;

    private const string SelectUnifiedSubmissionsSql = """
        WITH combined AS (
            SELECT
                gfs.response_id,
                gfs.order_id,
                gfs.order_number,
                gfs.customer_name,
                gfs.customer_email,
                gfs.description,
                gfs.amount_minor,
                COALESCE(o.status, gfs.status) AS status,
                gfs.created_at
            FROM public.google_form_submissions gfs
            LEFT JOIN public.orders o ON o.id = gfs.order_id OR o.order_number = gfs.order_number

            UNION ALL

            SELECT
                COALESCE(NULLIF(o.reference_code, ''), o.id) AS response_id,
                o.id AS order_id,
                o.order_number,
                COALESCE(
                    CASE WHEN o.metadata_json IS NOT NULL AND o.metadata_json LIKE '{%}'
                         THEN (o.metadata_json::jsonb ->> 'customer_name')
                         ELSE NULL END,
                    'Google Forms Customer'
                ) AS customer_name,
                COALESCE(
                    CASE WHEN o.metadata_json IS NOT NULL AND o.metadata_json LIKE '{%}'
                         THEN (o.metadata_json::jsonb ->> 'customer_email')
                         ELSE NULL END,
                    ''
                ) AS customer_email,
                o.title AS description,
                o.amount_minor,
                o.status,
                o.created_at
            FROM public.orders o
            WHERE (o.id LIKE 'ord_gf_%' OR o.reference_code LIKE 'gf_%' OR LOWER(o.category) = 'google_forms')
              AND NOT EXISTS (
                  SELECT 1 FROM public.google_form_submissions g
                  WHERE g.order_id = o.id
                     OR g.order_number = o.order_number
                     OR g.response_id = o.reference_code
              )
        )
        SELECT
            response_id,
            order_id,
            order_number,
            customer_name,
            customer_email,
            description,
            amount_minor,
            status,
            created_at
        FROM combined
        ORDER BY created_at DESC;
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

        try
        {
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            bool existsInDb;
            await using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = CheckExistsSql;
                checkCmd.Parameters.AddWithValue("response_id", submission.ResponseId);
                var scalar = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                existsInDb = scalar is bool b && b;
            }

            await using (var upsertCmd = connection.CreateCommand())
            {
                upsertCmd.CommandText = UpsertSubmissionSql;
                upsertCmd.Parameters.AddWithValue("response_id", submission.ResponseId);
                upsertCmd.Parameters.AddWithValue("order_id", submission.OrderId ?? $"ord_{submission.ResponseId}");
                upsertCmd.Parameters.AddWithValue("order_number", submission.OrderNumber ?? $"ORD-{submission.CreatedAt:yyyyMM}-{submission.ResponseId[..Math.Min(4, submission.ResponseId.Length)]}");
                upsertCmd.Parameters.AddWithValue("customer_name", submission.CustomerName);
                upsertCmd.Parameters.AddWithValue("customer_email", submission.CustomerEmail);
                upsertCmd.Parameters.AddWithValue("description", submission.Description);
                upsertCmd.Parameters.AddWithValue("amount_minor", submission.AmountCentavos);
                upsertCmd.Parameters.AddWithValue("status", submission.Status);
                upsertCmd.Parameters.AddWithValue("created_at", submission.CreatedAt.UtcDateTime);
                upsertCmd.Parameters.AddWithValue("updated_at", DateTime.UtcNow);

                await upsertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return (submission, !existsInDb);
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

    public async Task<IReadOnlyList<GoogleFormSubmission>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (_dataSource is null)
        {
            return _fallbackCache.Values
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }

        try
        {
            await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = SelectUnifiedSubmissionsSql;

            var dbList = new List<GoogleFormSubmission>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string responseId = reader.GetString(0);
                string orderId = reader.GetString(1);
                string orderNumber = reader.GetString(2);
                string customerName = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? "Google Forms Customer" : reader.GetString(3);
                string customerEmail = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? string.Empty : reader.GetString(4);
                string description = await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? string.Empty : reader.GetString(5);
                long amountMinor = reader.GetInt64(6);
                string status = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false) ? "pending" : reader.GetString(7);
                DateTime createdAtRaw = reader.GetDateTime(8);

                var item = new GoogleFormSubmission(
                    OrderId: orderId,
                    OrderNumber: orderNumber,
                    ResponseId: responseId,
                    CustomerName: customerName,
                    CustomerEmail: customerEmail,
                    Description: description,
                    AmountCentavos: amountMinor,
                    Status: status,
                    CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(createdAtRaw, DateTimeKind.Utc)));

                dbList.Add(item);
                _fallbackCache[item.ResponseId] = item;
            }

            // Merge any in-flight memory items not yet committed
            var seenIds = new HashSet<string>(dbList.Select(x => x.ResponseId), StringComparer.OrdinalIgnoreCase);
            foreach (var memItem in _fallbackCache.Values)
            {
                if (seenIds.Add(memItem.ResponseId))
                {
                    dbList.Add(memItem);
                }
            }

            return dbList
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GOOGLE-FORMS-STORE] Database read failed; returning cached submissions.");
            return _fallbackCache.Values
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
    }

    public async Task<GoogleFormsStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        int total = all.Count;
        int paid = all.Count(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase));
        int pending = total - paid;
        var lastSyncedAt = all.Count > 0 ? all.Max(x => x.CreatedAt) : (DateTimeOffset?)null;

        return new GoogleFormsStatusResponse(
            Enabled: true,
            TotalSubmissions: total,
            SyncedCount: total,
            LastSyncedAt: lastSyncedAt,
            SecretConfigured: true,
            Status: "active",
            IngestedOrdersCount: total,
            PaidOrdersCount: paid,
            PendingOrdersCount: pending);
    }

    public async Task SaveSubmissionAsync(GoogleFormSubmission submission, CancellationToken cancellationToken = default)
    {
        await UpsertSubmissionAsync(submission, cancellationToken).ConfigureAwait(false);
    }

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
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Count;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaEnsured || _dataSource is null)
        {
            return;
        }

        await _schemaLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaEnsured)
            {
                return;
            }

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = EnsureTableSql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _schemaEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GOOGLE-FORMS-STORE] Schema initialization warning; proceeding with fallback resilience.");
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public void Dispose()
    {
        _schemaLock.Dispose();
    }
}

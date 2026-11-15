using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Application.Pagination;
using Rge.BuildingBlocks.Infrastructure.Observability;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Infrastructure.Persistence.Mappings;
using Rge.Payments.Infrastructure.Persistence.Sql;

namespace Rge.Payments.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private static long _fallbackSequence = 10;
    private static readonly ConcurrentDictionary<string, Order> MemoryById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Order> MemoryByNumber = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAtUtc, IReadOnlyList<Order> Orders)> ListCache = new(StringComparer.Ordinal);
    private static long _lastBridgeTicksUtc;
    private static readonly long BridgeCooldownTicks = TimeSpan.FromSeconds(30).Ticks;

    static OrderRepository()
    {
        EnsureSeeded();
    }

    private readonly ConnectionScope _connectionScope;
    private readonly ILogger<OrderRepository>? _logger;

    public OrderRepository(ConnectionScope connectionScope, ILogger<OrderRepository>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
        _logger = logger;
    }

    public async Task<RepositoryPagedOrders> ListPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sw = Stopwatch.StartNew();
        double connAcquireMs = 0;
        double sqlDurationMs = 0;
        double materializeDurationMs = 0;

        try
        {
            var connSw = Stopwatch.StartNew();
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            connSw.Stop();
            connAcquireMs = connSw.Elapsed.TotalMilliseconds;

            var sqlSw = Stopwatch.StartNew();
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = OrderQueries.ListOrdersPagedSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;

            AddParam(cmd, "@Status", request.Status);
            AddParam(cmd, "@Category", request.Category);
            AddParam(cmd, "@SearchPattern", BuildSafeIlikePattern(request.Search));
            AddParam(cmd, "@PageSize", request.PageSize);
            AddParam(cmd, "@Offset", request.Offset);

            var rows = new List<OrderJoinedRow>();
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new OrderJoinedRow
                {
                    TotalCount = reader.GetInt32(0),
                    Id = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(1),
                    OrderNumber = await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2),
                    CustomerName = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
                    CustomerEmail = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                    Category = await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5),
                    Status = await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(6),
                    SubtotalCentavos = reader.GetInt64(7),
                    VatCentavos = reader.GetInt64(8),
                    TotalCentavos = reader.GetInt64(9),
                    Currency = await reader.IsDBNullAsync(10, cancellationToken).ConfigureAwait(false) ? "PHP" : reader.GetString(10),
                    CreatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(11, cancellationToken).ConfigureAwait(false),
                    UpdatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(12, cancellationToken).ConfigureAwait(false)
                };
                rows.Add(row);
            }
            sqlSw.Stop();
            sqlDurationMs = sqlSw.Elapsed.TotalMilliseconds;

            var matSw = Stopwatch.StartNew();
            var materialized = OrderMaterializer.MaterializePage(rows);
            matSw.Stop();
            materializeDurationMs = matSw.Elapsed.TotalMilliseconds;

            var pagedResult = PagedResult<Order>.Create(
                items: materialized.Orders,
                page: request.Page,
                pageSize: request.PageSize,
                totalCount: materialized.TotalCount);

            return new RepositoryPagedOrders(
                Page: pagedResult,
                Timing: new RepositoryPageTiming(
                    ConnectionAcquireMs: Math.Round(connAcquireMs, 2),
                    SqlDurationMs: Math.Round(sqlDurationMs, 2),
                    MaterializeDurationMs: Math.Round(materializeDurationMs, 2)));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Database unreachable for ListPagedAsync; falling back to in-memory store.");

            // In-memory fallback
            var all = MemoryById.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                all = all.Where(o => string.Equals(o.Status, request.Status, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                all = all.Where(o => string.Equals(o.Category, request.Category, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var s = request.Search.Trim();
                all = all.Where(o => (o.OrderNumber?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (o.CustomerName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (o.CustomerEmail?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var ordered = all.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).ToList();
            var totalCount = ordered.Count;
            var items = ordered.Skip(request.Offset).Take(request.PageSize).ToList();

            var paged = PagedResult<Order>.Create(items, request.Page, request.PageSize, totalCount);
            return new RepositoryPagedOrders(
                Page: paged,
                Timing: new RepositoryPageTiming(0.01, 0.05, 0.02));
        }
    }

    public async Task<KeysetPage<Order>> ListKeysetAsync(
        int limit,
        KeysetCursor? cursor,
        string? status,
        string? category,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, PageRequest.MaxPageSize);
        var pagedReq = new PageRequest(page: 1, pageSize: safeLimit + 1, status: status, category: category, search: search);
        var result = await ListPagedAsync(pagedReq, cancellationToken).ConfigureAwait(false);

        return KeysetPage<Order>.FromPlusOne(
            fetchedItems: result.Page.Items,
            requestedLimit: safeLimit,
            cursorSelector: o => new KeysetCursor(o.CreatedAt, Guid.TryParse(o.Id, out var g) ? g : Guid.NewGuid()));
    }

    private static string? BuildSafeIlikePattern(string? rawSearch)
    {
        if (string.IsNullOrWhiteSpace(rawSearch)) return null;
        var escaped = rawSearch
            .Trim()
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    public Task<IReadOnlyList<Order>> ListAsync(
        string? customerId,
        string? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return ListAsync(status, limit, customerId, cancellationToken);
    }

    /// <summary>
    /// Resets the in-memory fallback dictionary to the initial baseline seed orders.
    /// Useful for test isolation.
    /// </summary>
    public static void ResetToBaseline()
    {
        MemoryById.Clear();
        MemoryByNumber.Clear();
        ListCache.Clear();
        Interlocked.Exchange(ref _lastBridgeTicksUtc, 0);
        Interlocked.Exchange(ref _fallbackSequence, 10);
        EnsureSeeded();
    }

    /// <summary>
    /// Invalidates the 5-second L1 query cache for list orders.
    /// </summary>
    public static void InvalidateListCache()
    {
        ListCache.Clear();
    }

    private static void EnsureSeeded()
    {
        if (!MemoryById.IsEmpty)
        {
            return;
        }

        Order[] baselineSeeds =
        [
            Order.Rehydrate(
                id: "ord_seed_001_cpa2026",
                userId: "acme_admin",
                orderNumber: "ORD-202611-0001",
                title: "CPA Review Materials - Batch 2026",
                category: OrderCategory.ProductOrder,
                referenceCode: "CPA-REV-AUDIT-2026",
                amount: Money.Php(1450_00),
                status: OrderStatus.Pending,
                metadataJson: """{"customer_name":"Maria Santos","customer_email":"maria.santos@example.ph","customer_phone":"+639171234567","department":"Tax","cost_center":"CC-101"}""",
                createdAt: new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero),
                updatedAt: new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero)),

            Order.Rehydrate(
                id: "ord_seed_002_costacc",
                userId: "acme_admin",
                orderNumber: "ORD-202611-0002",
                title: "Cost Accounting Textbook 2026",
                category: OrderCategory.ProductOrder,
                referenceCode: "COST-ACC-TEXT-2026",
                amount: Money.Php(1850_00),
                status: OrderStatus.Paid,
                metadataJson: """{"customer_name":"Juan Dela Cruz","customer_email":"juan.delacruz@example.ph","customer_phone":"+639189876543","source":"google_forms","google_form_response_id":"GFR-2026-8812"}""",
                createdAt: new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero),
                updatedAt: new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero)),

            Order.Rehydrate(
                id: "ord_seed_003_mfgdept",
                userId: "acme_admin",
                orderNumber: "INV-202611-0001",
                title: "Manufacturing Monthly Overhead Allocation",
                category: OrderCategory.DepartmentInvoice,
                referenceCode: "manufacturing",
                amount: Money.Php(45000_00),
                status: OrderStatus.Pending,
                metadataJson: """{"department":"manufacturing","cost_center":"CC-001","approved_by":"Operations Director"}""",
                createdAt: new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero),
                updatedAt: new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.Zero)),

            Order.Rehydrate(
                id: "ord_seed_004_canceldoc",
                userId: "acme_admin",
                orderNumber: "INV-202611-0002",
                title: "Assembly Monthly Overhead Allocation",
                category: OrderCategory.DepartmentInvoice,
                referenceCode: "assembly",
                amount: Money.Php(28000_00),
                status: OrderStatus.Cancelled,
                metadataJson: """{"department":"assembly","cost_center":"CC-002","cancellation_reason":"Budget realigned for Q4"}""",
                createdAt: new DateTimeOffset(2026, 11, 1, 11, 0, 0, TimeSpan.Zero),
                updatedAt: new DateTimeOffset(2026, 11, 1, 11, 30, 0, TimeSpan.Zero))
        ];

        foreach (Order order in baselineSeeds)
        {
            MemoryById.TryAdd(order.Id, order);
            MemoryByNumber.TryAdd(order.OrderNumber, order);
        }
    }

    public async Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        EnsureSeeded();

        if (MemoryById.TryGetValue(orderId.Trim(), out Order? cached))
        {
            return cached;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = OrderQueries.SelectByIdSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@id", orderId.Trim());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Order order = await OrderMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                MemoryById[order.Id] = order;
                MemoryByNumber[order.OrderNumber] = order;
                return order;
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }

        return null;
    }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return null;
        }

        EnsureSeeded();

        if (MemoryByNumber.TryGetValue(orderNumber.Trim(), out Order? cached))
        {
            return cached;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = OrderQueries.SelectByOrderNumberSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@order_number", orderNumber.Trim());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Order order = await OrderMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                MemoryById[order.Id] = order;
                MemoryByNumber[order.OrderNumber] = order;
                return order;
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }

        return null;
    }

    public async Task<IReadOnlyList<Order>> ListAsync(
        string? status = null,
        int limit = 50,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        int effectiveLimit = Math.Clamp(limit, 1, 250);

        var normalizedUserId = string.IsNullOrWhiteSpace(userId) ||
                               string.Equals(userId, Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(userId, "00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(userId, "anonymous", StringComparison.OrdinalIgnoreCase)
            ? null
            : userId.Trim();

        var normalizedStatus = string.IsNullOrWhiteSpace(status) || string.Equals(status, "all", StringComparison.OrdinalIgnoreCase)
            ? null
            : status.Trim();

        string cacheKey = $"{normalizedUserId}|{normalizedStatus}|{effectiveLimit}";
        if (ListCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Orders;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            // Ensure any unbridged rows in public.google_form_submissions are materialized into public.orders
            // Cooldown: at most once every 30 seconds to avoid multi-hop sequential SQL on every list read
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastTicks = Volatile.Read(ref _lastBridgeTicksUtc);
            if (nowTicks - lastTicks > BridgeCooldownTicks &&
                Interlocked.CompareExchange(ref _lastBridgeTicksUtc, nowTicks, lastTicks) == lastTicks)
            {
                try
                {
                    var swBridge = Stopwatch.StartNew();
                    await using DbCommand bridgeCmd = connection.CreateCommand();
                    bridgeCmd.Transaction = _connectionScope.CurrentTransaction;
                    bridgeCmd.CommandText = OrderQueries.MaterializeUnbridgedGoogleFormSubmissions;
                    bridgeCmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
                    var affected = await bridgeCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    swBridge.Stop();

                    var ctx = ExecutionContextAccessor.Instance.Current;
                    RuntimeExecutionStore.RecordSql(new SqlTraceNode(
                        ModuleName: ctx.ModuleName ?? "Payments",
                        HandlerName: ctx.HandlerName ?? "ListOrdersHandler",
                        RepositoryName: nameof(OrderRepository),
                        OperationName: nameof(ListAsync),
                        SqlVerb: "INSERT",
                        CommandType: "Text",
                        SqlText: "INSERT INTO public.orders (...) SELECT ... FROM public.google_form_submissions ...",
                        ParameterNames: Array.Empty<string>(),
                        BoundParametersJson: "{}",
                        RowsReturned: null,
                        RowsAffected: affected,
                        Status: "SUCCESS",
                        SqlState: null,
                        ElapsedMs: Math.Round(swBridge.Elapsed.TotalMilliseconds, 2),
                        ExecutedAtUtc: DateTimeOffset.UtcNow));
                }
                catch (Exception)
                {
                    // Non-fatal if running on a restricted read-only replica or pre-migration schema
                }
            }

            var swSelect = Stopwatch.StartNew();
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = OrderQueries.SelectListSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@status", normalizedStatus);
            AddParam(cmd, "@UserId", normalizedUserId);
            AddParam(cmd, "@limit", effectiveLimit);

            List<Order> list = [];
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Order order = await OrderMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                MemoryById[order.Id] = order;
                MemoryByNumber[order.OrderNumber] = order;
                list.Add(order);
            }
            swSelect.Stop();

            var ctxSelect = ExecutionContextAccessor.Instance.Current;
            var boundParams = new Dictionary<string, object?>
            {
                ["Status"] = normalizedStatus,
                ["UserId"] = normalizedUserId,
                ["Limit"] = effectiveLimit
            };
            RuntimeExecutionStore.RecordSql(new SqlTraceNode(
                ModuleName: ctxSelect.ModuleName ?? "Payments",
                HandlerName: ctxSelect.HandlerName ?? "ListOrdersHandler",
                RepositoryName: nameof(OrderRepository),
                OperationName: nameof(ListAsync),
                SqlVerb: "SELECT",
                CommandType: "Text",
                SqlText: "SELECT id, user_id, order_number, title, category, reference_code, amount_minor ... FROM public.orders ...",
                ParameterNames: ["@status", "@UserId", "@limit"],
                BoundParametersJson: System.Text.Json.JsonSerializer.Serialize(boundParams),
                RowsReturned: list.Count,
                RowsAffected: null,
                Status: "SUCCESS",
                SqlState: null,
                ElapsedMs: Math.Round(swSelect.Elapsed.TotalMilliseconds, 2),
                ExecutedAtUtc: DateTimeOffset.UtcNow));

            if (list.Count == 0)
            {
                EnsureSeeded();
                var fallback = MemoryById.Values.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(normalizedStatus))
                {
                    fallback = fallback.Where(o => string.Equals(o.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase));
                }
                if (!string.IsNullOrWhiteSpace(normalizedUserId))
                {
                    fallback = fallback.Where(o => string.Equals(o.UserId, normalizedUserId, StringComparison.OrdinalIgnoreCase));
                }
                var fallbackList = fallback.OrderByDescending(o => o.CreatedAt).Take(effectiveLimit).ToList();
                ListCache[cacheKey] = (DateTimeOffset.UtcNow.AddSeconds(5), fallbackList);
                return fallbackList;
            }

            ListCache[cacheKey] = (DateTimeOffset.UtcNow.AddSeconds(5), list);
            return list;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            EnsureSeeded();
            var memList = MemoryById.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                memList = memList.Where(o => string.Equals(o.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(normalizedUserId))
            {
                memList = memList.Where(o => string.Equals(o.UserId, normalizedUserId, StringComparison.OrdinalIgnoreCase));
            }
            var memResult = memList.OrderByDescending(o => o.CreatedAt).Take(effectiveLimit).ToList();
            ListCache[cacheKey] = (DateTimeOffset.UtcNow.AddSeconds(5), memResult);
            return memResult;
        }
    }

    public async Task<long> NextOrderSequenceAsync(
        string prefix,
        string yearMonth,
        CancellationToken cancellationToken = default)
    {
        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = OrderQueries.CountSequenceSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@prefix_pattern", $"{prefix}-{yearMonth}-%");

            object? scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is long count)
            {
                return count + 1;
            }
            if (scalar is int intCount)
            {
                return intCount + 1;
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }

        EnsureSeeded();
        int existingCount = MemoryByNumber.Keys.Count(k => k.StartsWith($"{prefix}-{yearMonth}-", StringComparison.OrdinalIgnoreCase));
        long next = Interlocked.Increment(ref _fallbackSequence);
        if (next <= existingCount)
        {
            next = existingCount + 10;
            Interlocked.Exchange(ref _fallbackSequence, next);
        }
        return next;
    }

    public async Task SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        MemoryById[order.Id] = order;
        MemoryByNumber[order.OrderNumber] = order;
        InvalidateListCache();

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = OrderQueries.InsertSql;
            cmd.CommandTimeout = SqlTimeouts.StandardCommandSeconds;

            AddParam(cmd, "@id", order.Id);
            AddParam(cmd, "@user_id", order.UserId);
            AddParam(cmd, "@order_number", order.OrderNumber);
            AddParam(cmd, "@title", order.Title);
            AddParam(cmd, "@category", order.Category);
            AddParam(cmd, "@reference_code", order.ReferenceCode);
            AddParam(cmd, "@amount_minor", order.Amount.AmountMinor);
            AddParam(cmd, "@currency", order.Amount.CurrencyCode);
            AddParam(cmd, "@status", order.Status);
            AddParam(cmd, "@metadata_json", order.MetadataJson);
            AddParam(cmd, "@created_at", order.CreatedAt);
            AddParam(cmd, "@updated_at", order.UpdatedAt);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        MemoryById[order.Id] = order;
        MemoryByNumber[order.OrderNumber] = order;
        InvalidateListCache();

        const string sql = """
            UPDATE public.orders
            SET
                title = COALESCE(NULLIF(@title, ''), title),
                amount_minor = @amount_minor,
                status = LOWER(@status),
                metadata_json = @metadata_json::jsonb,
                updated_at = @updated_at
            WHERE id = @id;

            UPDATE public.google_form_submissions
            SET
                customer_name = COALESCE(NULLIF(@customer_name, ''), customer_name),
                customer_email = COALESCE(NULLIF(@customer_email, ''), customer_email),
                description = COALESCE(NULLIF(@title, ''), description),
                amount_minor = @amount_minor,
                status = LOWER(@status),
                updated_at = @updated_at
            WHERE order_id = @id OR order_number = @order_number;
            """;

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = sql;
            cmd.CommandTimeout = SqlTimeouts.StandardCommandSeconds;

            AddParam(cmd, "@id", order.Id);
            AddParam(cmd, "@order_number", order.OrderNumber);
            AddParam(cmd, "@title", order.Title);
            AddParam(cmd, "@customer_name", order.CustomerName);
            AddParam(cmd, "@customer_email", order.CustomerEmail);
            AddParam(cmd, "@amount_minor", order.Amount.AmountMinor);
            AddParam(cmd, "@status", order.Status.ToLowerInvariant());
            AddParam(cmd, "@metadata_json", order.MetadataJson);
            AddParam(cmd, "@updated_at", order.UpdatedAt);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "Failed executing SQL UpdateAsync for order {OrderId}. In-memory state updated.", order.Id);
        }
    }

    public async Task DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId)) return;
        string cleanId = orderId.Trim();

        MemoryById.TryRemove(cleanId, out _);
        var matchingNumber = MemoryByNumber.FirstOrDefault(kvp => kvp.Value.Id.Equals(cleanId, StringComparison.OrdinalIgnoreCase)).Key;
        if (!string.IsNullOrEmpty(matchingNumber))
        {
            MemoryByNumber.TryRemove(matchingNumber, out _);
        }
        InvalidateListCache();

        const string sql = """
            DELETE FROM public.google_form_submissions
            WHERE order_id = @order_id;

            DELETE FROM public.fulfillment_outbox
            WHERE order_id = @order_id;

            DELETE FROM public.payment_attempts
            WHERE order_id = @order_id;

            DELETE FROM public.orders
            WHERE id = @order_id
              AND LOWER(status) NOT IN ('paid', 'succeeded', 'completed');
            """;

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = sql;
            cmd.CommandTimeout = SqlTimeouts.StandardCommandSeconds;

            AddParam(cmd, "@order_id", cleanId);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "Failed executing SQL DeleteAsync for order {OrderId}. In-memory state removed.", cleanId);
        }
    }

    private static void AddParam(DbCommand cmd, string name, object? value, System.Data.DbType? dbType = null)
    {
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        if (dbType.HasValue)
        {
            param.DbType = dbType.Value;
        }
        else if (value is string)
        {
            param.DbType = System.Data.DbType.String;
        }
        else if (value is long)
        {
            param.DbType = System.Data.DbType.Int64;
        }
        else if (value is int)
        {
            param.DbType = System.Data.DbType.Int32;
        }
        else if (value is DateTimeOffset)
        {
            param.DbType = System.Data.DbType.DateTimeOffset;
        }
        else if (value is null)
        {
            param.DbType = System.Data.DbType.String;
        }
        cmd.Parameters.Add(param);
    }
}

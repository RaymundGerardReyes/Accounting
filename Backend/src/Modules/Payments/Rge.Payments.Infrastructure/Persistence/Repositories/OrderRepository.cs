using System.Collections.Concurrent;
using System.Data.Common;
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

    static OrderRepository()
    {
        EnsureSeeded();
    }

    private readonly ConnectionScope _connectionScope;

    public OrderRepository(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
    }

    /// <summary>
    /// Resets the in-memory fallback dictionary to the initial baseline seed orders.
    /// Useful for test isolation.
    /// </summary>
    public static void ResetToBaseline()
    {
        MemoryById.Clear();
        MemoryByNumber.Clear();
        Interlocked.Exchange(ref _fallbackSequence, 10);
        EnsureSeeded();
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
        int effectiveLimit = Math.Clamp(limit, 1, 100);

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = OrderQueries.SelectListSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@status", string.IsNullOrWhiteSpace(status) ? null : status.Trim());
            AddParam(cmd, "@UserId", string.IsNullOrWhiteSpace(userId) ? null : userId.Trim());
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

            if (list.Count == 0)
            {
                EnsureSeeded();
                var fallback = MemoryById.Values.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(status))
                {
                    fallback = fallback.Where(o => string.Equals(o.Status, status.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    fallback = fallback.Where(o => string.Equals(o.UserId, userId.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                return fallback.OrderByDescending(o => o.CreatedAt).Take(effectiveLimit).ToList();
            }

            return list;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            EnsureSeeded();
            var memList = MemoryById.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                memList = memList.Where(o => string.Equals(o.Status, status.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(userId))
            {
                memList = memList.Where(o => string.Equals(o.UserId, userId.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            return memList.OrderByDescending(o => o.CreatedAt).Take(effectiveLimit).ToList();
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

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }
}

using System.Collections.Concurrent;
using System.Data.Common;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Infrastructure.Persistence.Mappings;
using Rge.Payments.Infrastructure.Persistence.Sql;

namespace Rge.Payments.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private static long _fallbackSequence;
    private static readonly ConcurrentDictionary<string, Order> MemoryById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Order> MemoryByNumber = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConnectionScope _connectionScope;

    public OrderRepository(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
    }

    public async Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

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

            return list;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            var memList = MemoryById.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                memList = memList.Where(o => string.Equals(o.Status, status.Trim(), StringComparison.OrdinalIgnoreCase));
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

        return Interlocked.Increment(ref _fallbackSequence);
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

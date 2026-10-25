using System.Collections.Concurrent;
using System.Data.Common;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Persistence.Mappings;
using Rge.Payments.Infrastructure.Persistence.Sql;

namespace Rge.Payments.Infrastructure.Persistence.Repositories;

public sealed class PaymentAttemptRepository : IPaymentAttemptRepository
{
    private static readonly ConcurrentDictionary<string, PaymentAttempt> MemoryById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, PaymentAttempt> MemoryByProviderId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, PaymentAttempt> MemoryByIdempotency = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConnectionScope _connectionScope;

    public PaymentAttemptRepository(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
    }

    public async Task<PaymentAttempt?> GetByIdAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return null;
        }

        if (MemoryById.TryGetValue(attemptId.Trim(), out PaymentAttempt? cached))
        {
            return cached;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = AttemptQueries.SelectByIdSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@id", attemptId.Trim());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                PaymentAttempt attempt = await PaymentAttemptMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                IndexInMemory(attempt);
                return attempt;
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }

        return null;
    }

    public async Task<PaymentAttempt?> GetByProviderPaymentIdAsync(string providerPaymentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return null;
        }

        string cleanId = providerPaymentId.Trim();
        if (MemoryByProviderId.TryGetValue(cleanId, out PaymentAttempt? cached))
        {
            return cached;
        }
        if (!cleanId.StartsWith("cs_", StringComparison.OrdinalIgnoreCase) &&
            MemoryByProviderId.TryGetValue($"cs_{cleanId}", out PaymentAttempt? csCached))
        {
            return csCached;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = AttemptQueries.SelectByProviderPaymentIdSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@provider_payment_id", cleanId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                PaymentAttempt attempt = await PaymentAttemptMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                IndexInMemory(attempt);
                return attempt;
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }

        return null;
    }

    public async Task<PaymentAttempt?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        if (MemoryByIdempotency.TryGetValue(idempotencyKey.Trim(), out PaymentAttempt? cached))
        {
            return cached;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = AttemptQueries.SelectByIdempotencyKeySql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@idempotency_key", idempotencyKey.Trim());

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                PaymentAttempt attempt = await PaymentAttemptMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                IndexInMemory(attempt);
                return attempt;
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }

        return null;
    }

    public async Task<IReadOnlyList<PaymentAttempt>> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return [];
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = AttemptQueries.SelectByOrderIdSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@order_id", orderId.Trim());

            List<PaymentAttempt> list = [];
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                PaymentAttempt attempt = await PaymentAttemptMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                IndexInMemory(attempt);
                list.Add(attempt);
            }

            return list;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            return MemoryById.Values
                .Where(a => string.Equals(a.OrderId, orderId.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.CreatedAt)
                .ToList();
        }
    }

    public async Task<PaymentAttempt?> GetLatestByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var attempts = await GetByOrderIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        return attempts.FirstOrDefault();
    }

    public async Task<IReadOnlyList<PaymentAttempt>> ListNonTerminalAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = AttemptQueries.SelectNonTerminalSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;

            List<PaymentAttempt> list = [];
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                PaymentAttempt attempt = await PaymentAttemptMaterializer.MaterializeAsync(reader, cancellationToken).ConfigureAwait(false);
                IndexInMemory(attempt);
                list.Add(attempt);
            }

            return list;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            return MemoryById.Values
                .Where(a => !PaymentAttemptStateMachine.IsTerminal(a.Status))
                .OrderBy(a => a.CreatedAt)
                .ToList();
        }
    }

    public async Task SaveAsync(PaymentAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        IndexInMemory(attempt);

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = AttemptQueries.InsertSql;
            cmd.CommandTimeout = SqlTimeouts.StandardCommandSeconds;

            AddParam(cmd, "@id", attempt.Id);
            AddParam(cmd, "@order_id", attempt.OrderId);
            AddParam(cmd, "@provider", attempt.Provider);
            AddParam(cmd, "@payment_method", attempt.PaymentMethod);
            AddParam(cmd, "@provider_payment_id", attempt.ProviderPaymentId);
            AddParam(cmd, "@amount_minor", attempt.Amount.AmountMinor);
            AddParam(cmd, "@currency", attempt.Amount.CurrencyCode);
            AddParam(cmd, "@status", attempt.Status.ToString().ToLowerInvariant());
            AddParam(cmd, "@idempotency_key", attempt.IdempotencyKey);
            AddParam(cmd, "@checkout_url", attempt.CheckoutUrl);
            AddParam(cmd, "@failure_code", attempt.FailureCode);
            AddParam(cmd, "@failure_message", attempt.FailureMessage);
            AddParam(cmd, "@expires_at", attempt.ExpiresAt);
            AddParam(cmd, "@paid_at", attempt.PaidAt);
            AddParam(cmd, "@created_at", attempt.CreatedAt);
            AddParam(cmd, "@updated_at", attempt.UpdatedAt);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
        }
    }

    private static void IndexInMemory(PaymentAttempt attempt)
    {
        MemoryById[attempt.Id] = attempt;
        MemoryByIdempotency[attempt.IdempotencyKey] = attempt;
        if (!string.IsNullOrWhiteSpace(attempt.ProviderPaymentId))
        {
            MemoryByProviderId[attempt.ProviderPaymentId] = attempt;
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

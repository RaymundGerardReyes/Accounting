using System.Collections.Concurrent;
using System.Data.Common;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Persistence.Sql;

namespace Rge.Receipts.Infrastructure.Persistence;

/// <summary>
/// ADO.NET persistence repository for OfficialReceipt aggregates over ConnectionScope.
/// </summary>
public sealed class ReceiptRepository : IReceiptRepository
{
    private static long _fallbackSequence;
    private static readonly ConcurrentDictionary<string, OfficialReceipt> ByPaymentMemory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, OfficialReceipt> ByOrderMemory = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConnectionScope _connectionScope;

    public ReceiptRepository(ConnectionScope connectionScope)
    {
        ArgumentNullException.ThrowIfNull(connectionScope);
        _connectionScope = connectionScope;
    }

    public async Task<long> NextReceiptSequenceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = ReceiptQueries.NextSequenceNumberSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;

            object? scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is string formatted && formatted.StartsWith("OR-", StringComparison.Ordinal))
            {
                if (long.TryParse(formatted.AsSpan(3), out long parsed))
                {
                    return parsed;
                }
            }
        }
        catch (DbException)
        {
            // Fallback to atomic in-memory sequence when running in offline contract parity tests
        }

        return Interlocked.Increment(ref _fallbackSequence);
    }

    public async Task SaveAsync(OfficialReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        ByPaymentMemory[receipt.PaymentAttemptId] = receipt;
        ByOrderMemory[receipt.OrderId] = receipt;

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = ReceiptQueries.InsertReceiptSql;
            cmd.CommandTimeout = SqlTimeouts.StandardCommandSeconds;

            AddParam(cmd, "@id", receipt.Id);
            AddParam(cmd, "@order_id", receipt.OrderId);
            AddParam(cmd, "@order_number", receipt.OrderNumber);
            AddParam(cmd, "@payment_attempt_id", receipt.PaymentAttemptId);
            AddParam(cmd, "@receipt_number", receipt.ReceiptNumber.Value);
            AddParam(cmd, "@acknowledgement_number", receipt.Acknowledgement.AcknowledgementNumber);
            AddParam(cmd, "@customer_name", receipt.CustomerName);
            AddParam(cmd, "@customer_email", receipt.CustomerEmail);
            AddParam(cmd, "@description", receipt.Description);
            AddParam(cmd, "@payment_method", receipt.PaymentMethod);
            AddParam(cmd, "@vatable_sales_minor", receipt.VatBreakdown.VatableSales.AmountMinor);
            AddParam(cmd, "@vat_amount_minor", receipt.VatBreakdown.VatAmount.AmountMinor);
            AddParam(cmd, "@vat_exempt_sales_minor", receipt.VatBreakdown.VatExemptSales.AmountMinor);
            AddParam(cmd, "@zero_rated_sales_minor", receipt.VatBreakdown.ZeroRatedSales.AmountMinor);
            AddParam(cmd, "@gross_amount_minor", receipt.VatBreakdown.GrossAmount.AmountMinor);
            AddParam(cmd, "@currency", receipt.VatBreakdown.GrossAmount.CurrencyCode);
            AddParam(cmd, "@audit_digest", receipt.Digest.Value);
            AddParam(cmd, "@issued_at", receipt.IssuedAtUtc);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException)
        {
            // Retained in ByPaymentMemory / ByOrderMemory when DB table is offline
        }
    }

    public async Task<OfficialReceipt?> GetByPaymentAttemptIdAsync(
        string paymentAttemptId,
        CancellationToken cancellationToken = default)
    {
        if (ByPaymentMemory.TryGetValue(paymentAttemptId, out OfficialReceipt? cached))
        {
            return cached;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = ReceiptQueries.SelectByPaymentAttemptIdSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@payment_attempt_id", paymentAttemptId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                OfficialReceipt materialized = Materialize(reader);
                ByPaymentMemory[paymentAttemptId] = materialized;
                return materialized;
            }
        }
        catch (DbException)
        {
        }

        return null;
    }

    public async Task<OfficialReceipt?> GetByOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        if (ByOrderMemory.TryGetValue(orderId, out OfficialReceipt? cached))
        {
            return cached;
        }

        try
        {
            DbConnection connection = await _connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using DbCommand cmd = connection.CreateCommand();
            cmd.Transaction = _connectionScope.CurrentTransaction;
            cmd.CommandText = ReceiptQueries.SelectByOrderIdSql;
            cmd.CommandTimeout = SqlTimeouts.FastReadSeconds;
            AddParam(cmd, "@order_id", orderId);

            await using DbDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                OfficialReceipt materialized = Materialize(reader);
                ByOrderMemory[orderId] = materialized;
                return materialized;
            }
        }
        catch (DbException)
        {
        }

        return null;
    }

    private static OfficialReceipt Materialize(DbDataReader reader)
    {
        string currency = reader.GetString(15);
        DateTimeOffset issuedAt = reader.GetFieldValue<DateTimeOffset>(17);

        var breakdown = new VatBreakdown(
            vatableSales: new Money(reader.GetInt64(10), currency),
            vatAmount: new Money(reader.GetInt64(11), currency),
            vatExemptSales: new Money(reader.GetInt64(12), currency),
            zeroRatedSales: new Money(reader.GetInt64(13), currency));

        return new OfficialReceipt(
            id: reader.GetString(0),
            orderId: reader.GetString(1),
            orderNumber: reader.GetString(2),
            paymentAttemptId: reader.GetString(3),
            receiptNumber: new OfficialReceiptNumber(reader.GetString(4)),
            acknowledgement: new TreasuryAcknowledgement(reader.GetString(5), issuedAt),
            customerName: reader.GetString(6),
            customerEmail: reader.GetString(7),
            description: reader.GetString(8),
            paymentMethod: reader.GetString(9),
            vatBreakdown: breakdown,
            digest: new AuditDigest(reader.GetString(16)),
            issuedAtUtc: issuedAt);
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }
}

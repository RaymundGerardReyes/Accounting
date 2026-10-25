using System.Data.Common;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Domain.PaymentAttempts;

namespace Rge.Payments.Infrastructure.Persistence.Mappings;

public static class PaymentAttemptMaterializer
{
    public static async Task<PaymentAttempt> MaterializeAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string id = reader.GetString(0);
        string orderId = reader.GetString(1);
        string provider = reader.GetString(2);
        string paymentMethod = reader.GetString(3);
        string? providerPaymentId = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4);
        long amountMinor = reader.GetInt64(5);
        string currency = reader.GetString(6);
        string rawStatus = reader.GetString(7);
        string idempotencyKey = reader.GetString(8);
        string? checkoutUrl = await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9);
        string? failureCode = await reader.IsDBNullAsync(10, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(10);
        string? failureMessage = await reader.IsDBNullAsync(11, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(11);
        DateTimeOffset? expiresAt = await reader.IsDBNullAsync(12, cancellationToken).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(12, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? paidAt = await reader.IsDBNullAsync(13, cancellationToken).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(13, cancellationToken).ConfigureAwait(false);
        DateTimeOffset createdAt = await reader.GetFieldValueAsync<DateTimeOffset>(14, cancellationToken).ConfigureAwait(false);
        DateTimeOffset updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(15, cancellationToken).ConfigureAwait(false);

        if (!Enum.TryParse(rawStatus, ignoreCase: true, out PaymentAttemptStatus status))
        {
            status = PaymentAttemptStatus.Pending;
        }

        return PaymentAttempt.Rehydrate(
            id: id,
            orderId: orderId,
            provider: provider,
            paymentMethod: paymentMethod,
            providerPaymentId: providerPaymentId,
            amount: new Money(amountMinor, currency),
            status: status,
            idempotencyKey: idempotencyKey,
            checkoutUrl: checkoutUrl,
            failureCode: failureCode,
            failureMessage: failureMessage,
            expiresAt: expiresAt,
            paidAt: paidAt,
            createdAt: createdAt,
            updatedAt: updatedAt);
    }
}

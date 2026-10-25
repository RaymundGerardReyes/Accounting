using System.Data.Common;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Domain.Orders;

namespace Rge.Payments.Infrastructure.Persistence.Mappings;

public static class OrderMaterializer
{
    public static async Task<Order> MaterializeAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string id = reader.GetString(0);
        string userId = reader.GetString(1);
        string orderNumber = reader.GetString(2);
        string title = reader.GetString(3);
        string category = reader.GetString(4);
        string referenceCode = reader.GetString(5);
        long amountMinor = reader.GetInt64(6);
        string currency = reader.GetString(7);
        string status = reader.GetString(8);
        string metadataJson = await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? "{}" : reader.GetString(9);
        DateTimeOffset createdAt = await reader.GetFieldValueAsync<DateTimeOffset>(10, cancellationToken).ConfigureAwait(false);
        DateTimeOffset updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(11, cancellationToken).ConfigureAwait(false);

        return Order.Rehydrate(
            id: id,
            userId: userId,
            orderNumber: orderNumber,
            title: title,
            category: category,
            referenceCode: referenceCode,
            amount: new Money(amountMinor, currency),
            status: status,
            metadataJson: metadataJson,
            createdAt: createdAt,
            updatedAt: updatedAt);
    }
}

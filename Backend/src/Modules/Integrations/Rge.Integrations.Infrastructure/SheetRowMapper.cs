using System.Globalization;
using Rge.Integrations.Domain;

namespace Rge.Integrations.Infrastructure;

public static class SheetRowMapper
{
    public static object?[] MapToRow(GoogleFormSubmission submission, DateTimeOffset syncTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return
        [
            submission.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            submission.ResponseId,
            submission.CustomerName,
            submission.CustomerEmail,
            submission.CustomerPhone,
            submission.ItemReference,
            submission.Quantity,
            (submission.Amount.AmountMinor / 100.0).ToString("N2", CultureInfo.InvariantCulture),
            submission.PaymentMethod,
            "pending",
            submission.OrderId,
            submission.OrderNumber,
            submission.CheckoutUrl,
            submission.StudentId,
            submission.InstructorCredit,
            submission.BookTitle,
            submission.Notes,
            syncTimeUtc.ToString("O", CultureInfo.InvariantCulture)
        ];
    }
}


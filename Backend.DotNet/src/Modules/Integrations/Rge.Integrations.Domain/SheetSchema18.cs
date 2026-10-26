namespace Rge.Integrations.Domain;

/// <summary>
/// Defines the authoritative 18-column schema of the Google Sheets orders spreadsheet.
/// </summary>
public static class SheetSchema18
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "Timestamp",
        "Response ID",
        "Customer Name",
        "Customer Email",
        "Contact Number",
        "Item Reference",
        "Quantity",
        "Gross Amount (PHP)",
        "Payment Method",
        "Payment Status",
        "Order ID",
        "Order Number",
        "Checkout URL",
        "Student ID",
        "Instructor Credit",
        "Book Title",
        "Notes / Remarks",
        "Last Sync UTC"
    ];

    public const int TimestampIndex = 0;
    public const int ResponseIdIndex = 1;
    public const int CustomerNameIndex = 2;
    public const int CustomerEmailIndex = 3;
    public const int CustomerPhoneIndex = 4;
    public const int ItemReferenceIndex = 5;
    public const int QuantityIndex = 6;
    public const int AmountIndex = 7;
    public const int PaymentMethodIndex = 8;
    public const int PaymentStatusIndex = 9;
    public const int OrderIdIndex = 10;
    public const int OrderNumberIndex = 11;
    public const int CheckoutUrlIndex = 12;
    public const int StudentIdIndex = 13;
    public const int InstructorCreditIndex = 14;
    public const int BookTitleIndex = 15;
    public const int NotesIndex = 16;
    public const int LastSyncIndex = 17;
}


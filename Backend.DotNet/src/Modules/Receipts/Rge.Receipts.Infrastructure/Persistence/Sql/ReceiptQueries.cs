namespace Rge.Receipts.Infrastructure.Persistence.Sql;

/// <summary>
/// Parameterized SQL statements for the Receipts module.
/// </summary>
public static class ReceiptQueries
{
    public const string NextSequenceNumberSql = """
        SELECT fn_next_sequence_number('OR', 4);
        """;

    public const string InsertReceiptSql = """
        INSERT INTO official_receipts (
            id, order_id, order_number, payment_attempt_id,
            receipt_number, acknowledgement_number, customer_name, customer_email,
            description, payment_method, vatable_sales_minor, vat_amount_minor,
            vat_exempt_sales_minor, zero_rated_sales_minor, gross_amount_minor,
            currency, audit_digest, issued_at
        )
        VALUES (
            @id, @order_id, @order_number, @payment_attempt_id,
            @receipt_number, @acknowledgement_number, @customer_name, @customer_email,
            @description, @payment_method, @vatable_sales_minor, @vat_amount_minor,
            @vat_exempt_sales_minor, @zero_rated_sales_minor, @gross_amount_minor,
            @currency, @audit_digest, @issued_at
        )
        ON CONFLICT (payment_attempt_id) DO NOTHING;
        """;

    public const string SelectByPaymentAttemptIdSql = """
        SELECT id, order_id, order_number, payment_attempt_id,
               receipt_number, acknowledgement_number, customer_name, customer_email,
               description, payment_method, vatable_sales_minor, vat_amount_minor,
               vat_exempt_sales_minor, zero_rated_sales_minor, gross_amount_minor,
               currency, audit_digest, issued_at
          FROM official_receipts
         WHERE payment_attempt_id = @payment_attempt_id
         LIMIT 1;
        """;

    public const string SelectByOrderIdSql = """
        SELECT id, order_id, order_number, payment_attempt_id,
               receipt_number, acknowledgement_number, customer_name, customer_email,
               description, payment_method, vatable_sales_minor, vat_amount_minor,
               vat_exempt_sales_minor, zero_rated_sales_minor, gross_amount_minor,
               currency, audit_digest, issued_at
          FROM official_receipts
         WHERE order_id = @order_id
         ORDER BY issued_at DESC
         LIMIT 1;
        """;
}


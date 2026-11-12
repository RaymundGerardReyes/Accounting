namespace Rge.Payments.Infrastructure.Persistence.Sql;

public static class AttemptQueries
{
    public const string InsertSql = """
        INSERT INTO payment_attempts (
            id, order_id, provider, payment_method, provider_payment_id,
            amount_minor, currency, status, idempotency_key, checkout_url,
            failure_code, failure_message, expires_at, paid_at, created_at, updated_at
        ) VALUES (
            @id, @order_id, @provider, @payment_method, @provider_payment_id,
            @amount_minor, @currency, @status, @idempotency_key, @checkout_url,
            @failure_code, @failure_message, @expires_at, @paid_at, @created_at, @updated_at
        )
        ON CONFLICT (id) DO UPDATE SET
            status = CASE
                WHEN LOWER(payment_attempts.status) = 'succeeded' THEN payment_attempts.status
                ELSE EXCLUDED.status
            END,
            provider_payment_id = COALESCE(EXCLUDED.provider_payment_id, payment_attempts.provider_payment_id),
            checkout_url = COALESCE(EXCLUDED.checkout_url, payment_attempts.checkout_url),
            failure_code = CASE
                WHEN LOWER(payment_attempts.status) = 'succeeded' THEN payment_attempts.failure_code
                ELSE EXCLUDED.failure_code
            END,
            failure_message = CASE
                WHEN LOWER(payment_attempts.status) = 'succeeded' THEN payment_attempts.failure_message
                ELSE EXCLUDED.failure_message
            END,
            expires_at = EXCLUDED.expires_at,
            paid_at = CASE
                WHEN LOWER(payment_attempts.status) = 'succeeded' THEN COALESCE(payment_attempts.paid_at, EXCLUDED.paid_at)
                ELSE EXCLUDED.paid_at
            END,
            updated_at = EXCLUDED.updated_at;
        """;

    public const string SelectByIdSql = """
        SELECT id, order_id, provider, payment_method, provider_payment_id,
               amount_minor, currency, status, idempotency_key, checkout_url,
               failure_code, failure_message, expires_at, paid_at, created_at, updated_at
        FROM payment_attempts
        WHERE id = @id;
        """;

    public const string SelectByProviderPaymentIdSql = """
        SELECT id, order_id, provider, payment_method, provider_payment_id,
               amount_minor, currency, status, idempotency_key, checkout_url,
               failure_code, failure_message, expires_at, paid_at, created_at, updated_at
        FROM payment_attempts
        WHERE provider_payment_id = @provider_payment_id
           OR provider_payment_id = 'cs_' || @provider_payment_id;
        """;

    public const string SelectByIdempotencyKeySql = """
        SELECT id, order_id, provider, payment_method, provider_payment_id,
               amount_minor, currency, status, idempotency_key, checkout_url,
               failure_code, failure_message, expires_at, paid_at, created_at, updated_at
        FROM payment_attempts
        WHERE idempotency_key = @idempotency_key;
        """;

    public const string SelectByOrderIdSql = """
        SELECT id, order_id, provider, payment_method, provider_payment_id,
               amount_minor, currency, status, idempotency_key, checkout_url,
               failure_code, failure_message, expires_at, paid_at, created_at, updated_at
        FROM payment_attempts
        WHERE order_id = @order_id
        ORDER BY created_at DESC;
        """;

    public const string SelectNonTerminalSql = """
        SELECT id, order_id, provider, payment_method, provider_payment_id,
               amount_minor, currency, status, idempotency_key, checkout_url,
               failure_code, failure_message, expires_at, paid_at, created_at, updated_at
        FROM payment_attempts
        WHERE LOWER(status) IN ('pending', 'requiresaction', 'requires_action')
        ORDER BY created_at ASC;
        """;
}

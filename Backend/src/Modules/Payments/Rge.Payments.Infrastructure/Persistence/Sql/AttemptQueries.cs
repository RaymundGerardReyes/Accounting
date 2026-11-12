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
            status = EXCLUDED.status,
            provider_payment_id = COALESCE(EXCLUDED.provider_payment_id, payment_attempts.provider_payment_id),
            checkout_url = EXCLUDED.checkout_url,
            failure_code = EXCLUDED.failure_code,
            failure_message = EXCLUDED.failure_message,
            expires_at = EXCLUDED.expires_at,
            paid_at = EXCLUDED.paid_at,
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
        WHERE status NOT IN ('succeeded', 'failed', 'cancelled', 'expired')
        ORDER BY created_at ASC;
        """;
}

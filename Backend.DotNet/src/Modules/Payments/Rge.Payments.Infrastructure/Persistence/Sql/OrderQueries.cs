namespace Rge.Payments.Infrastructure.Persistence.Sql;

public static class OrderQueries
{
    public const string InsertSql = """
        INSERT INTO orders (
            id, user_id, order_number, title, category, reference_code,
            amount_minor, currency, status, metadata_json, created_at, updated_at
        ) VALUES (
            @id, @user_id, @order_number, @title, @category, @reference_code,
            @amount_minor, @currency, @status, @metadata_json::jsonb, @created_at, @updated_at
        )
        ON CONFLICT (id) DO UPDATE SET
            status = EXCLUDED.status,
            metadata_json = EXCLUDED.metadata_json,
            updated_at = EXCLUDED.updated_at;
        """;

    public const string SelectByIdSql = """
        SELECT id, user_id, order_number, title, category, reference_code,
               amount_minor, currency, status, metadata_json, created_at, updated_at
        FROM orders
        WHERE id = @id;
        """;

    public const string SelectByOrderNumberSql = """
        SELECT id, user_id, order_number, title, category, reference_code,
               amount_minor, currency, status, metadata_json, created_at, updated_at
        FROM orders
        WHERE order_number = @order_number;
        """;

    public const string SelectListSql = """
        SELECT id, user_id, order_number, title, category, reference_code,
               amount_minor, currency, status, metadata_json, created_at, updated_at
        FROM orders
        WHERE (@status IS NULL OR status = @status)
        ORDER BY created_at DESC
        LIMIT @limit;
        """;

    public const string CountSequenceSql = """
        SELECT COUNT(*)
        FROM orders
        WHERE order_number LIKE @prefix_pattern;
        """;
}

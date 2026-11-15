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
          AND (@UserId IS NULL OR user_id = @UserId)
        ORDER BY created_at DESC, id DESC
        LIMIT @limit;
        """;

    public const string ListOrders = """
        SELECT
            o.id,
            o.user_id,
            o.order_number,
            o.title,
            o.category,
            o.reference_code,
            o.amount_minor,
            o.currency,
            o.status,
            o.metadata_json,
            o.created_at,
            o.updated_at
        FROM public.orders o
        WHERE (@user_id IS NULL OR o.user_id = @user_id)
          AND (@status IS NULL OR LOWER(o.status) = LOWER(@status))
          AND (@category IS NULL OR LOWER(o.category) = LOWER(@category))
          AND (
              @search IS NULL
              OR o.order_number ILIKE '%' || @search || '%'
              OR o.title ILIKE '%' || @search || '%'
              OR o.reference_code ILIKE '%' || @search || '%'
              OR COALESCE(o.metadata_json, '') ILIKE '%' || @search || '%'
          )
          AND (@cursor IS NULL OR o.created_at < @cursor)
        ORDER BY o.created_at DESC, o.id DESC
        LIMIT @limit;
        """;

    public const string ListAllOrders = """
        SELECT id, user_id, order_number, title, category, reference_code,
               amount_minor, currency, status, metadata_json, created_at, updated_at
        FROM orders
        ORDER BY created_at DESC, id DESC
        LIMIT @limit;
        """;

    public const string CountSequenceSql = """
        SELECT COUNT(*)
        FROM orders
        WHERE order_number LIKE @prefix_pattern;
        """;
}

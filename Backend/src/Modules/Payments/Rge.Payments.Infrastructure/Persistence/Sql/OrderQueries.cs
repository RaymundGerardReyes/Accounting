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
            o.updated_at,
            o.customer_name,
            o.customer_email,
            o.description,
            o.subtotal_minor,
            o.vat_minor,
            o.source_channel,
            o.external_reference_id,
            o.idempotency_key
        FROM public.orders o
        WHERE (CAST(@status AS text) IS NULL
               OR LOWER(o.status) = LOWER(CAST(@status AS text))
               OR (LOWER(CAST(@status AS text)) = 'pending' AND LOWER(o.status) IN ('pending', 'pending_payment'))
               OR (LOWER(CAST(@status AS text)) = 'paid' AND LOWER(o.status) IN ('paid', 'settled')))
          AND (CAST(@UserId AS text) IS NULL OR o.user_id = CAST(@UserId AS text) OR o.user_id = 'google_forms_bridge' OR o.user_id = 'anonymous' OR o.id LIKE 'ord_gf_%')
        ORDER BY o.created_at DESC, o.id DESC
        LIMIT COALESCE(@limit, 250);
        """;

    public const string ListOrders = SelectListSql;

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

    public const string MaterializeUnbridgedGoogleFormSubmissions = """
        DO $$
        BEGIN
            IF to_regclass('public.google_form_submissions') IS NOT NULL AND to_regclass('public.orders') IS NOT NULL THEN
                INSERT INTO public.orders (
                    id,
                    user_id,
                    order_number,
                    title,
                    category,
                    reference_code,
                    amount_minor,
                    currency,
                    status,
                    metadata_json,
                    created_at,
                    updated_at
                )
                SELECT
                    COALESCE(NULLIF(g.order_id, ''), 'ord_gf_' || SUBSTRING(REPLACE(MD5(g.response_id), '-', ''), 1, 8)),
                    COALESCE(NULLIF(g.customer_email, ''), 'google_forms_bridge'),
                    COALESCE(NULLIF(g.order_number, ''), 'ORD-GF-' || UPPER(SUBSTRING(REPLACE(MD5(g.response_id), '-', ''), 1, 8))),
                    COALESCE(NULLIF(g.description, ''), 'Google Form Order'),
                    'product_order',
                    COALESCE(NULLIF(g.description, ''), 'GFORM-ITEM'),
                    GREATEST(g.amount_minor, 100),
                    COALESCE(NULLIF(g.currency, ''), 'PHP'),
                    CASE WHEN LOWER(g.status) = 'paid' THEN 'paid' ELSE 'pending' END,
                    jsonb_build_object(
                        'source', 'google_forms',
                        'customer_name', g.customer_name,
                        'customer_email', g.customer_email,
                        'google_form_response_id', g.response_id,
                        'response_id', g.response_id
                    )::text,
                    COALESCE(g.created_at, NOW()),
                    NOW()
                FROM public.google_form_submissions g
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM public.orders o
                    WHERE (g.order_id IS NOT NULL AND g.order_id <> '' AND o.id = g.order_id)
                       OR (g.order_number IS NOT NULL AND g.order_number <> '' AND o.order_number = g.order_number)
                       OR (o.metadata_json LIKE '%' || g.response_id || '%')
                )
                ON CONFLICT (id) DO NOTHING;

                UPDATE public.google_form_submissions g
                SET
                    order_id = o.id,
                    order_number = o.order_number,
                    status = CASE WHEN o.status = 'paid' THEN 'paid' ELSE g.status END
                FROM public.orders o
                WHERE (g.order_id IS NULL OR g.order_id = '' OR g.order_id <> o.id)
                  AND (o.metadata_json LIKE '%' || g.response_id || '%');
            END IF;
        END $$;
        """;

    public const string ListOrdersPagedSql = """
        WITH filtered_base AS (
            SELECT
                o.id,
                o.order_number,
                o.customer_name,
                o.customer_email,
                o.category,
                o.status,
                COALESCE(o.subtotal_minor, 0) AS subtotal_centavos,
                COALESCE(o.vat_minor, 0) AS vat_centavos,
                o.amount_minor AS total_centavos,
                o.currency,
                o.created_at,
                o.updated_at
            FROM public.orders o
            WHERE (@Status IS NULL OR LOWER(o.status) = LOWER(@Status))
              AND (@Category IS NULL OR LOWER(o.category) = LOWER(@Category))
              AND (
                  @SearchPattern IS NULL
                  OR o.order_number ILIKE @SearchPattern ESCAPE '\'
                  OR o.customer_name ILIKE @SearchPattern ESCAPE '\'
                  OR o.customer_email ILIKE @SearchPattern ESCAPE '\'
              )
        ),
        total_meta AS (
            SELECT COUNT(*)::int AS total_count
            FROM filtered_base
        ),
        paged_orders AS (
            SELECT
                fb.*
            FROM filtered_base fb
            ORDER BY fb.created_at DESC, fb.id DESC
            LIMIT @PageSize OFFSET @Offset
        )
        SELECT
            tm.total_count,
            po.id,
            po.order_number,
            po.customer_name,
            po.customer_email,
            po.category,
            po.status,
            po.subtotal_centavos,
            po.vat_centavos,
            po.total_centavos,
            po.currency,
            po.created_at,
            po.updated_at
        FROM total_meta tm
        LEFT JOIN paged_orders po ON TRUE
        ORDER BY po.created_at DESC NULLS LAST, po.id DESC NULLS LAST;
        """;

    public const string ListOrdersKeysetSql = """
        WITH paged_orders AS (
            SELECT
                o.id,
                o.order_number,
                o.customer_name,
                o.customer_email,
                o.category,
                o.status,
                COALESCE(o.subtotal_minor, 0) AS subtotal_centavos,
                COALESCE(o.vat_minor, 0) AS vat_centavos,
                o.amount_minor AS total_centavos,
                o.currency,
                o.created_at,
                o.updated_at
            FROM public.orders o
            WHERE (@Status IS NULL OR LOWER(o.status) = LOWER(@Status))
              AND (@Category IS NULL OR LOWER(o.category) = LOWER(@Category))
              AND (
                  @SearchPattern IS NULL
                  OR o.order_number ILIKE @SearchPattern ESCAPE '\'
                  OR o.customer_name ILIKE @SearchPattern ESCAPE '\'
                  OR o.customer_email ILIKE @SearchPattern ESCAPE '\'
              )
              AND (
                  @CursorCreatedAt IS NULL
                  OR (o.created_at, o.id) < (@CursorCreatedAt, @CursorId)
              )
            ORDER BY o.created_at DESC, o.id DESC
            LIMIT @PageSizePlusOne
        )
        SELECT
            0 AS total_count,
            po.id,
            po.order_number,
            po.customer_name,
            po.customer_email,
            po.category,
            po.status,
            po.subtotal_centavos,
            po.vat_centavos,
            po.total_centavos,
            po.currency,
            po.created_at,
            po.updated_at
        FROM paged_orders po
        ORDER BY po.created_at DESC, po.id DESC;
        """;
}
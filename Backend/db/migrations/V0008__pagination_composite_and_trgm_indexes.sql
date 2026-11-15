-- V0008__pagination_composite_and_trgm_indexes.sql

-- 1. Unfiltered & Keyset default ordering index (eliminates Sort node on ORDER BY created_at DESC, id DESC)
CREATE INDEX IF NOT EXISTS ix_orders_created_at_id_desc
    ON public.orders (created_at DESC, id DESC);

-- 2. Status-filtered ordering index (supports WHERE status = @Status ORDER BY created_at DESC, id DESC)
CREATE INDEX IF NOT EXISTS ix_orders_status_created_at_id_desc
    ON public.orders (status, created_at DESC, id DESC);

-- 3. Category + Status composite index
CREATE INDEX IF NOT EXISTS ix_orders_category_status_created_at_id_desc
    ON public.orders (category, status, created_at DESC, id DESC);

-- 4. Enable pg_trgm for substring ILIKE '%search%' on order_number, customer_name, and customer_email
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX IF NOT EXISTS ix_orders_order_number_trgm
    ON public.orders USING GIN (order_number gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_orders_customer_name_trgm
    ON public.orders USING GIN (customer_name gin_trgm_ops);

CREATE INDEX IF NOT EXISTS ix_orders_customer_email_trgm
    ON public.orders USING GIN (customer_email gin_trgm_ops);

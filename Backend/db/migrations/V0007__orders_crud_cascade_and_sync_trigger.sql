-- V0007: Orders CRUD cascade foreign keys and two-way sync trigger

-- 1. Allow deleting unpaid/cancelled orders without FK crashes on payment_attempts & fulfillment_outbox
ALTER TABLE public.payment_attempts
DROP CONSTRAINT IF EXISTS payment_attempts_order_id_fkey,
ADD CONSTRAINT payment_attempts_order_id_fkey
    FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE CASCADE;

ALTER TABLE public.fulfillment_outbox
DROP CONSTRAINT IF EXISTS fulfillment_outbox_order_id_fkey,
ADD CONSTRAINT fulfillment_outbox_order_id_fkey
    FOREIGN KEY (order_id) REFERENCES public.orders(id) ON DELETE CASCADE;

ALTER TABLE public.fulfillment_outbox
DROP CONSTRAINT IF EXISTS fulfillment_outbox_payment_attempt_id_fkey,
ADD CONSTRAINT fulfillment_outbox_payment_attempt_id_fkey
    FOREIGN KEY (payment_attempt_id) REFERENCES public.payment_attempts(id) ON DELETE CASCADE;

-- 2. Two-way CRUD trigger: When an order in public.orders is UPDATED or DELETED,
--    automatically mirror the UPDATE or DELETE into public.google_form_submissions
CREATE OR REPLACE FUNCTION public.fn_sync_orders_crud_to_google_forms()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM public.google_form_submissions
        WHERE order_id = OLD.id OR order_number = OLD.order_number;
        RETURN OLD;
    ELSIF TG_OP = 'UPDATE' THEN
        UPDATE public.google_form_submissions
        SET
            customer_name = COALESCE(NULLIF(NEW.customer_name, ''), customer_name),
            customer_email = COALESCE(NULLIF(NEW.customer_email, ''), customer_email),
            description = COALESCE(NULLIF(NEW.description, ''), NEW.title, description),
            amount_minor = NEW.amount_minor,
            status = LOWER(NEW.status),
            updated_at = NOW()
        WHERE order_id = NEW.id OR order_number = NEW.order_number;
        RETURN NEW;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_sync_orders_crud_to_google_forms ON public.orders;
CREATE TRIGGER trg_sync_orders_crud_to_google_forms
AFTER UPDATE OR DELETE ON public.orders
FOR EACH ROW
EXECUTE FUNCTION public.fn_sync_orders_crud_to_google_forms();

-- Migration: V0006__schema_polish_and_live_sync_tracking.sql
-- Live sync tracking and foreign key polish for Google Form submissions

CREATE TABLE IF NOT EXISTS public.google_form_submissions (
    response_id       character varying(128) NOT NULL,
    order_id          character varying(36)  NOT NULL,
    order_number      character varying(64)  NOT NULL,
    customer_name     character varying(255) NOT NULL,
    customer_email    character varying(255) NOT NULL,
    description       text                   NOT NULL,
    amount_minor      bigint                 NOT NULL,
    currency          character varying(3)   NOT NULL DEFAULT 'PHP',
    status            character varying(32)  NOT NULL DEFAULT 'pending',
    email_dispatched  boolean                NOT NULL DEFAULT true,
    raw_payload_json  text                   NULL,
    created_at        timestamp with time zone NOT NULL DEFAULT now(),
    updated_at        timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT google_form_submissions_pkey PRIMARY KEY (response_id)
);

CREATE INDEX IF NOT EXISTS ix_google_form_submissions_order_id
    ON public.google_form_submissions USING btree (order_id);

CREATE INDEX IF NOT EXISTS ix_google_form_submissions_created_at_desc
    ON public.google_form_submissions USING btree (created_at DESC);

-- Ensure non-blocking ingestion if foreign key was previously attached
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'google_form_submissions_order_id_fkey'
          AND table_name = 'google_form_submissions'
    ) THEN
        EXECUTE 'ALTER TABLE public.google_form_submissions DROP ' || 'CONSTRAINT google_form_submissions_order_id_fkey';
    END IF;
END $$;

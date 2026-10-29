CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE OR REPLACE FUNCTION fn_authenticate_user(
    p_identifier VARCHAR(128),
    p_password   TEXT
)
RETURNS TABLE (
    is_authenticated BOOLEAN,
    user_id          VARCHAR(64),
    failure_reason   VARCHAR(64),
    locked_until     TIMESTAMPTZ
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_clean_id       TEXT := LOWER(btrim(COALESCE(p_identifier, '')));
    v_user_id        VARCHAR(64);
    v_password_hash  TEXT;
    v_failed_count   INT;
    v_locked_until   TIMESTAMPTZ;
    v_now            TIMESTAMPTZ := clock_timestamp();
BEGIN
    IF v_clean_id = '' OR p_password IS NULL OR p_password = '' THEN
        RETURN QUERY SELECT FALSE, NULL::VARCHAR(64), 'INVALID_CREDENTIALS'::VARCHAR(64), NULL::TIMESTAMPTZ;
        RETURN;
    END IF;

    -- Uses functional indexes ix_user_credentials_lower_username / ix_user_credentials_lower_email
    SELECT uc.user_id, uc.password_hash, uc.failed_attempts, uc.locked_until
      INTO v_user_id, v_password_hash, v_failed_count, v_locked_until
      FROM public.user_credentials uc
     WHERE LOWER(uc.username) = v_clean_id
        OR LOWER(uc.email)    = v_clean_id
     LIMIT 1
     FOR UPDATE;

    IF NOT FOUND THEN
        -- Constant-time dummy bcrypt work factor to prevent user-enumeration timing attacks
        PERFORM public.crypt('dummy_timing_defense_password', '$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy');
        RETURN QUERY SELECT FALSE, NULL::VARCHAR(64), 'INVALID_CREDENTIALS'::VARCHAR(64), NULL::TIMESTAMPTZ;
        RETURN;
    END IF;

    IF v_locked_until IS NOT NULL AND v_locked_until > v_now THEN
        RETURN QUERY SELECT FALSE, v_user_id, 'ACCOUNT_LOCKED'::VARCHAR(64), v_locked_until;
        RETURN;
    END IF;

    IF v_password_hash = public.crypt(p_password, v_password_hash) THEN
        UPDATE public.user_credentials
           SET failed_attempts = 0,
               locked_until    = NULL,
               last_login_at   = v_now
         WHERE public.user_credentials.user_id = v_user_id;

        RETURN QUERY SELECT TRUE, v_user_id, NULL::VARCHAR(64), NULL::TIMESTAMPTZ;
    ELSE
        v_failed_count := COALESCE(v_failed_count, 0) + 1;
        IF v_failed_count >= 5 THEN
            v_locked_until := v_now + INTERVAL '15 minutes';
        ELSE
            v_locked_until := NULL;
        END IF;

        UPDATE public.user_credentials
           SET failed_attempts = v_failed_count,
               locked_until    = v_locked_until
         WHERE public.user_credentials.user_id = v_user_id;

        RETURN QUERY SELECT FALSE, v_user_id,
            CASE WHEN v_failed_count >= 5 THEN 'ACCOUNT_LOCKED'::VARCHAR(64) ELSE 'INVALID_CREDENTIALS'::VARCHAR(64) END,
            v_locked_until;
    END IF;
END;
$$;

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
AS $$
DECLARE
    v_user_id        VARCHAR(64);
    v_password_hash  TEXT;
    v_failed_count   INT;
    v_locked_until   TIMESTAMPTZ;
BEGIN
    SELECT uc.user_id, uc.password_hash, uc.failed_attempts, uc.locked_until
      INTO v_user_id, v_password_hash, v_failed_count, v_locked_until
      FROM user_credentials uc
     WHERE LOWER(uc.username) = LOWER(btrim(p_identifier))
        OR LOWER(uc.email) = LOWER(btrim(p_identifier))
     FOR UPDATE;

    IF NOT FOUND THEN
        RETURN QUERY SELECT FALSE, NULL::VARCHAR(64), 'INVALID_CREDENTIALS'::VARCHAR(64), NULL::TIMESTAMPTZ;
        RETURN;
    END IF;

    IF v_locked_until IS NOT NULL AND v_locked_until > clock_timestamp() THEN
        RETURN QUERY SELECT FALSE, v_user_id, 'ACCOUNT_LOCKED'::VARCHAR(64), v_locked_until;
        RETURN;
    END IF;

    IF v_password_hash = crypt(p_password, v_password_hash) THEN
        UPDATE user_credentials
           SET failed_attempts = 0,
               locked_until = NULL,
               last_login_at = clock_timestamp()
         WHERE user_credentials.user_id = v_user_id;

        RETURN QUERY SELECT TRUE, v_user_id, NULL::VARCHAR(64), NULL::TIMESTAMPTZ;
    ELSE
        v_failed_count := COALESCE(v_failed_count, 0) + 1;
        IF v_failed_count >= 5 THEN
            v_locked_until := clock_timestamp() + INTERVAL '15 minutes';
        END IF;

        UPDATE user_credentials
           SET failed_attempts = v_failed_count,
               locked_until = v_locked_until
         WHERE user_credentials.user_id = v_user_id;

        RETURN QUERY SELECT FALSE, v_user_id,
            CASE WHEN v_failed_count >= 5 THEN 'ACCOUNT_LOCKED'::VARCHAR(64) ELSE 'INVALID_CREDENTIALS'::VARCHAR(64) END,
            v_locked_until;
    END IF;
END;
$$;


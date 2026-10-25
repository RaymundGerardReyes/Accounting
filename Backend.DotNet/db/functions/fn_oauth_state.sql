CREATE OR REPLACE FUNCTION fn_oauth_create_state(
    p_state         VARCHAR(128),
    p_provider      VARCHAR(32),
    p_code_verifier VARCHAR(256),
    p_redirect_uri  TEXT
)
RETURNS TIMESTAMPTZ
LANGUAGE plpgsql
AS $$
DECLARE
    v_expires_at TIMESTAMPTZ := clock_timestamp() + INTERVAL '10 minutes';
BEGIN
    INSERT INTO oauth_states (state, provider, code_verifier, redirect_uri, expires_at, consumed_at)
    VALUES (p_state, p_provider, p_code_verifier, p_redirect_uri, v_expires_at, NULL);

    RETURN v_expires_at;
END;
$$;

CREATE OR REPLACE FUNCTION fn_oauth_consume_state(
    p_state    VARCHAR(128),
    p_provider VARCHAR(32)
)
RETURNS TABLE (
    is_valid      BOOLEAN,
    code_verifier VARCHAR(256),
    redirect_uri  TEXT
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_verifier VARCHAR(256);
    v_redirect TEXT;
BEGIN
    UPDATE oauth_states os
       SET consumed_at = clock_timestamp()
     WHERE os.state = p_state
       AND os.provider = p_provider
       AND os.consumed_at IS NULL
       AND os.expires_at > clock_timestamp()
    RETURNING os.code_verifier, os.redirect_uri
         INTO v_verifier, v_redirect;

    IF NOT FOUND THEN
        RETURN QUERY SELECT FALSE, NULL::VARCHAR(256), NULL::TEXT;
    ELSE
        RETURN QUERY SELECT TRUE, v_verifier, v_redirect;
    END IF;
END;
$$;


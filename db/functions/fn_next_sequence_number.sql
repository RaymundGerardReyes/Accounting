CREATE TABLE IF NOT EXISTS sequence_counters (
    prefix      VARCHAR(64) PRIMARY KEY,
    current_val BIGINT NOT NULL DEFAULT 0,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE OR REPLACE FUNCTION fn_next_sequence_number(
    p_prefix VARCHAR(64),
    p_pad    INT DEFAULT 4
)
RETURNS VARCHAR(128)
LANGUAGE plpgsql
AS $$
DECLARE
    v_next BIGINT;
BEGIN
    INSERT INTO sequence_counters (prefix, current_val, updated_at)
    VALUES (p_prefix, 1, clock_timestamp())
    ON CONFLICT (prefix)
    DO UPDATE SET
        current_val = sequence_counters.current_val + 1,
        updated_at  = clock_timestamp()
    RETURNING current_val INTO v_next;

    RETURN p_prefix || '-' || lpad(v_next::TEXT, GREATEST(p_pad, 1), '0');
END;
$$;

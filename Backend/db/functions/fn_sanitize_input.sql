CREATE OR REPLACE FUNCTION fn_sanitize_input(p_input TEXT)
RETURNS TEXT
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    v_cleaned TEXT;
BEGIN
    IF p_input IS NULL THEN
        RETURN NULL;
    END IF;

    v_cleaned := btrim(p_input);

    -- Neutralize CSV / Spreadsheet formula injection prefixes
    IF left(v_cleaned, 1) IN ('=', '+', '-', '@', E'\t', E'\r') THEN
        v_cleaned := '''' || v_cleaned;
    END IF;

    RETURN v_cleaned;
END;
$$;


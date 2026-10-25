CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE OR REPLACE FUNCTION fn_append_audit_event(
    p_actor_id       VARCHAR(64),
    p_actor_role     VARCHAR(128),
    p_action         VARCHAR(128),
    p_entity_type    VARCHAR(64),
    p_entity_id      VARCHAR(64),
    p_previous_state TEXT,
    p_new_state      TEXT,
    p_reason         TEXT,
    p_correlation_id VARCHAR(64),
    p_evidence_ref   VARCHAR(64) DEFAULT NULL
)
RETURNS TABLE (
    sequence_no BIGINT,
    prev_hash   VARCHAR(64),
    record_hash VARCHAR(64),
    occurred_at TIMESTAMPTZ
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_next_seq    BIGINT;
    v_prev_hash   VARCHAR(64);
    v_occurred_at TIMESTAMPTZ := clock_timestamp();
    v_payload     TEXT;
    v_record_hash VARCHAR(64);
BEGIN
    -- Acquire transaction-level advisory lock to guarantee strict monotonic serialization
    PERFORM pg_advisory_xact_lock(904210);

    SELECT COALESCE(MAX(ae.sequence_no), 0) + 1
      INTO v_next_seq
      FROM audit_events ae;

    IF v_next_seq = 1 THEN
        v_prev_hash := repeat('0', 64);
    ELSE
        SELECT ae.record_hash
          INTO v_prev_hash
          FROM audit_events ae
         WHERE ae.sequence_no = v_next_seq - 1;
    END IF;

    v_payload := concat_ws('|',
        v_next_seq::TEXT,
        COALESCE(p_actor_id, ''),
        COALESCE(p_actor_role, ''),
        COALESCE(p_action, ''),
        COALESCE(p_entity_type, ''),
        COALESCE(p_entity_id, ''),
        COALESCE(p_previous_state, ''),
        COALESCE(p_new_state, ''),
        COALESCE(p_reason, ''),
        COALESCE(p_correlation_id, ''),
        COALESCE(p_evidence_ref, ''),
        to_char(v_occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        v_prev_hash
    );

    v_record_hash := encode(digest(v_payload, 'sha256'), 'hex');

    INSERT INTO audit_events (
        sequence_no, actor_id, actor_role, action,
        entity_type, entity_id, previous_state, new_state,
        reason, correlation_id, evidence_ref, occurred_at,
        prev_hash, record_hash
    )
    VALUES (
        v_next_seq, p_actor_id, p_actor_role, p_action,
        p_entity_type, p_entity_id, p_previous_state, p_new_state,
        p_reason, p_correlation_id, p_evidence_ref, v_occurred_at,
        v_prev_hash, v_record_hash
    );

    RETURN QUERY SELECT v_next_seq, v_prev_hash, v_record_hash, v_occurred_at;
END;
$$;


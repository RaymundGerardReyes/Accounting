CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- -----------------------------------------------------------------------------
-- 1. Fast Session Resolution & Token Hash Verification (Index-Only Scan)
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_resolve_session_actor(
    p_token_hash VARCHAR(64)
)
RETURNS TABLE (
    session_id          VARCHAR(36),
    user_id             VARCHAR(64),
    employee_code       VARCHAR(64),
    full_name           VARCHAR(128),
    email               VARCHAR(128),
    department_id       VARCHAR(64),
    cost_center_id      VARCHAR(32),
    effective_roles     TEXT[],
    effective_scope     VARCHAR(64),
    expires_at          TIMESTAMPTZ
)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
BEGIN
    RETURN QUERY
    SELECT
        s.id AS session_id,
        u.id AS user_id,
        u.employee_code,
        u.full_name,
        u.email,
        u.department_id,
        u.cost_center_id,
        CASE
            WHEN s.act_as_role IS NOT NULL THEN ARRAY[s.act_as_role::TEXT]
            ELSE COALESCE(array_agg(ra.role::TEXT) FILTER (WHERE ra.role IS NOT NULL), ARRAY[]::TEXT[])
        END AS effective_roles,
        COALESCE(
            s.act_as_department_scope,
            CASE WHEN bool_or(ra.department_scope = '*') THEN '*' ELSE u.department_id END
        )::VARCHAR(64) AS effective_scope,
        s.expires_at
    FROM public.user_sessions s
    INNER JOIN public.users_finance u
        ON u.id = s.user_id
       AND u.status = 'active'
    LEFT JOIN public.role_assignments ra
        ON ra.user_id = u.id
       AND ra.is_active = 1
    WHERE s.token_hash = p_token_hash
      AND s.revoked_at IS NULL
      AND s.expires_at > clock_timestamp()
    GROUP BY
        s.id, u.id, u.employee_code, u.full_name, u.email,
        u.department_id, u.cost_center_id, s.act_as_role,
        s.act_as_department_scope, s.expires_at;
END;
$$;

-- -----------------------------------------------------------------------------
-- 2. Server-Side RBAC, Department Scope, Approval Matrix & Four-Eyes SoD Guard
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_enforce_sod_and_approval_limit(
    p_actor_id             VARCHAR(64),
    p_requestor_id         VARCHAR(64),
    p_workflow             VARCHAR(64),
    p_stage                VARCHAR(64),
    p_target_department_id VARCHAR(64),
    p_amount_minor         BIGINT
)
RETURNS TABLE (
    is_authorized  BOOLEAN,
    error_code     VARCHAR(64),
    error_message  TEXT
)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_actor_status VARCHAR(32);
    v_matched_role VARCHAR(64);
BEGIN
    -- 1. Four-Eyes Separation of Duties check
    IF p_requestor_id IS NOT NULL AND LOWER(btrim(p_actor_id)) = LOWER(btrim(p_requestor_id)) THEN
        RETURN QUERY SELECT
            FALSE,
            'Finance.SoD.SelfApprovalForbidden'::VARCHAR(64),
            format('Actor "%s" cannot approve or verify their own request.', p_actor_id);
        RETURN;
    END IF;

    -- 2. Verify Actor is active
    SELECT u.status INTO v_actor_status
      FROM public.users_finance u
     WHERE u.id = p_actor_id;

    IF NOT FOUND OR v_actor_status <> 'active' THEN
        RETURN QUERY SELECT
            FALSE,
            'Security.ActorInactive'::VARCHAR(64),
            format('Actor "%s" is not found or not active.', p_actor_id);
        RETURN;
    END IF;

    -- 3. Verify Role Assignment, Department Scope, and Approval Matrix Limit in one indexed join
    SELECT ra.role INTO v_matched_role
      FROM public.role_assignments ra
      INNER JOIN public.approval_matrix am
          ON am.role = ra.role
         AND am.workflow = p_workflow
         AND am.stage = p_stage
     WHERE ra.user_id = p_actor_id
       AND ra.is_active = 1
       AND (ra.department_scope = '*' OR LOWER(ra.department_scope) = LOWER(p_target_department_id))
       AND p_amount_minor BETWEEN am.min_amount_minor AND am.max_amount_minor
     LIMIT 1;

    IF NOT FOUND THEN
        RETURN QUERY SELECT
            FALSE,
            'Security.InsufficientApprovalAuthority'::VARCHAR(64),
            format('Actor "%s" lacks role, department scope (%s), or monetary authority (%s centavos) for %s/%s.',
                   p_actor_id, p_target_department_id, p_amount_minor, p_workflow, p_stage);
        RETURN;
    END IF;

    RETURN QUERY SELECT TRUE, NULL::VARCHAR(64), NULL::TEXT;
END;
$$;

-- -----------------------------------------------------------------------------
-- 3. Single-Round-Trip Atomic PayMongo Webhook + Outbox Stored Function (< 5ms)
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_ingest_paymongo_webhook_atomic(
    p_webhook_id          VARCHAR(36),
    p_provider            VARCHAR(32),
    p_provider_event_id   VARCHAR(128),
    p_event_type          VARCHAR(128),
    p_raw_payload         TEXT,
    p_payment_attempt_id  VARCHAR(36),
    p_provider_payment_id VARCHAR(128),
    p_actual_method       VARCHAR(32),
    p_outbox_id           VARCHAR(36),
    p_correlation_id      VARCHAR(64)
)
RETURNS TABLE (
    outcome_status     VARCHAR(32),
    duplicate_ignored  BOOLEAN,
    resolved_order_id  VARCHAR(36),
    resolved_attempt_id VARCHAR(36)
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_inserted_webhook_id VARCHAR(36);
    v_attempt_id          VARCHAR(36);
    v_order_id            VARCHAR(36);
    v_current_status      VARCHAR(32);
    v_order_number        VARCHAR(64);
    v_order_title         VARCHAR(255);
    v_amount_minor        BIGINT;
    v_currency            VARCHAR(3);
    v_metadata_json       TEXT;
    v_now                 TIMESTAMPTZ := clock_timestamp();
    v_outbox_payload      TEXT;
BEGIN
    -- Step 1: Atomic replay guard via UNIQUE (provider, provider_event_id)
    INSERT INTO public.webhook_events (
        id, provider, provider_event_id, event_type,
        processing_status, payload, received_at
    )
    VALUES (
        p_webhook_id, p_provider, p_provider_event_id, p_event_type,
        'received', p_raw_payload, v_now
    )
    ON CONFLICT (provider, provider_event_id) DO NOTHING
    RETURNING id INTO v_inserted_webhook_id;

    IF v_inserted_webhook_id IS NULL THEN
        RETURN QUERY SELECT 'ignored_duplicate'::VARCHAR(32), TRUE, NULL::VARCHAR(36), NULL::VARCHAR(36);
        RETURN;
    END IF;

    -- Step 2: Locate PaymentAttempt via indexed PK or provider_payment_id
    SELECT pa.id, pa.order_id, pa.status
      INTO v_attempt_id, v_order_id, v_current_status
      FROM public.payment_attempts pa
     WHERE (p_payment_attempt_id IS NOT NULL AND pa.id = p_payment_attempt_id)
        OR (p_provider_payment_id IS NOT NULL AND pa.provider_payment_id = p_provider_payment_id)
     ORDER BY pa.created_at DESC
     LIMIT 1
     FOR UPDATE;

    IF NOT FOUND THEN
        UPDATE public.webhook_events
           SET processing_status = 'ignored', processed_at = v_now
         WHERE id = p_webhook_id;

        RETURN QUERY SELECT 'ignored_unmatched'::VARCHAR(32), FALSE, NULL::VARCHAR(36), NULL::VARCHAR(36);
        RETURN;
    END IF;

    -- Step 3: Enforce Terminal State Immutability
    IF v_current_status IN ('succeeded', 'failed', 'cancelled', 'expired') THEN
        IF p_event_type ILIKE '%paid%' AND v_current_status <> 'succeeded' THEN
            UPDATE public.payment_attempts
               SET status = 'duplicate_paid_review', updated_at = v_now
             WHERE id = v_attempt_id;
        END IF;

        UPDATE public.webhook_events
           SET processing_status = 'ignored', processed_at = v_now
         WHERE id = p_webhook_id;

        RETURN QUERY SELECT 'ignored_terminal'::VARCHAR(32), FALSE, v_order_id, v_attempt_id;
        RETURN;
    END IF;

    -- Step 4: Transition PaymentAttempt, Order, and Enqueue Transactional Outbox Atomically
    IF p_event_type ILIKE '%paid%' OR p_event_type ILIKE '%succeeded%' THEN
        UPDATE public.payment_attempts
           SET status         = 'succeeded',
               payment_method = COALESCE(NULLIF(p_actual_method, ''), payment_method),
               paid_at        = COALESCE(paid_at, v_now),
               updated_at     = v_now
         WHERE id = v_attempt_id;

        UPDATE public.orders
           SET status     = 'paid',
               updated_at = v_now
         WHERE id = v_order_id
        RETURNING order_number, title, amount_minor, currency, metadata_json
             INTO v_order_number, v_order_title, v_amount_minor, v_currency, v_metadata_json;

        v_outbox_payload := json_build_object(
            'order_id', v_order_id,
            'order_number', v_order_number,
            'payment_attempt_id', v_attempt_id,
            'amount_minor', v_amount_minor,
            'currency', v_currency,
            'description', v_order_title,
            'payment_method', COALESCE(NULLIF(p_actual_method, ''), 'paymongo')
        )::TEXT;

        INSERT INTO public.fulfillment_outbox (
            id, order_id, payment_attempt_id, event_type,
            payload_json, correlation_id, status, created_at
        )
        VALUES (
            p_outbox_id, v_order_id, v_attempt_id, 'order.paid',
            v_outbox_payload, p_correlation_id, 'pending', v_now
        );
    ELSIF p_event_type ILIKE '%failed%' THEN
        UPDATE public.payment_attempts
           SET status = 'failed', failure_code = 'GATEWAY_DECLINED', updated_at = v_now
         WHERE id = v_attempt_id;
    ELSIF p_event_type ILIKE '%expired%' THEN
        UPDATE public.payment_attempts
           SET status = 'expired', failure_code = 'SESSION_EXPIRED', updated_at = v_now
         WHERE id = v_attempt_id;
    END IF;

    UPDATE public.webhook_events
       SET processing_status = 'processed', processed_at = v_now
     WHERE id = p_webhook_id;

    RETURN QUERY SELECT 'processed'::VARCHAR(32), FALSE, v_order_id, v_attempt_id;
END;
$$;

-- -----------------------------------------------------------------------------
-- 4. Set-Based In-Database Audit Hash-Chain Verifier (Window Function LAG)
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION fn_verify_audit_hash_chain()
RETURNS TABLE (
    is_intact              BOOLEAN,
    total_verified_records BIGINT,
    first_broken_seq_no    BIGINT,
    failure_reason         TEXT
)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, public
AS $$
DECLARE
    v_total       BIGINT;
    v_broken_seq  BIGINT;
    v_broken_msg  TEXT;
BEGIN
    SELECT COUNT(*) INTO v_total FROM public.audit_events;

    IF v_total = 0 THEN
        RETURN QUERY SELECT TRUE, 0::BIGINT, NULL::BIGINT, NULL::TEXT;
        RETURN;
    END IF;

    WITH ordered_chain AS (
        SELECT
            ae.sequence_no,
            ae.prev_hash,
            ae.record_hash,
            LAG(ae.sequence_no, 1, 0::BIGINT) OVER (ORDER BY ae.sequence_no ASC) AS expected_prev_seq,
            LAG(ae.record_hash, 1, repeat('0', 64)::VARCHAR(64)) OVER (ORDER BY ae.sequence_no ASC) AS expected_prev_hash,
            encode(
                public.digest(
                    concat_ws('|',
                        ae.sequence_no::TEXT,
                        COALESCE(ae.actor_id, ''),
                        COALESCE(ae.actor_role, ''),
                        COALESCE(ae.action, ''),
                        COALESCE(ae.entity_type, ''),
                        COALESCE(ae.entity_id, ''),
                        COALESCE(ae.previous_state, '{}'),
                        COALESCE(ae.new_state, '{}'),
                        COALESCE(ae.reason, ''),
                        COALESCE(ae.correlation_id, ''),
                        COALESCE(ae.evidence_ref, ''),
                        to_char(ae.occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                        ae.prev_hash
                    ),
                    'sha256'
                ),
                'hex'
            ) AS recomputed_hash
        FROM public.audit_events ae
    )
    SELECT
        oc.sequence_no,
        CASE
            WHEN oc.sequence_no <> oc.expected_prev_seq + 1
                THEN format('Sequence gap detected at sequence_no %s (expected %s).', oc.sequence_no, oc.expected_prev_seq + 1)
            WHEN oc.prev_hash <> oc.expected_prev_hash
                THEN format('prev_hash link broken at sequence_no %s.', oc.sequence_no)
            ELSE format('record_hash payload tampering detected at sequence_no %s.', oc.sequence_no)
        END
    INTO v_broken_seq, v_broken_msg
    FROM ordered_chain oc
    WHERE oc.sequence_no <> oc.expected_prev_seq + 1
       OR oc.prev_hash <> oc.expected_prev_hash
       OR oc.record_hash <> oc.recomputed_hash
    ORDER BY oc.sequence_no ASC
    LIMIT 1;

    IF v_broken_seq IS NOT NULL THEN
        RETURN QUERY SELECT FALSE, (v_broken_seq - 1), v_broken_seq, v_broken_msg;
    ELSE
        RETURN QUERY SELECT TRUE, v_total, NULL::BIGINT, NULL::TEXT;
    END IF;
END;
$$;


# Runbook: Webhook Ingestion & Transactional Outbox Replay

## 1. Problem Description & Safeguards
During a production incident or application rollback, two failure modes can occur:
1. **Missed or Failed Webhooks**: PayMongo sent `payment.paid` or `payment.failed` webhook callbacks that failed with HTTP 500 or timed out.
2. **Stalled Transactional Outbox**: Webhooks were accepted and persisted in `webhook_events`, but the background worker (`OutboxWorker` in .NET or Celery in Python) crashed, leaving events stuck in `pending` or `failed` status in `fulfillment_outbox`.

> [!IMPORTANT]
> **Strict Idempotency Invariant**:
> Both `webhook_events.event_id` and `fulfillment_outbox.idempotency_key` enforce unique constraints. Replaying an event MUST NOT produce duplicate charges, duplicate ledger entries, or duplicate BIR Official Receipts.

---

## 2. Diagnosing Webhook & Outbox Status

### Step 2.1: Inspect Outbox Dead-Letter / Failed Records
```sql
-- Query stalled or failed outbox events from the last 24 hours
SELECT 
    id,
    event_type,
    status,
    retry_count,
    last_error,
    created_at,
    next_retry_at
FROM fulfillment_outbox
WHERE status IN ('failed', 'stalled')
  AND created_at >= NOW() - INTERVAL '24 hours'
ORDER BY created_at DESC;
```

### Step 2.2: Inspect PayMongo Webhook Delivery Status
Log in to the **PayMongo Dashboard** $\rightarrow$ **Developers** $\rightarrow$ **Webhooks**:
- Review failed delivery attempts for endpoint `https://api.rge.internal/api/webhooks/paymongo`.
- Note event IDs with HTTP 500 responses.

---

## 3. Safe Outbox Replay Procedure

### Step 3.1: Reset Failed Outbox Items for Reprocessing
Once the bug in the outbox handler has been resolved and deployed:

```sql
-- Reset outbox entries back to 'pending' state
BEGIN;

UPDATE fulfillment_outbox
SET 
    status = 'pending',
    retry_count = 0,
    next_retry_at = NOW(),
    last_error = NULL
WHERE status = 'failed'
  AND created_at >= '2026-10-25 00:00:00+00';

-- Commit transaction
COMMIT;
```

### Step 3.2: Monitor Outbox Worker Consumption
Watch the active outbox worker process the backlog:
```bash
# Monitor container worker logs
docker logs -f rge-accounting-dotnet | grep -E "OutboxDispatcher|Fulfillment"
```

Verify that counts decrease to zero:
```sql
SELECT status, count(*) 
FROM fulfillment_outbox 
GROUP BY status;
```

---

## 4. PayMongo Webhook Redelivery (External Replay)

If PayMongo marked the event as permanently failed:
1. Navigate to the failed event in the PayMongo dashboard.
2. Click **Resend Webhook**.
3. Confirm HTTP 200 response.
4. Verify in database:
   ```sql
   SELECT id, status, payload->'data'->'id' as payment_id 
   FROM webhook_events 
   WHERE event_id = '<PAYMONGO_EVENT_ID>';
   ```

---

## 5. Audit Trail Verification
Confirm that every replayed event produced an immutable, cryptographically chained audit record via `fn_append_audit_event`:

```sql
SELECT 
    sequence_number,
    action,
    entity_name,
    entity_id,
    previous_hash,
    current_hash,
    created_at
FROM audit_trail
WHERE created_at >= NOW() - INTERVAL '1 hour'
ORDER BY sequence_number DESC
LIMIT 10;
```
Verify that `previous_hash` matches the preceding record's `current_hash`.


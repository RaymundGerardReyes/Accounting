---
description: Strict architectural guardrails for E:\Accounting legacy compatibility and live payment orchestration
---

# Accounting & Payment Orchestration Guardrails

1. **Legacy API & UI Preservation**:
   - Keep all legacy Cost Analysis endpoints (`/api/dashboard`, `/api/department/{slug}`, `/api/products`, `/api/variance`, `/api/configuration`) operational on port `5000`.
   - Maintain `₱` (PHP) currency formatting across all Frontend components (`Dashboard`, `DepartmentDetail`, `ProductLineAnalysis`, `VarianceAnalysis`, `Configuration`, `PaymentsHub`).

2. **Payment Orchestration Invariants**:
   - **Frontend Initiates, Backend Orchestrates, Gateway Confirms**: Never mark an order as paid based on a browser redirect (`/payment/return`). Only signed webhooks (`/api/v1/webhooks/paymongo`) or backend reconciliation (`POST /api/v1/admin/payments/{id}/reconcile`) may transition a payment to `succeeded`.
   - **Authoritative Server-Side Pricing**: Always calculate `amount_minor` (in PHP centavos, `BIGINT > 0`) on the backend from trusted product or department records.
   - **Terminal State Immutability**: All terminal states (`succeeded`, `failed`, `expired`, `cancelled`) are strictly immutable. Out-of-order webhooks, late declines, or reconciliation attempts on terminal records are idempotent no-ops and must never mutate state.
   - **Comprehensive Failure & Expiration Event Mapping**: PayMongo webhook handlers must accept all payment failure events (`payment.failed`, `checkout_session.payment.failed`, `payment_intent.payment_failed`, `source.failed`), expiration events (`checkout_session.expired`), and cancellation events (`payment.canceled`, `checkout_session.cancelled`, `payment_intent.cancelled`), populating structured failure codes (`SESSION_EXPIRED`, `CARD_DECLINED`, etc.) and failure messages.
   - **Session Precedence in Reconciliation**: When reconciling against PayMongo's checkout session API, check for any successful payment first (`paid`). If none succeeded, prioritize top-level session status (`expired` / `cancelled`) over older failed card attempts.
   - **Parent Order Recovery**: An order whose latest attempt failed, expired, or was cancelled must remain in `pending` status so the customer can retry with a new payment attempt. The UI must present a recovery action ("Retry Payment" / "Start New Checkout").
   - **Timeout Safety**: Never auto-retry a charge on a gateway timeout; mark the attempt as `unknown` and reconcile first.
   - **Transactional Outbox Resilience**: Outbox processors must support explicit retries of failed entries (`?retry_failed=true`), ensuring asynchronous fulfillment and customer receipt delivery succeed even after temporary email or file I/O outages.


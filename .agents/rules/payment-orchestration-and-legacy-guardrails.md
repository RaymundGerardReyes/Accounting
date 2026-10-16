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
   - **Monotonic Terminal States**: Once a payment attempt reaches `succeeded`, late or out-of-order webhooks must never revert it to `pending`, `processing`, or `failed`.
   - **Timeout Safety**: Never auto-retry a charge on a gateway timeout; mark the attempt as `unknown` and reconcile first.


---
description: CPA-grade control invariants for any finance workflow added to E:\Accounting
---

# Finance Workflow Control Invariants

1. **Identity**: roles, limits and SoD come from server-side DB records; never from client headers or `configuration.json` (its write routes are unauthenticated).
2. **Money**: integer centavos (`BigInteger`) or `Decimal`; never `float`. Derived balances are computed from append-only ledger entries, not stored.
3. **Statuses**: keep approval, payment, review, posting and closure as separate state machines; check persisted state before each transition.
4. **Evidence over clicks**: approval is not release; release is not settlement; zero balance is not closure.
5. **Separation of duties**: requestor, approver, maker, verifier, bank approver and evidence verifier are distinct people per transaction.
6. **Audit**: financial audit is an append-only DB table; `configuration.json` auditLogs is not an audit trail.
7. **Gateways**: PayMongo Checkout here is inbound collection only; never describe it as a disbursement/payout rail.
8. **Seeds**: no pre-verified vendors, bank data or financial records outside `FINANCE_SEED=dev`.
9. **Policy**: business parameters are versioned configuration; unresolved policy is flagged, never invented.
10. **Review habit**: before implementing a finance spec, list logic conflicts and CPA risks and confirm decisions with the user.
11. **UI Role Awareness & Dev Identity Persistence**: In development mode (`FINANCE_AUTH_MODE=dev`), persist the active actor identity in browser `localStorage` so role context survives Fast Refresh and page reloads. Financial action buttons (e.g. Verify Vendor, Approve Advance, Release Disbursement) must reflect the active actor's RBAC permissions in the UI, disabling the button and displaying guidance text when the current actor lacks the requisite role or violates Segregation of Duties (SoD).
12. **Google Sheets Standard 18-Column Schema Invariant**: Both `FormBuilder.gs` and `Code.gs` must strictly adhere to the standardized 18-column schema: `Timestamp`, `Customer Name`, `Email Address`, `Contact Number`, `Book / Material`, `Quantity`, `Instructor Credit`, `Student ID`, `Notes`, `Response ID`, `Order Number`, `PayMongo Checkout Link`, `Amount`, `Payment Status`, `Payment Attempt ID`, `Official Receipt No`, `Webhook Outcome Details`, `Synchronized At`. Sheet column matching must maintain alias dictionaries (`COLUMN_ALIASES`) mapping Google Form question titles to standardized business headers to prevent duplicate columns. Submissions must retain customer contact, order items, and timestamps even when encountering error diagnostics.


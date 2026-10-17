# Frontend Finance Operations Guide

This guide details the Next.js / React 19 Frontend architecture, UI components, client-side API layer, and user interaction flows for the Finance Operations workspace in Acme Cost & Payment Ledger.

---

## 1. Architectural Overview

The Finance Operations frontend interface provides a unified, auditable workspace for employees, supervisors, finance reviewers, disbursement makers, bank authorizers, and the finance controller.

- **Client Technology**: Next.js 15.2.4 (App Router), React 19, Tailwind CSS, Lucide React, shadcn/ui.
- **Location in Application**:
  - Accessible via the **"Finance Operations"** item in the main sidebar ([`Frontend/components/sidebar.tsx`](file:///e:/Accounting/Frontend/components/sidebar.tsx)).
  - Dynamically mounted in [`Frontend/app/page.tsx`](file:///e:/Accounting/Frontend/app/page.tsx) via `next/dynamic` (`FinanceOperationsHub`).

---

## 2. Source Files

### `Frontend/lib/finance-api.ts`
- **Path**: [`Frontend/lib/finance-api.ts`](file:///e:/Accounting/Frontend/lib/finance-api.ts)
- **Role**: Type-safe REST client for all `/api/v1/finance/*` backend endpoints.
- **Key Invariants**:
  - `X-User-Id` request header transmits the server-resolved actor context in dev mode.
  - Handles JSON decoding, structured domain error parsing (`detail.message` or `detail`), and formula-safe CSV blob downloads.
  - Provides `formatPhpMinor(minor: number)` to format integer centavos as localized PHP currency (`₱XX,XXX.XX`).
- **Core API Functions**:
  - **Auth & Matrix**: `fetchFinanceMe()`, `switchDevActor()`
  - **Policy & Exceptions**: `fetchPolicies()`, `createPolicyVersion()`, `submitPolicyVersion()`, `approvePolicyVersion()`, `retirePolicyVersion()`, `fetchExceptionRequests()`, `createExceptionRequest()`, `decideExceptionRequest()`
  - **Document Evidence**: `uploadDocumentJson()`, `uploadDocumentEvidence()`
  - **Cash Advances & SoD**: `fetchCashAdvances()`, `createCashAdvance()`, `submitCashAdvance()`, `decideCashAdvance()`, `createDisbursement()`, `verifyDisbursement()`, `approveDisbursementBankRelease()`, `executeDisbursement()`, `verifyDisbursementEvidence()`, `recordCashReturn()`, `closeCashAdvance()`
  - **Expense Reports & Liquidation**: `fetchExpenseReports()`, `createExpenseReport()`, `addExpenseLine()`, `evaluateExpenseReport()`, `submitExpenseReport()`, `reviewExpenseReport()`
  - **Purchase-to-Pay**: `fetchVendors()`, `createVendor()`, `verifyVendor()`, `fetchTaxCodes()`, `createTaxCode()`, `fetchPurchaseRequests()`, `createPurchaseRequest()`, `submitPurchaseRequest()`, `decidePurchaseRequest()`, `fetchAccountsPayable()`, `validateAccountPayable()`, `preparePayablePayment()`, `verifyPayablePayment()`, `approvePayablePaymentBank()`, `executePayablePayment()`, `reconcilePayablePayment()`
  - **Reporting & Audit**: `fetchFinanceDashboard()`, `fetchCentralizedTracker()`, `downloadTrackerCsv()`, `fetchExceptionRegister()`, `resolveExceptionRegisterItem()`, `fetchAuditEvents()`

---

### `Frontend/components/finance-operations-hub.tsx`
- **Path**: [`Frontend/components/finance-operations-hub.tsx`](file:///e:/Accounting/Frontend/components/finance-operations-hub.tsx)
- **Role**: Primary tabbed workspace component.
- **Major Subsystems**:

#### 1. Identity & Dev Mode Act-As Switcher
- Displays current user profile, avatar, department, cost center, staff type (`field` vs `non_field`), assigned roles, and approval limits.
- Features an interactive dropdown enabling instant switching between registered dev actors (`emp_field_01`, `emp_field_02`, `sup_approver_01`, `fin_reviewer_01`, `fin_maker_01`, `fin_verifier_01`, `bank_approver_01`, `evidence_verifier_01`, `fin_controller_01`, `auditor_01`, `sys_admin_01`).
- All actor switches are audit-logged in `audit_events`.

#### 2. Dashboard & KPIs Tab
- Real-time KPI summary cards:
  - Aggregate Open Advances Balance & Unliquidated Count.
  - Overdue Advances Aging (>30 days) and automated advance block indicator.
  - Accounts Payable Due and Tax Review Required count.
  - SHA-256 Hash Chain Security badge verifying tamper-free append-only audit records.
- Interactive Operational Exception Register: Displays shortfalls, disallowances, and resolution modal.

#### 3. Cash Advances & 6-Person SoD Tab
- Lists all employee cash advances with independent approval, payment, liquidation review, and settlement states.
- Modal for requesting new advances with date needed, purpose, and destination site.
- **6-Person Controlled Disbursement Drawer**:
  - Maker prepares disbursement amount and bank account reference.
  - Independent Verifier confirms payee and banking information.
  - Bank Approver authorizes disbursement release within matrix limits.
  - Execution records manual bank reference and uploads remittance advice PDF.
  - Evidence Verifier confirms remittance PDF to finalize release to employee advance ledger.
- Cash Return modal to record evidenced employee cash deposits.
- Controller Final Closure action with automated zero-balance checks.

#### 4. Expense Reports & Liquidation Tab
- Decoupled monthly expense reporting table with period filter.
- Creation modal with consolidated OR receipt PDF and out-of-base MFAR uploads.
- Line items editor with categorized expenses, merchants, and destinations.
- **Live Dry-Run Policy Evaluation Engine**:
  - Runs client-triggered preview against active policy rules.
  - Visual flags for the PHP 15,000 monthly aggregate cap, 1 report per month rule, missing PDFs, and exception coverage.
- Finance Reviewer line-by-line review:
  - Partial approval / line rejection with explicit reasons.
  - Disallowed amounts remain as receivables due from employee.
  - Automatically settles approved expenses oldest-first against open advances.

#### 5. Purchase-to-Pay (P2P) Tab
- Vendor registry with verification badges and KYC notes.
- Purchase Requests table with supervisor and controller approval actions.
- Accounts Payable validator:
  - 3-way matching against approved PR.
  - Automated duplicate invoice normalized detection.
  - Configurable VAT (12%) and EWT (1% / 2%) computations.
- AP disbursement maker, verifier, and bank approver workflows.

#### 6. Policies & Exceptions Tab
- Active policy versions viewer showing aggregate caps, submission deadlines, and SLA business days.
- Version history with draft authoring modal (enforcing author != approver).
- Policy Exception Request form and Controller review decision dialog.

#### 7. Centralized Tracker & Audit Tab
- Unified multi-workflow searchable table across Advances, Reports, PRs, and Payables.
- Filter by workflow, status, and free text search.
- One-click button to download formula-safe, Excel-compatible UTF-8 BOM CSV.


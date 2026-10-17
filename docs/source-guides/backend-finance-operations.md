# Backend Finance Operations Guide

This guide details the CPA-audited Finance Operations backend architecture, domain models, policy evaluation engine, workflow services, API routes, and separation-of-duties (SoD) controls implemented in the Acme Cost Accounting platform.

---

## 1. Architectural Principles & Control Invariants

The Finance Operations module conforms to 10 non-negotiable financial control invariants:

1. **Server-Bound Identity & RBAC**: Roles, approval limits, and separation-of-duties constraints are loaded exclusively from backend database tables (`users_finance`, `role_assignments`, `approval_matrix`). Client-sent role headers and unauthenticated configurations are never trusted. Dev role-switching (`/api/v1/finance/auth/act-as`) is restricted to dev mode and is audit-logged.
2. **Integer Centavo Precision**: All monetary values are represented and stored as integer centavos (`BigInteger`) or exact Decimal arithmetic (e.g., PHP 15,000.00 is represented as `1500000`). Floating-point arithmetic on monetary figures is prohibited.
3. **Derived Ledgers**: Balances are calculated dynamically by aggregating append-only ledger transactions (`advance_ledger_entries`, `payment_records`) rather than relying on mutable stored balance columns.
4. **Decoupled Liquidation**: Monthly `ExpenseReport` submissions are decoupled from individual `CashAdvance` records. Approved expense lines are applied oldest-first across open advances via `SettlementApplication`.
5. **No Auto-Write-Offs**: Disallowed or rejected expense shortfall amounts remain on the employee advance as an explicit receivable ("shortfall_receivable") with automated tracking in `exception_registers`.
6. **Controlled Bank Disbursement (6-Person SoD)**: Payouts require six distinct actors across the transaction lifecycle:
   - Requestor / Advance Applicant
   - Business Approver (Supervisor / Controller)
   - Disbursement Maker (`fin_maker_01`)
   - Disbursement Verifier (`fin_verifier_01`)
   - Bank Authorizer (`bank_approver_01`)
   - Evidence Verifier (`evidence_verifier_01`)
7. **Inbound PayMongo Separation**: PayMongo Checkout is strictly an inbound collections gateway. Outbound disbursements are executed via controlled manual bank records with verified remittance evidence.
8. **Dev-Only Vendor Seeding**: Vendor master records are created in `pending_verification` status without bank account data. Maker/Creator cannot verify their own vendor records.
9. **Versioned Policy Engine**: Business parameters (e.g., PHP 15,000 monthly cap, 1 report per month) are stored as versioned policies (`FinancialPolicy`, `PolicyVersion`, `PolicyRule`). An author cannot self-approve a policy version.
10. **Append-Only Cryptographic Audit Trail**: All financial operations log SHA-256 hash-chained records in `audit_events`.

---

## 2. Source Code Components

### `Backend/finance_models.py`
- **Path**: [`Backend/finance_models.py`](file:///e:/Accounting/Backend/finance_models.py)
- **Role**: Declarative SQLAlchemy models for finance operations.
- **Core Entities**:
  - `FinanceUser`: Employee master with staff type (`field` vs `non_field`), department, and cost center.
  - `RoleAssignment`: Server-bound role assignments (`employee`, `supervisor`, `finance_reviewer`, `maker`, `verifier`, `bank_approver`, `evidence_verifier`, `finance_controller`, `auditor`, `system_admin`).
  - `ApprovalMatrix`: Amount thresholds and workflow stage permissions.
  - `FinancialPolicy`, `PolicyVersion`, `PolicyRule`: Centrally managed versioned policy configuration.
  - `ExceptionRequest`: Transaction-scoped policy exception authorizations.
  - `CashAdvance`: Multi-status advance records with separate approval, payment, review, settlement, and posting status chains.
  - `Disbursement`: Controlled bank disbursement with maker, verifier, bank approver, and evidence verifier references.
  - `AdvanceLedgerEntry`: Append-only debit/credit ledger tracking advance disbursements, expense settlements, and cash returns.
  - `ExpenseReport` & `ExpenseLine`: Monthly field expense reports with PDF evidence linkages (`DocumentReference`).
  - `SettlementApplication`: Audit-logged link applying approved expense lines to open advances oldest-first.
  - `Vendor` & `VendorBankChange`: Vendor master with independent verification and bank account change authorization.
  - `TaxCode`: VAT and withholding tax configurations with CPA approval flags.
  - `PurchaseRequest` & `PurchaseRequestLine`: P2P procurement requests.
  - `AccountPayable`: Supplier invoices with normalized duplicate prevention and automated VAT/EWT computation.
  - `PaymentRecord`: Partial and full disbursement tracking against accounts payable.
  - `ExceptionRegister`: Operational tracking of control exceptions, disallowance shortfalls, and SLA breaches.
  - `AuditEvent`: Append-only hash-chained financial audit log.

### `Backend/services/policy_engine.py`
- **Path**: [`Backend/services/policy_engine.py`](file:///e:/Accounting/Backend/services/policy_engine.py)
- **Role**: Policy evaluation service, version lifecycle management, and exception matching.
- **Key Methods**:
  - `list_policies()`: Retrieves policies, versions, and active rules.
  - `create_policy_version()`: Authors a new policy version with snapshot of previous configuration.
  - `submit_policy_version()`: Transitions policy version from `draft` to `pending_approval`.
  - `approve_policy_version()`: Activates version while enforcing `author_id != approver_id`.
  - `evaluate_expense_report()`: Evaluates expense reports against active rules (cap, frequency, receipts, MFAR, duplicate receipts).
  - `create_exception_request()` / `decide_exception_request()`: Single-use exception request workflow.

### `Backend/services/finance_workflows.py`
- **Path**: [`Backend/services/finance_workflows.py`](file:///e:/Accounting/Backend/services/finance_workflows.py)
- **Role**: State machines for cash advances, disbursements, expense review, and P2P cycles.
- **Key Methods**:
  - `create_cash_advance()`, `submit_cash_advance()`, `decide_cash_advance()`: Advance authorization.
  - `prepare_disbursement()`, `verify_disbursement()`, `approve_disbursement_bank()`, `record_disbursement_execution()`, `verify_disbursement_evidence()`: 6-person SoD pipeline.
  - `record_cash_return()`: Evidence-backed cash returns crediting employee advance balance.
  - `close_cash_advance()`: Requires reconciled balance or controller-signed exception.
  - `review_expense_report()`: Line-level approve/reject with automatic oldest-first settlement application.
  - `create_vendor()`, `verify_vendor()`, `request_vendor_bank_change()`, `decide_vendor_bank_change()`: Vendor management.
  - `create_purchase_request()`, `decide_purchase_request()`: PR processing.
  - `validate_account_payable()`, `prepare_payable_payment()`, `verify_and_reconcile_payable_payment()`: AP lifecycle.

### `Backend/services/finance_reporting.py`
- **Path**: [`Backend/services/finance_reporting.py`](file:///e:/Accounting/Backend/services/finance_reporting.py)
- **Role**: Executive KPIs, overdue aging schedules, unified read models, and CSV exports.
- **Key Methods**:
  - `get_dashboard_summary()`: Computes KPIs including open advance balances, overdue aging (>30 days), and AP balances.
  - `get_centralized_tracker()`: Unified read model across Advances, Reports, PRs, and Payables.
  - `export_tracker_csv()`: Generates formula-safe CSV with UTF-8 BOM (`\uFEFF`) and cell sanitization (`'`, `=`, `+`, `-`, `@`).
  - `list_exception_register()` / `resolve_exception_register_item()`: Operational exception register management.

### `Backend/finance_routes.py`
- **Path**: [`Backend/finance_routes.py`](file:///e:/Accounting/Backend/finance_routes.py)
- **Role**: FastAPI router under `/api/v1/finance/*`.
- **Key Endpoints**:
  - `GET /auth/me`, `POST /auth/act-as`
  - `GET /policies`, `POST /policies/{policy_code}/versions`, `POST /policies/versions/{id}/approve`
  - `GET /exception-requests`, `POST /exception-requests`, `POST /exception-requests/{id}/decide`
  - `POST /documents`, `GET /documents/{id}/download`
  - `GET /cash-advances`, `POST /cash-advances`, `POST /cash-advances/{id}/decide`
  - `POST /cash-advances/{id}/disbursements`, `POST /disbursements/{id}/verify`, `POST /disbursements/{id}/execute`
  - `GET /expense-reports`, `POST /expense-reports`, `POST /expense-reports/{id}/lines`, `POST /expense-reports/{id}/evaluate`, `POST /expense-reports/{id}/review`
  - `GET /vendors`, `POST /vendors/{id}/verify`, `GET /payables`, `POST /payables`
  - `GET /dashboard/summary`, `GET /tracker`, `GET /tracker/export.csv`, `GET /audit-events`


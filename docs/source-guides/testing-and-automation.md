# Testing, Git Automation & Governance Guide

This guide details the automated test suite, Git workflow script, and paired AI engineering rules and skills.

---

## 1. `Backend/test_orchestration.py`
- **Path**: [`Backend/test_orchestration.py`](file:///e:/Accounting/Backend/test_orchestration.py)
- **Framework**: Pytest, FastAPI `TestClient`, SQLAlchemy test fixtures
- **Total Tests**: 15 automated integration tests

### Detailed Test Inventory
1. `test_legacy_dashboard()`: Asserts HTTP 200 on `/api/dashboard` and verifies the presence of `costTrendData`, `departmentData`, and department metrics.
2. `test_legacy_departments_no_404()`: Iterates through all cost centers (`manufacturing`, `assembly`, `qa`, `logistics`) ensuring no route returns 404.
3. `test_legacy_products_and_variance()`: Verifies payload structures for `/api/products` and `/api/variance`.
4. `test_legacy_configuration_get_and_put()`: Verifies reading general configuration and updating company name via `PUT /api/configuration`.
5. `test_order_creation_server_side_pricing()`: Verifies server-side authoritative pricing for Product A (10 units @ ₱125.00 = ₱1,250.00 = 125,000 centavos).
6. `test_payment_attempt_and_idempotency()`: Verifies that creating a payment attempt with an existing `idempotency_key` returns the identical attempt.
7. `test_webhook_hmac_verification_and_state_transitions()`:
   - Forged signature test: Asserts HTTP 401 Unauthorized when signature is modified.
   - Valid HMAC test: Verifies `checkout_session.payment.paid` transitions attempt to `succeeded` and order to `paid`.
   - Webhook deduplication test: Dispatches exact duplicate event and asserts `{ duplicate: True }`.
   - Monotonic state test: Dispatches late `payment.failed` event and asserts attempt remains `succeeded`.
8. `test_reconciliation_and_outbox()`: Verifies single-attempt reconciliation, bulk reconciliation, and outbox task listing.
9. `test_get_order_detail_includes_latest_attempt()`: Verifies that retrieving an order includes the `latest_attempt` object.
10. `test_idempotency_conflict_across_different_orders()`: Asserts HTTP 400 when attempting to reuse an idempotency key across two different orders.
11. `test_payment_attempt_on_cancelled_order_rejected()`: Asserts HTTP 400 when attempting to create a payment attempt on a cancelled order.
12. `test_department_invoice_pricing_lookup()`: Verifies department monthly settlement budget lookup for Logistics (₱10,000.00 = 1,000,000 centavos).
13. `test_terminal_state_immutability_expired_and_failed()`: Verifies that once an attempt reaches `expired`, a delayed `payment.failed` event cannot alter the status.
14. `test_simulate_webhook_with_order_id_fallback()`: Verifies the sandbox simulator fallback resolution when passed `order_id` instead of `payment_attempt_id`.
15. `test_legacy_configuration_users_persistence()`: Verifies user roster appending, retrieval, and cleanup via `POST /api/configuration/users`.

---

## 2. `scripts/git_workflow.py`
- **Path**: [`scripts/git_workflow.py`](file:///e:/Accounting/scripts/git_workflow.py)
- **Role**: Automation CLI for Conventional Commits 1.0.0 and Semantic Versioning 2.0.0.

### Functional Scope Mapping
- `repo`: `.gitignore`, `README.md`, `LICENSE`
- `backend`: `Backend/` (excluding tests)
- `frontend`: `Frontend/`
- `test`: `Backend/test_*`, `tests/`
- `docs`: `docs/`
- `agents`: `.agents/`
- `versioning`: `scripts/`

### CLI Commands
- `python scripts/git_workflow.py status`: Groups and displays working tree changes by functional scope.
- `python scripts/git_workflow.py stage <scope>`: Stages all changed files belonging to a specific functional scope.
- `python scripts/git_workflow.py commit --type <type> --scope <scope> --message "<msg>"`: Formats and executes a conventional commit.
- `python scripts/git_workflow.py release <major|minor|patch>`: Computes the next SemVer version, creates an annotated release tag (`vX.Y.Z`), and outputs release metadata.

---

## 3. Workspace Architectural Invariants (`.agents/rules/*`)
1. **`accounting-invariants-and-csv-encoding.md`**:
   - Mandates UTF-8 Byte Order Mark (`\uFEFF`) on CSV exports to eliminate Excel CP-1252 Mojibake.
   - Enforces cost variance convention: $\text{Variance} = \text{Budget} - \text{Actual}$.
   - Mandates raw numeric values in spreadsheet export cells.
   - Requires Next.js `-H localhost` binding to prevent Windows APIPA resolution delays.
2. **`payment-orchestration-and-legacy-guardrails.md`**:
   - Forbids marking orders paid via browser redirects.
   - Enforces authoritative backend pricing.
   - Mandates monotonic terminal state immutability.
   - Enforces timeout safety (transition to `unknown` rather than auto-retrying).
3. **`git-workflow-and-commit-conventions.md`**:
   - Strict Conventional Commits 1.0.0 and release tagging guidelines.

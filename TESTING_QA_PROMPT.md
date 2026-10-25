# Robust Testing and QA Prompt: R.G.E Accounting Platform

This file has four parts:

1. **How the prompt is built** (the method, so you can adapt it yourself)
2. **Test strategy mapped to your scaffold** (what gets tested, where, and why)
3. **The prompt** (copy-paste into your coding agent, one phase at a time)
4. **How to verify the agent's work** and the QA standard it must meet

---

## Part 1. How to build a good testing prompt (the method)

A weak testing prompt says "write unit, integration and security tests." An agent then produces many shallow tests that pass but prove little. A strong prompt follows these eight rules, and each one is encoded in Part 3.

| # | Rule | Why it matters for your system |
|---|---|---|
| 1 | **Audit first, write later.** The agent reads the code and reports what exists before creating tests | You already have 88 passing Python tests. New work must build on them, not duplicate them. |
| 2 | **Test by risk, not by file count.** Rank areas by financial and security impact and test the highest first | Money, VAT, receipts, the audit hash chain, webhooks and SoD matter far more than the cost-allocation dashboard. |
| 3 | **Name the invariants.** List the rules that must always hold and require a test per rule | Your spec already has them: integer centavos only, terminal payment states are immutable, `Maker ≠ Verifier ≠ BankApprover ≠ EvidenceVerifier`, audit and ledger are append-only. |
| 4 | **Map tests to the scaffold.** Say exactly which test project each kind of test goes in | Your scaffold already defines `Architecture.Tests`, `Contract.Parity.Tests`, `Security.Tests`, `Performance.Tests`, `Integration.Tests`, `Tests.Common` and per-module test projects. |
| 5 | **Require evidence, not claims.** The agent must run the tests and show real output, including coverage and mutation results | "All tests pass" is not evidence unless the run output is shown. |
| 6 | **Make tests prove they can fail.** Require mutation testing on critical code, and a "break it on purpose" check | A test that cannot fail gives false confidence. |
| 7 | **Report bugs, do not hide them.** When a test exposes a defect, the agent writes a failing test and reports it. It does not silently change production code | You need to decide how each defect is fixed, especially in financial logic. |
| 8 | **Gate with approvals.** The agent stops after each phase | Reviewable chunks, and no runaway changes. |

**How to run it:** do not paste the whole prompt and walk away. Give the agent the header (Role, Context, Constraints, Output rules) plus **one phase at a time**. Large single prompts exceed what an agent can hold in context and the quality drops in later phases.

---

## Part 2. Test strategy mapped to your scaffold

### 2.1 Test types and where they live

| Test type | Purpose | Location in your scaffold | Main tools (verify .NET 10 support first) |
|---|---|---|---|
| **Unit** | Pure domain and application logic, no I/O | `tests/Modules/<Module>.Domain.Tests`, `.Application.Tests` | xUnit, Shouldly (or AwesomeAssertions), NSubstitute, Bogus |
| **Property-based** | Prove invariants over thousands of random inputs | Same projects, in a `Properties/` folder | FsCheck |
| **Path / state-machine** | Cover every branch path and every state transition, valid and invalid | `Domain.Tests` | xUnit theories, cyclomatic-complexity report, coverlet branch coverage |
| **Integration** | Real PostgreSQL, real SQL, stored functions, outbox, endpoints | `tests/Integration.Tests`, `tests/Modules/*.Integration.Tests` | Testcontainers (PostgreSQL), WebApplicationFactory, Respawn, WireMock.Net |
| **Contract / parity** | C# responses match the Python golden master for every route | `tests/Contract.Parity.Tests`, `tools/ParityRunner` | Verify (snapshots), JSON schema diff |
| **Regression** | Stop old bugs from returning | Tagged `[Trait("Category","Regression")]` across projects, plus `Regression/` folders | Same as above |
| **Security** | Authz bypass, IDOR, CSRF, replay, SoD bypass, injection, upload attacks | `tests/Security.Tests` | xUnit, WebApplicationFactory, OWASP ZAP baseline, Semgrep, CodeQL, gitleaks |
| **Architecture** | Enforce layer and module boundaries | `tests/Architecture.Tests` | NetArchTest |
| **Smoke** | Fast "is it alive and wired right" after every deploy | `tests/Smoke/` (new, small) | Plain HTTP checks, runs in under 2 minutes |
| **End to end (E2E)** | Real user journeys through browser, frontend and backend | `e2e/` at repo root (Playwright) | Playwright |
| **Performance / load** | Check latency budgets | `tests/Performance.Tests` | NBomber, BenchmarkDotNet |
| **Resilience / chaos** | Gateway timeouts, SMTP down, DB failover, duplicate delivery | `Integration.Tests/Resilience/` | WireMock.Net fault injection, Testcontainers pause/stop |
| **Concurrency** | Races in the audit chain, idempotency, outbox leasing | `Integration.Tests/Concurrency/` | Parallel tasks against real PostgreSQL |
| **Mutation** | Prove tests catch real defects | CI job on critical projects only | Stryker.NET (and Stryker for TypeScript if useful) |
| **Frontend unit/component** | Components, hooks, form logic | `frontend/**/__tests__` | Vitest or Jest, React Testing Library, MSW |
| **Accessibility** | Basic WCAG checks | Inside Playwright E2E | axe-core |
| **Python line** | Characterization of legacy behavior | `Backend/test_*.py` (existing 88 tests) plus new | pytest, Hypothesis, Schemathesis (fuzz FastAPI from OpenAPI) |

### 2.2 Risk-ranked priority (test these first)

| Priority | Area | Why |
|---|---|---|
| **P0** | Money, VAT, rounding, receipt numbers, audit digest `sha256:v2` | Financial and BIR compliance. One bug is a legal problem. |
| **P0** | Audit hash chain and append-only guarantees | The tamper-evidence of the whole system. |
| **P0** | Payment webhooks: signature, replay, idempotency, terminal-state immutability | Money moves here and it is internet-facing. |
| **P0** | Authentication, session, CSRF, authorization, SoD four-eyes | Prevents fraud and privilege escalation. |
| **P1** | Cash advance five state machines, ledger balance derivation, settlement, closing snapshot | Core finance workflow. |
| **P1** | Vendor bank change dual-approval, AP deduplication, 3-way match | Fraud controls. |
| **P1** | Document upload (magic bytes, size, path traversal) | Classic attack surface. |
| **P2** | Policy engine, approval matrix, aging buckets, business-day calendar | Business correctness. |
| **P2** | Integrations (Google Forms, Sheets sync, SMTP) | Important but isolated behind ports. |
| **P3** | Cost allocation, legacy dashboard | Lower risk. |

### 2.3 Invariants that must each have at least one test

1. Money is always `long` centavos. No `float`, `double` or `decimal` in money paths (architecture test plus a property test).
2. VAT: `VatableSales = round_half_up(Amount / 1.12)` and `Vat = Amount - VatableSales`, so `VatableSales + Vat == Amount` always (property test over the full range, plus boundary golden vectors such as x.5 centavo cases).
3. Terminal payment states (`succeeded`, `failed`, `cancelled`, `expired`) can never be overridden by a late webhook.
4. `UNIQUE(provider, idempotency_key)` and `UNIQUE(provider, provider_event_id)` hold under concurrent requests.
5. The audit chain is gap-free, `prev_hash` links correctly, and `record_hash` is reproducible. UPDATE and DELETE on `audit_events` and `advance_ledger_entries` fail for the runtime DB role.
6. Advance balance is derived, never stored, and equals `Σ release − Σ expense_applied − Σ cash_return + Σ adjustments`.
7. SoD: for every four-eyes workflow, every forbidden pairing of actors is rejected, at the domain, application and DB levels.
8. Server-side price is authoritative. A client-supplied price is ignored.
9. Receipt numbers (`OR-YYYYMM-XXXX-HASH8`) are deterministic and collision-resistant, and re-issuing a receipt never changes a historical one.
10. CSV exports neutralize `=`, `+`, `-`, `@` prefixes.
11. Dev-only routes (`switch-user`, `act-as`, `sandbox/*`, `simulate`) do not exist in a Release build.
12. Login lockout triggers after 5 failures for 15 minutes. Session tokens are stored hashed.

### 2.4 Quality gates (proposed starting thresholds, tune with your team)

| Gate | Threshold |
|---|---|
| Domain line coverage / branch coverage | ≥ 90% / ≥ 85% |
| Application layer line coverage | ≥ 80% |
| Mutation score on SharedKernel Money, Payments state machine, Receipts, Audit, SoD | ≥ 80% |
| Route parity vs Python golden master | 100% of unique routes (~110) |
| Security test suite | 100% pass, zero skipped |
| Dependency and container scan | Zero critical or high vulnerabilities |
| Secret scan | Zero findings |
| Smoke suite | Passes in under 2 minutes after every deploy |
| Performance | Webhook p99 < 50 ms, reads p99 < 30 ms, create order p99 < 80 ms, regression above 10% fails the build |
| Flaky tests | Zero tolerated: quarantine, fix within an agreed window, never retry-until-green |
| Test determinism | No test depends on wall-clock time, random seeds without logging, or test order |

---

## Part 3. The prompt (copy everything inside the block)

Use the header plus **one phase at a time**.

````markdown
# ROLE
You are a principal QA architect and test automation engineer. You write tests that prove correctness and can fail when the code is wrong. You work in a Windows environment (PowerShell) on `E:\Accounting`. You stop at every APPROVAL GATE and wait for my confirmation.

# CONTEXT
- The repository contains an accounting platform for R.G.E Accounting Firm Inc. (Philippines, BIR RMC 77-2024 / RA 11976 compliance).
- **Line A:** Next.js + TypeScript frontend + C# .NET 10 backend (`Backend.DotNet/`), a modular monolith with modules Identity, Payments, Receipts, Finance, Audit, CostAllocation, Documents, Integrations, each split into Domain, Application, Infrastructure, Api and Contracts projects, plus `BuildingBlocks`.
- **Line B (legacy):** Next.js + TypeScript frontend + Python FastAPI backend (`Backend/`) with 88 existing passing pytest tests across `test_orchestration.py` (47), `test_finance_operations.py` (9) and `test_google_forms_integration.py` / `test_integrations.py` / `test_proxy_cors.py` (32).
- The two lines are on **different branches**. Detect the branch names; do not guess. The frontend is mostly identical on both lines.
- The existing tests folder layout (from the architecture scaffold) is: `tests/Architecture.Tests`, `Contract.Parity.Tests`, `Security.Tests`, `Performance.Tests`, `Integration.Tests`, `Tests.Common`, and `tests/Modules/<Module>.{Domain,Application,Integration}.Tests`. Use these. Create projects only where they are missing.
- PostgreSQL stored functions (`fn_append_audit_event`, `fn_authenticate_user`, `fn_next_sequence_number`, `fn_sanitize_input`, `fn_oauth_create_state`, `fn_oauth_consume_state`) are part of the system under test.

# GOAL
Build a robust, deterministic, CI-ready test suite and QA standard covering: unit, property-based, path (branch and state-transition), integration, contract/parity, regression, security, architecture, smoke, end-to-end, performance, resilience, concurrency and mutation testing, for the .NET line, the frontend, and the Python line (characterization only).

# HARD CONSTRAINTS (never violate)
1. **Never modify production code** unless I approve a specific testability change at a gate (for example adding an `IClock` seam or `InternalsVisibleTo`). Propose it; do not do it silently.
2. **If a test exposes a bug, do not fix the production code.** Write the failing test, mark it with `[Trait("Category","KnownDefect")]` and a skip reason referencing a defect ID only if I approve skipping, and add the finding to `docs/qa/defect-log.md` with severity, reproduction, expected vs actual, and suggested fix.
3. **Never touch real external systems:** no real PayMongo calls, no real SMTP, no real Google APIs, no production or shared database. Use Testcontainers PostgreSQL, WireMock.Net or fakes, and a fake SMTP.
4. **No secrets** in tests, fixtures, logs or snapshots. Use obviously fake values (for example `test_secret_not_real`). If you find a real-looking secret in the repo, report it and do not copy it.
5. **Deterministic tests only:** inject `FakeClock`, seed randomness and log the seed, no `Thread.Sleep`, no reliance on test order or on the machine's timezone. Business time is `Asia/Manila`.
6. **Never rewrite history** (no force push, no tag changes). Commit on a branch named `test/<phase>-<line>`, using Conventional Commits with scope matching the module (for example `test(payments): ...`). Do not push.
7. **No mixing** frontend and backend paths in one commit.
8. **No snapshot or golden file may contain personal data or secrets.** Use synthetic data.
9. Do not weaken, delete or skip an existing passing test. If one looks wrong, report it.
10. Pin tool versions. Before adopting any library, verify it supports the .NET, Node and Python versions this repo uses and check its license. If unsure, say so and propose an alternative.
11. Never add `[Retry]`, rerun-until-green, or broad timeouts to hide flakiness.
12. Tests must be fast: unit tests under 100 ms each on average, the full unit suite under 2 minutes, and integration tests parallel-safe with isolated schemas or database snapshots.

# TEST DESIGN RULES (apply to every test)
- Naming: `Method_Scenario_ExpectedResult`.
- Arrange / Act / Assert, one logical behavior per test.
- Prefer theories/data-driven tests for tables of cases. Keep the data in readable form.
- Each test traces to a requirement ID or invariant ID from `docs/qa/traceability-matrix.md` (see Phase 1). Put the ID in a trait: `[Trait("Req","INV-VAT-001")]`.
- Traits for selection: `Category` ∈ {`Unit`,`Property`,`Path`,`Integration`,`Contract`,`Regression`,`Security`,`Architecture`,`Smoke`,`E2E`,`Performance`,`Resilience`,`Concurrency`}, and `Priority` ∈ {`P0`,`P1`,`P2`,`P3`}.
- Test data through builders in `Tests.Common` (Object Mother / Builder pattern) with synthetic values, never hand-written copies of production data.
- Use `Shouldly` or `AwesomeAssertions` (verify license and .NET 10 support) rather than a commercially licensed assertion library without my approval.
- A test must fail for the right reason: for every P0 test, demonstrate once (and record in the report) that deliberately breaking the production rule makes the test fail.

# PHASE 0. READ-ONLY AUDIT (no changes)
Report:
1. Branch names for both lines; current test projects and files; test frameworks and versions; how tests run today (commands, runtime, pass/fail).
2. Run the existing Python suite exactly as the project runs it and report pass/fail counts and duration. Do not change anything.
3. Per module, list public behaviors, state machines, calculations, branch-heavy methods, stored-function calls, external calls, and config switches. Compute or estimate cyclomatic complexity of the 25 most complex methods (use a tool if available; otherwise state that it is an estimate).
4. Existing coverage numbers, if any. If none, say so.
5. Seams that block testing: static clocks, `DateTime.Now`/`datetime.now`, `new HttpClient`, direct `File` or `Environment` access, static singletons, missing interfaces.
6. Frontend: framework versions, package manager, existing tests, API client approach, how env vars are used.
7. CI files present and what they run.
**APPROVAL GATE 0:** Present the audit with open questions. Wait.

# PHASE 1. STRATEGY, TRACEABILITY AND QA STANDARD (documents only)
Create:
- `docs/qa/test-strategy.md`: pyramid and ratios, test types and their locations (use the table in this prompt's context), tooling decisions with versions and licenses, environments, test data policy, flaky-test policy, definition of done.
- `docs/qa/traceability-matrix.md`: columns Requirement/Invariant ID, Description, Module, Priority, Test type(s), Test project, Test name(s), Status. Seed it from these invariants and add any others you find in the spec and code:
  - INV-MONEY-001 money is `long` centavos only
  - INV-VAT-001 `VatableSales + Vat == Amount`, half-up rounding
  - INV-PAY-001 terminal payment states are immutable to late webhooks
  - INV-PAY-002 idempotency key uniqueness under concurrency
  - INV-PAY-003 webhook event uniqueness (replay)
  - INV-PAY-004 server-side authoritative pricing
  - INV-AUD-001 audit chain continuity and reproducible hash
  - INV-AUD-002 audit and ledger are append-only for the runtime role
  - INV-LED-001 advance balance is derived, formula verified
  - INV-SOD-001..00N every forbidden actor pairing per workflow
  - INV-RCT-001 receipt number format and determinism; INV-RCT-002 historical receipts never change; INV-RCT-003 digest `sha256:v2` stability
  - INV-DOC-001 magic bytes, size limit, path traversal, random stored names
  - INV-SEC-001..00N lockout, CSRF, session token hashing, rate limits, security headers, dev-only routes absent in Release
  - INV-CSV-001 injection-safe CSV
  - INV-CAL-001 PH business-day and holiday logic, aging buckets boundaries
- `docs/qa/quality-gates.md`: thresholds table (propose: Domain line ≥ 90% and branch ≥ 85%, Application line ≥ 80%, mutation score ≥ 80% on Money, Payments state machine, Receipts, Audit and SoD, route parity 100%, security suite 100% pass with zero skips, zero critical/high vulnerabilities, zero secret findings, smoke under 2 minutes, performance regression above 10% fails).
- `docs/qa/defect-log.md` (empty template: ID, severity S1 to S4, module, found by, reproduction, expected, actual, status) and `docs/qa/release-exit-criteria.md`.
- `docs/qa/test-data-policy.md`: synthetic data only, builders, no production data, no real PII.
**APPROVAL GATE 1:** Show the documents. Wait.

# PHASE 2. TEST INFRASTRUCTURE (`Tests.Common` and fixtures)
Build, in the existing `tests/Tests.Common` project:
- `FakeClock` (settable, `Asia/Manila`), `FakeIdGenerator`, deterministic randomness helper that logs its seed.
- Builders for core aggregates and value objects (Order, PaymentAttempt, CashAdvance, Disbursement, ExpenseReport, Vendor, PurchaseRequest, AccountPayable, Money).
- `PostgresFixture` (Testcontainers) that applies the real DbUp migrations from `db/migrations` and functions from `db/functions`, then provides a clean database per test class or per test (Respawn or template database) with least-privilege roles that mirror `app_runtime_role`, `app_migrator_role` and `app_readonly_role`.
- `ApiFactory` based on `WebApplicationFactory` that replaces PayMongo, SMTP, Google Sheets and file storage with fakes or WireMock.Net, and lets tests authenticate as any actor with any role set.
- `WebhookSigner` test helper that produces valid and tampered `Paymongo-Signature` headers using a fake secret.
- A `CollectionFixture` strategy so containers start once per run, not once per test.
Show a short sample test in each style to prove the plumbing works, and run them.
**APPROVAL GATE 2:** Show the diff and test output. List any testability changes needed in production code (do not make them). Wait.

# PHASE 3. UNIT AND PROPERTY TESTS (domain first, in P0 to P3 order)
For each module in priority order, write unit tests in `<Module>.Domain.Tests` and `<Module>.Application.Tests`. Required coverage per area:
- **SharedKernel/Money:** construction, addition/subtraction overflow behavior, negative values, currency mismatch, conversions. Property tests: no operation produces a non-integer centavo value.
- **VatCalculator:** golden vectors including half-centavo boundaries (for example amounts where `Amount/1.12` ends in exactly .5), zero, one centavo, maximum `long` safe range, and a property test over a large random range proving `VatableSales + Vat == Amount` and `ROUND_HALF_UP` behavior. Compare results against an independent reference implementation written in the test project using `BigInteger` arithmetic.
- **Receipts:** receipt number format `OR-YYYYMM-XXXX-HASH8`, determinism, HASH8 collision handling, `sha256:v2` digest golden snapshot using fixed inputs (a change to the digest must fail the test loudly), canonical JSON ordering and whitespace rules.
- **Payments:** order creation, server-side pricing, quantity limits, `IdempotencyKey` rules, all `PaymentAttempt` transitions (see Phase 4).
- **Finance:** advance numbering, due-date calculation with the PH business calendar and holidays, ledger balance calculator, ExpenseLine fingerprint duplicate detection, policy evaluator rules (hard block, exception allowed, warn only), period counters, approval router and matrix thresholds at exact boundaries (min, max, min-1, max+1).
- **Procurement/Vendors:** normalized invoice number deduplication, 3-way match outcomes, masked bank account, dual-authorization of bank changes.
- **Documents:** magic bytes for PDF, PNG and JPEG (valid, spoofed extension, truncated, polyglot), size limit at exactly 10 MB and 10 MB + 1 byte, path traversal strings (`..\`, `../`, encoded variants, absolute paths, reserved Windows names).
- **Audit:** hash computation, canonical serialization.
- **Business calendar:** weekends, holidays, year boundaries, `Asia/Manila` versus UTC date boundaries.
- **Aging buckets:** boundaries at days overdue 0, 1, 30, 31, 60, 61.
Do not use mocks for pure domain logic. Use mocks only at ports.
**APPROVAL GATE 3 (after each module):** Show test counts, coverage (line and branch) per project, mutation score on the P0 areas (run Stryker.NET), the list of defects found, and the traceability matrix updates. Wait.

# PHASE 4. PATH TESTING (branch coverage and state machines)
1. **Basis-path testing:** for the 25 highest-complexity methods from Phase 0, derive independent paths (cyclomatic complexity + 1) and create one test per path. Document the path table in `docs/qa/path-coverage.md` (method, complexity, paths, test names). Verify with branch coverage results.
2. **State-machine exhaustive testing:** for each state machine (`PaymentAttempt` and the five `CashAdvance` machines: `approval_status`, `payment_status`, `liquidation_review_status`, `settlement_status`, `posting_status`; plus `Disbursement`, `PolicyVersion` lifecycle, `ExpenseReport` review status, `ExceptionRequest`, `VendorBankChange`), generate a full N×N transition matrix test:
   - every allowed transition succeeds with the expected side effects and events;
   - **every disallowed transition is rejected** with the expected error and with no state change and no event;
   - terminal states reject all transitions;
   - cross-machine rules (for example posting only after evidence verified) are tested as combinations.
   Generate the matrix from the code's transition tables where possible, and compare it to the transition diagrams in the spec (`docs/architecture` and the spec's Mermaid diagrams) so the test also catches code that drifted from the documented design.
3. **Sequence-path tests** for the cash-advance four-eyes workflow: the happy path plus each step skipped, repeated, reordered, and attempted by a forbidden actor.
**APPROVAL GATE 4:** Show the path tables, the transition matrices (counts of allowed/rejected), and branch coverage. Wait.

# PHASE 5. INTEGRATION TESTS (real PostgreSQL)
In `tests/Integration.Tests` and `tests/Modules/*.Integration.Tests`:
- **Migrations:** apply all migrations to an empty database, then verify schema, constraints, indexes and roles. Verify that re-running is a no-op.
- **Stored functions:** direct tests for each `fn_*`: `fn_sanitize_input` (injection prefixes, whitespace, null, unicode), `fn_next_sequence_number` (atomic under parallel calls, padding), `fn_append_audit_event` (sequence monotonic, `prev_hash` link, reproducible `record_hash`, advisory lock `904210` serializes 50 parallel appends with no gaps or duplicates), `fn_authenticate_user` (success, wrong password, unknown user, lockout after 5 failures for 15 minutes, lockout expiry using a controllable time source or a documented DB-time approach, token entropy and uniqueness), `fn_oauth_create_state` / `fn_oauth_consume_state` (single use, expiry at 10 minutes, replay).
- **Least privilege:** connect as the runtime role and assert that UPDATE and DELETE on `audit_events` and `advance_ledger_entries` fail, that DDL fails, and that the readonly role cannot write.
- **Repositories and SQL:** each repository's queries against real data, including keyset pagination edge cases (empty, one page, exact page boundary), unique constraint violations mapping to the right domain error, and transaction rollback on failure.
- **Outbox:** a paid order writes exactly one outbox row in the same transaction. Worker lease behavior: two workers never process the same row, a crashed worker's lease expires and the row is retried, handlers are idempotent when delivered twice, poison messages move to failed with a reason.
- **HTTP endpoints** through `ApiFactory`: each route category has success, validation failure, unauthenticated, unauthorized, wrong scope, not found, conflict, and idempotent-repeat cases. Check ProblemDetails shape, correlation ID header, and that no stack trace is ever returned.
- **End-to-end business flows** (in-process, fake gateway): order → attempt → webhook paid → outbox → receipt issued → PDF rendered → email sent; cash advance full lifecycle through all four-eyes steps to closing snapshot; vendor bank change dual approval; AP invoice → 3-way match → payment → reconciliation.
- **Gateway fakes:** PayMongo success, decline, timeout, 500, malformed JSON, slow response beyond the configured timeout, and circuit-breaker opening and recovery.
**APPROVAL GATE 5:** Show results, the DB role test evidence, parallel-run proof (run the suite twice in parallel with no interference), and defects found. Wait.

# PHASE 6. SECURITY TESTING (`tests/Security.Tests`)
Write automated tests for each item, mapped to OWASP ASVS requirement IDs in the traceability matrix:
1. **Authentication:** brute force and lockout, user enumeration (identical response and timing class for unknown user and wrong password), session fixation, session expiry and revocation on logout, cookie flags (`HttpOnly`, `Secure`, `SameSite=Strict`), tokens stored hashed, Bearer token handling.
2. **Authorization (deny by default):** iterate **every registered route** via the endpoint data source and assert that each has an explicit authorization policy or is on an explicit anonymous allowlist (webhook, health, version, login, csrf, OAuth). Any new unprotected route fails the build.
3. **IDOR / BOLA:** for every route that takes an id (orders, payments, advances, expense reports, documents, vendors, payables), test that an actor from another department or user cannot read or change another's data.
4. **Privilege escalation:** the client cannot select its own role; `act-as` and `switch-user` do not exist in Release; role tampering in headers, cookies and bodies has no effect.
5. **SoD bypass:** for every four-eyes workflow, attempt every forbidden actor combination through the HTTP API (not only the domain), including same user holding multiple roles, role assigned mid-flow, and race attempts (two requests at once as the same user).
6. **CSRF:** state-changing cookie-session requests without or with a wrong token are rejected. Token is single-site bound.
7. **Webhooks:** missing signature, wrong signature, valid signature with modified body, valid signature outside the timestamp tolerance, replayed event ID, huge body over the cap, wrong content type, and constant-time comparison (assert the comparer used is the constant-time one via an architecture or unit test).
8. **Injection:** SQL injection payloads in every string input and query parameter (assert parameterized queries only, plus an architecture test that bans string-built SQL), CSV/formula injection on export, header injection and CRLF in email fields, log injection (control characters in user input are neutralized in logs), JSON deserialization with unexpected types and oversized numbers (money overflow).
9. **File upload:** spoofed MIME and extension, polyglot files, zip bombs or oversized streams, path traversal in filenames, double extensions, null bytes, concurrent uploads of the same hash, download endpoint authorization and `Content-Disposition` / `X-Content-Type-Options`.
10. **Rate limiting:** login, webhook and upload buckets enforce limits, return 429 with `Retry-After`, and are per-IP and per-actor.
11. **Security headers and TLS config:** HSTS, CSP, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, no `Server` banner, CORS allowlist is exact (no wildcard with credentials).
12. **Error handling:** forced exceptions never leak stack traces, connection strings or file paths.
13. **Secrets:** a startup test proves the app fails fast when a required secret is missing, and a repository scan test (gitleaks or equivalent in CI) proves no secrets are committed. Verify the Google Forms intake secret is compared in constant time and is read from configuration, never from source.
14. **Configuration endpoint:** `POST /api/v1/integrations/configure` is limited to `system_admin`, never writes credentials to a JSON file, and every change is audited.
15. **Tooling in CI (configure, do not run against production):** OWASP ZAP baseline against the test environment, Semgrep, CodeQL, `dotnet list package --vulnerable --include-transitive`, `npm audit`, `pip-audit`, container scan (Trivy or equivalent), gitleaks.
For any finding, follow Hard Constraint 2 (report, do not silently fix).
**APPROVAL GATE 6:** Show the route-by-route authorization table, the SoD attempt matrix with results, and every finding with severity. Wait.

# PHASE 7. REGRESSION TESTING
1. **Golden master / parity** (`Contract.Parity.Tests` and `tools/ParityRunner`): capture Python responses for every unique route (about 110; list the duplicate aliases #111 and #112 separately) using synthetic seed data and a fixed clock. Replay against the .NET line and diff status code, headers that matter, body schema and values. Maintain an explicit, reviewed **allowed-differences list** (for example generated IDs, timestamps) with a reason for each. Output `docs/api/endpoint-parity-matrix.md` with pass/fail per route.
2. **Snapshot tests** (Verify) for: receipt PDF text content (extract text, do not compare binary), receipt JSON, tracker CSV, dashboard summary, error responses.
3. **Bug-to-test rule:** create `docs/qa/regression-policy.md`: every defect fixed in the future must land with a failing-first test tagged `Regression` and the defect ID. Seed `tests/**/Regression/` folders and tag any existing tests that already guard past fixes.
4. **Selection:** define three suites by trait: `PR` (Unit, Path, Architecture, Smoke, fast Integration), `Nightly` (everything except long load tests, plus mutation on critical projects), `Release` (all plus parity, security, performance, E2E).
5. **Data migration verification:** extend `tools/DataMigration.Verifier` tests (row counts, checksums) and `tools/AuditChain.Verifier` tests (detect a deliberately corrupted row, a deleted row, and a reordered row in a copy of the data).
**APPROVAL GATE 7:** Show the parity matrix, the allowed-differences list, and the suite definitions. Wait.

# PHASE 8. SMOKE AND END-TO-END TESTING
1. **Smoke suite** (`tests/Smoke`, must finish in under 2 minutes, safe to run against any deployed environment, read-only except for one clearly marked synthetic test order that is created and cleaned up in non-production environments only; in production it must be read-only):
   - `/health` and `/api/health` return healthy with DB connectivity
   - `/version` returns the expected line, version, git SHA, schema version and API contract
   - login page and session endpoint respond; CSRF token endpoint works
   - one authenticated read per module returns the expected shape
   - DB migrations are at the expected version
   - outbox backlog is below a threshold and the oldest pending age is below a threshold
   - webhook endpoint rejects an unsigned request with the expected status (proves the signature gate is active)
   - dev-only routes return 404 in production
   - audit chain verifier passes for the latest N entries
   - frontend home page loads and displays the same version as the backend `apiContract`
2. **E2E (Playwright, `e2e/`)** against the full stack in Docker Compose with fakes for the gateway and SMTP. Write journeys: customer checkout and receipt; staff login and role-based navigation; cash advance request through approval; liquidation submission and review; vendor bank-change dual approval; document upload. Add accessibility checks with axe-core on the main pages. Use stable `data-testid` selectors. Do not use arbitrary sleeps; use Playwright's auto-waiting and explicit event or network waits. Capture trace, screenshot and video on failure only.
3. The same E2E and smoke suites must be runnable against **both lines** (set by an environment variable for the base URL and expected line) so you can prove frontend parity across Line A and Line B.
**APPROVAL GATE 8:** Show a full run for each line and the timing of the smoke suite. Wait.

# PHASE 9. PERFORMANCE, RESILIENCE AND CONCURRENCY
1. **Micro-benchmarks** (BenchmarkDotNet): Money/VAT math, HMAC verification, canonical JSON, receipt number generation, audit hash. Store baselines in `docs/performance/benchmark-baselines.md`.
2. **Load tests** (NBomber) against the latency budgets: webhook p99 < 50 ms, reads p99 < 30 ms, create order p99 < 80 ms, plus a soak run and a spike run. Fail the CI performance job on a regression above 10% against the stored baseline.
3. **Concurrency tests:** 100 parallel identical webhooks produce exactly one processed event and one outbox row; 100 parallel `CreateOrder` calls with the same idempotency key create one order; parallel disbursement verify and bank-approve by forbidden combinations are rejected; 50 parallel audit appends keep the chain intact.
4. **Resilience tests:** gateway timeout, DB connection loss and recovery, SMTP down (receipt email retried without losing the receipt), outbox worker crash mid-batch, duplicate outbox delivery, clock skew on webhook timestamps, disk-full on document store. Assert graceful errors, retries with backoff, and no data loss or duplicate side effects.
5. **Memory and allocation checks** on the webhook hot path (allocation per request budget) if the tooling allows.
**APPROVAL GATE 9:** Show results with raw numbers, the machine specification, and what you could not measure reliably. Wait.

# PHASE 10. FRONTEND TESTS (identical on both lines)
1. **Unit and component** (Vitest or Jest, React Testing Library, MSW): forms and validation, money formatting (centavos to PHP display and back, never floating-point arithmetic), status badges for every payment and advance state, role-based UI visibility, error and loading states, API error mapping (ProblemDetails), CSRF header attachment, and `src/lib/version.ts` fallbacks.
2. **API contract tests:** validate MSW handlers and TypeScript types against the canonical OpenAPI document, so a backend change that breaks the frontend fails on either line.
3. **Security:** no secrets in the client bundle (scan the build output), no `dangerouslySetInnerHTML` with untrusted data (lint rule), safe handling of tokens (no tokens in `localStorage`), CSP compatibility.
4. **Type and lint gates:** `tsc --noEmit` strict, ESLint security rules, no `any` in money-related code.
5. Keep the frontend test code **byte-identical on both lines** except for an allowlisted environment file. If a test needs to differ per line, report it as drift.
**APPROVAL GATE 10:** Show results on both lines and the drift check. Wait.

# PHASE 11. PYTHON LINE (characterization only)
1. Keep all 88 existing tests passing and unchanged.
2. Add characterization tests only for behaviors the .NET parity matrix depends on and that are currently untested.
3. Add Hypothesis property tests for VAT math and receipt number generation to cross-check with the .NET implementation using shared test vectors in `tests/vectors/*.json` (a single source of vectors consumed by both languages).
4. Add Schemathesis against the FastAPI OpenAPI schema in a test environment (read-only routes and sandbox only).
5. Do not refactor Python production code.
**APPROVAL GATE 11:** Show results. Wait.

# PHASE 12. CI WIRING AND QA STANDARD
1. Workflows: `ci-pr.yml` (build, analyzers, unit, path, architecture, fast integration, smoke against an ephemeral stack, commitlint), `ci-nightly.yml` (full integration, security tools, mutation, parity), `ci-release.yml` (everything plus E2E, performance gate, SBOM), `ci-security-scan.yml`, `ci-performance-gate.yml`. Pin action versions to full SHAs. Use path filters so frontend-only changes do not run the whole backend suite, and vice versa.
2. Publish artifacts: test results (TRX/JUnit), coverage (Cobertura + HTML via ReportGenerator), mutation report, parity report, Playwright traces, and a one-page `docs/qa/quality-report.md` generated per run.
3. Branch protection recommendations (list them, do not apply): required checks, no merge on red, no skipped required jobs.
4. **Flaky policy implemented:** a script that detects tests that fail intermittently across N repeated runs and posts a quarantine list. Quarantined tests still run but do not block, and must have an owner and an expiry date.
5. **Definition of Done** added to the PR template: tests written first or with the change, traceability row updated, no new analyzer warnings, coverage gates met, security tests updated when a new route or role is added, docs updated.
6. Add a rule to `.agents/rules/` in the existing numbering style: "No new route, state transition, or money calculation without tests and a traceability entry."
**APPROVAL GATE 12:** Show the workflows, a dry run, and the final quality report. Wait.

# FINAL REPORT (after Gate 12)
Produce `docs/qa/final-qa-report.md` containing: totals by test type and module, coverage and mutation results versus gates, parity result, security findings table (severity, status), defects found (linked to `defect-log.md`), performance results versus budgets, unresolved risks, tests skipped or quarantined with reasons, what could not be verified, and a go/no-go recommendation against `release-exit-criteria.md`.

# OUTPUT RULES FOR EVERY RESPONSE
- Begin each phase with a 3-line plan. End with: what changed, commands you ran and their real output summary, what you could not verify, defects found, open questions.
- Show diffs rather than whole files unless the file is new.
- Never claim a test passes without having run it. Never claim coverage without a coverage report.
- If an instruction conflicts with a hard constraint, follow the hard constraint and tell me.
- If something is ambiguous, ask. Do not guess requirements, branch roles, or business rules.
````

---

## Part 4. How to verify the agent's work

### 4.1 Evidence you should insist on

| Claim | Evidence you must see |
|---|---|
| "Tests pass" | Real run output with counts and duration, from a clean checkout |
| "Coverage is X%" | A generated Cobertura/HTML report, with branch coverage as well as line coverage |
| "Tests are meaningful" | A Stryker mutation report on the P0 projects, and the "break it on purpose" notes |
| "Parallel safe" | The suite run twice at the same time with no failures |
| "SoD enforced" | The attempt matrix: every forbidden actor pairing, the HTTP status, and the error code |
| "Append-only" | The runtime-role test showing the DB rejecting UPDATE and DELETE |
| "Parity" | `endpoint-parity-matrix.md` with pass/fail per route and a reviewed allowed-differences list |

### 4.2 Red flags in the agent's output

- Tests that only assert "no exception thrown" or `Assert.True(true)`
- Tests that mock the very class under test
- Domain tests full of mocks (pure logic should need none)
- Production code changed in a "test" commit
- Skipped, retried or `[Fact(Skip=...)]` tests without an approved defect ID
- Coverage reported without branch coverage
- Snapshot files containing secrets or realistic personal data
- Sleeps and wall-clock dependence
- Round numbers only in VAT tests (no half-centavo boundaries)
- A security suite that tests only the happy path

### 4.3 Run order I recommend

| Order | Phases | Why first |
|---|---|---|
| 1 | 0, 1, 2 | Audit, strategy and plumbing. Everything else depends on these. |
| 2 | 3 (Money, VAT, Receipts, Audit, Payments) and 4 | P0 correctness and state machines |
| 3 | 5 and 6 | Real database behavior and security |
| 4 | 7 | Parity and regression, which gates the strangler cutover |
| 5 | 8, 9, 10 | Smoke, E2E, performance, frontend |
| 6 | 11, 12 | Python characterization and CI wiring |

### 4.4 Release exit criteria (proposed)

A release can ship only when:

1. All required CI checks are green with zero skipped required jobs.
2. Quality gates in Part 2.4 are met.
3. No open S1 or S2 defects, and every S3 has an owner and a date.
4. The smoke suite passes against the staging environment, then against production immediately after deploy.
5. The audit-chain verifier passes before and after deploy.
6. The rollback runbook has been rehearsed for this release line.

### 4.5 Limits to be honest about

- Automated tests cannot prove tax or accounting correctness by themselves. Have a **CPA or finance controller review the VAT vectors, the SoD matrix and the policy rules**, then freeze them as golden data.
- ZAP and Semgrep find common issues but do not replace a **manual penetration test** before go-live on a system that handles real payments.
- Mutation testing is slow, so run it nightly on critical projects only.
- Tool versions and licenses (assertion libraries, Stryker, Testcontainers, NBomber) change often. The agent is told to verify .NET 10 support and licensing before adopting each one, and you should confirm the choices at Gate 0 and Gate 2.

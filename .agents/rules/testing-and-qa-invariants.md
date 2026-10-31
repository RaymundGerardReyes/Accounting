---
description: Authoritative testing and QA invariants, scope boundaries, and verification standards for .NET 10 Modular Monolith
---

# Testing & QA Architecture Invariants

## 1. Scope Boundary: Strict Line Isolation
- All testing, quality assurance, verification, and test harness execution must strictly target **`E:\Accounting\Backend.DotNet`** (`src/`, `tests/`, `tools/`).
- **Legacy Python Decommissioning**: The legacy Python implementation (`Backend/`) has been officially retired on branch `migrating-C#` (preserved on branch `legacy-python`). All backend testing, build validation, and QA must exclusively target `Backend.DotNet/`.
- Commits must strictly maintain scope isolation: use Conventional Commits with `test(dotnet)` or `test(<module>)` and never mix changes with frontend or legacy directories.

## 2. Core Gate Rule: Traceability & Coverage Mandatory
- **Rule**: No new route, state transition, or money calculation without corresponding tests and an entry in `docs/qa/traceability-matrix.md`.
- **Quality Gates**:
  - Domain layer line coverage $\ge 90\%$, branch coverage $\ge 85\%$.
  - Application layer line coverage $\ge 80\%$.
  - Mutation score $\ge 80\%$ on P0 areas (`SharedKernel/Money`, `Payments` state machine, `Receipts`, `Audit`, and `FourEyesRule`) via Stryker.NET.
  - Security test suite: 100% pass rate with zero skipped tests.
  - Smoke suite: execution duration under 2 minutes.

## 3. The 12 Mandatory Invariants (Traceability IDs)
1. **INV-MONEY-001 (Zero-Float Precision)**: Money is strictly stored and calculated as `long AmountMinor` (integer centavos). Floating-point (`float`, `double`, `decimal`) types are forbidden in money paths. Checked arithmetic ensures overflow safety.
2. **INV-VAT-001 (12% VAT Centavo Conservation)**: 
   $$\text{VatableSales} = \text{DivideHalfUp}(\text{GrossAmount}, 11200) \times 10000$$
   $$\text{VatAmount} = \text{GrossAmount} - \text{VatableSales}$$
   $$\text{VatableSales} + \text{VatAmount} \equiv \text{GrossAmount}$$
   Always holds to the exact centavo, satisfying BIR RMC No. 77-2024.
3. **INV-PAY-001 (Payment Terminal State Immutability)**: Once a `PaymentAttempt` reaches a terminal status (`Succeeded`, `Failed`, `Cancelled`, `Expired`), late webhooks or retries can never alter its state.
4. **INV-PAY-002 (Idempotency & Replay Protection)**: `(provider, idempotency_key)` and `(provider, provider_event_id)` must be globally unique under concurrent execution.
5. **INV-PAY-004 (Authoritative Pricing)**: Server-side catalog pricing is strictly authoritative; client-supplied unit prices are completely ignored.
6. **INV-AUD-001 (Audit Chain Integrity)**: Monotonically increasing sequence numbers, strict `prev_hash` linkage, reproducible `record_hash` using canonical JSON and `sha256:v2:`, serialized via PostgreSQL advisory lock `904210`.
7. **INV-AUD-002 (Append-Only Ledgers)**: Runtime database role (`app_runtime_role`) is denied `UPDATE` and `DELETE` on `audit_events`, `advance_ledger_entries`, and `closing_snapshots`.
8. **INV-LED-001 (Derived Advance Balance)**: Cash advance balance is derived dynamically, never stored:
   $$\text{Balance} = \sum \text{Releases} - \sum \text{ExpensesApplied} - \sum \text{CashReturns} + \sum \text{Adjustments}$$
9. **INV-SOD-001 (CPA Four-Eyes Separation of Duties)**:
   $$\text{Maker} \neq \text{Verifier} \neq \text{BankApprover} \neq \text{EvidenceVerifier}$$
   Forbidden actor pairings are rejected at domain, application, and database levels.
10. **INV-RCT-001..003 (Official Receipt Determinism)**: Format is `OR-YYYYMM-XXXX-HASH8`. Historical receipts are immutable. Hash digest is stable across re-serialization.
11. **INV-DOC-001 (Document Guardrails)**: Header magic byte verification (`%PDF-`, PNG, JPEG), strict 10 MB ceiling, and path traversal rejection (`..`, control characters, encoded separators).
12. **INV-SEC-001..00N (Security Infrastructure)**: Account lockout triggers after 5 failed attempts for 15 minutes. Session tokens stored hashed. HMAC comparison is constant-time. CSV formula injection neutralized (`=`, `+`, `-`, `@`). Dev-only routes (`sandbox/*`, `switch-user`, `act-as`) are excluded from Release builds.

## 4. Test Construction & Execution Rules
- **Naming Convention**: `Method_Scenario_ExpectedResult`.
- **Structure**: Arrange, Act, Assert. One logical concept per test method.
- **Traits**: Every test must have appropriate traits for selective running:
  - `[Trait("Req", "INV-VAT-001")]`
  - `[Trait("Category", "Unit" | "Property" | "Path" | "Integration" | "Security" | "Architecture" | "Smoke" | "Performance")]`
  - `[Trait("Priority", "P0" | "P1" | "P2" | "P3")]`
- **Determinism**: Zero wall-clock dependence (inject `FakeClock` with `Asia/Manila`), zero `Thread.Sleep`, deterministic seeded randomness.
- **Defect Governance**: When a defect is discovered, do NOT secretly alter production code. Write a failing test, tag with `[Trait("Category", "KnownDefect")]`, and register the incident in `docs/qa/defect-log.md`.

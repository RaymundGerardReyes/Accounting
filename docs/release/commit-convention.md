# Conventional Commit Guidelines & Scope Discipline

All commits in the R.G.E Accounting Platform must strictly adhere to the Conventional Commits 1.0.0 specification with strict functional scope isolation.

---

## 1. Commit Structure

```text
<type>(<scope>): <short summary>

[optional body explaining motivation and technical context]

[optional footer: BREAKING CHANGE: <explanation> or refs <issue-id>]
```

### Constraints
- **Header Max Length**: 100 characters.
- **Body Line Max Length**: 100 characters.
- **Subject**: Imperative mood, lowercase, no trailing period.
- **Breaking Changes**: Indicated with a `!` after type/scope (e.g., `feat(payments)!: ...`) or a `BREAKING CHANGE:` footer.

---

## 2. Allowed Types

| Type | When to Use |
| :--- | :--- |
| `feat` | New feature, capability, or endpoint |
| `fix` | Bug fix, regression correction, or calculation error |
| `perf` | Performance improvement or latency reduction |
| `refactor` | Code reorganization without functional or behavioral changes |
| `docs` | Documentation, ADR, or runbook additions |
| `test` | Unit, integration, architecture, or parity tests |
| `build` | Build system, MSBuild props, package manifests, or compiler flags |
| `ci` | GitHub Actions workflows, CI scripts, or automated gates |
| `chore` | Maintenance, `.gitignore`, release synchronization |
| `revert` | Reverting a previous commit |

---

## 3. Allowed Scopes

| Scope | Allowed Paths & Responsibilities |
| :--- | :--- |
| `web` | **Strictly `Frontend/` only**. Next.js pages, components, client state |
| `payments` | Payments module (PayMongo, order checkout, webhooks, idempotency) |
| `receipts` | Receipts module (BIR RMC 77-2024, QuestPDF, email dispatch) |
| `finance` | Finance module (Cash advances, 4-eyes disbursements, expense liquidation, ledger) |
| `identity` | Identity module (Authentication, sessions, CSRF, RBAC, SoD) |
| `audit` | Audit module (SHA-256 hash chaining, advisory locks, chain verifier) |
| `costalloc` | Cost Allocation module (Departmental cost tracking, budget caps) |
| `documents` | Documents module (Magic byte inspection, safe file storage) |
| `integrations` | Integrations module (Google Forms webhook ingestion, Google Sheets sync, SMTP) |
| `db` | Database migrations (`db/migrations/`), functions (`db/functions/`), roles, seeds |
| `deploy` | Docker, Kubernetes, NGINX, YARP strangler reverse proxy |
| `release` | Release metadata, version tags, compatibility matrix |
| `deps` | Dependency version bumps (`Directory.Packages.props`, lockfiles) |

---

## 4. Strict Scope Isolation Invariant

> [!IMPORTANT]
> A commit must **NEVER mix `Frontend/` and backend (`Backend/`, `Backend.DotNet/`, `db/`) paths in a single commit**, except when bumping monorepo version numbers under `chore(release)` or `build(deps)`.
>
> Commits with scope `web` must only touch files within `Frontend/`.

This rule guarantees clean cherry-picking of frontend improvements between the `.NET` and `Python` branches without pulling uncompiled or divergent backend code.

---

## 5. Realistic Commit Examples

### Example 1: Frontend Bugfix
```text
fix(web): prevent multiple payment submissions on double-click

Disable the PayMongo checkout button immediately upon initial submission
to eliminate accidental duplicate payment attempt generation.
```

### Example 2: Payments Backend Feature
```text
feat(payments): support QRPh dynamic webhook reconciliation

Process incoming payment.failed and qrph.expired webhook events and transition
PaymentAttempt aggregate root into terminal status.
```

### Example 3: Receipts Compliance Update
```text
feat(receipts): format BIR RMC 77-2024 official receipt breakdown

Ensure VATable sales, VAT amount (1200 bps), and VAT-exempt sales appear as
independent lines in generated QuestPDF invoice layouts.
```

### Example 4: Finance SoD Enforcement
```text
fix(finance): enforce four-eyes rule on bank disbursement approval

Assert that the bank approver actor ID cannot match either the maker ID
or the supervisor verifier ID in the disbursement pipeline.
```

### Example 5: Database Forward Migration
```text
feat(db): add outbox lease management columns to V0004 migration

Introduce locked_by_node and lease_expires_at columns to transaction_outbox
to prevent duplicate concurrent worker execution.
```

### Example 6: Breaking API Contract Change
```text
feat(payments)!: transition order amount to integer centavos in API payload

BREAKING CHANGE: The /api/v1/orders endpoint now requires amount_minor as an
integer number of Philippine centavos instead of floating-point PHP decimal.
```

### Example 7: Reverting a Commit
```text
revert: "feat(finance): auto-approve advances below 5000 PHP" (refs INC-1042)

Revert commit 89f41a2 to maintain mandatory supervisor review on all
Philippine cash advances regardless of amount threshold.
```


# Dual-Line Semantic Versioning & Milestone Release Policy

## 1. Scope & Topology

The R.G.E Accounting Platform operates across two distinct, long-lived technology lines:

```
Line A (New):     Next.js + TypeScript  +  C# .NET 10        (Backend.DotNet/)
Line B (Legacy):  Next.js + TypeScript  +  Python FastAPI    (Backend/)
```

Both lines share a unified Next.js frontend codebase located at `Frontend/`. To eliminate version collisions and maintain clean auditability across disparate runtimes, each line maintains its own independent SemVer tag namespace.

---

## 2. Tag Namespacing & SemVer 2.0.0 Principles

All release tags adhere strictly to Semantic Versioning 2.0.0 (`MAJOR.MINOR.PATCH`) prefixed by runtime line:

| Line | Release Tag Pattern | Pre-Release Patterns | Meaning |
| :--- | :--- | :--- | :--- |
| **Line A (.NET 10)** | `dotnet/vX.Y.Z` | `dotnet/vX.Y.Z-preview.N`, `dotnet/vX.Y.Z-rc.N` | Full-stack release of .NET backend + Frontend |
| **Line B (Python)** | `python/vX.Y.Z` | `python/vX.Y.Z-beta.N`, `python/vX.Y.Z-rc.N` | Full-stack release of Python backend + Frontend |

### Core SemVer Principles for the Migration:
1. **A Language Migration is NOT a MAJOR Bump**: SemVer measures compatibility of the public API contract, which remains fixed at `/api/v1`. If the C# .NET 10 line behaves identically to Python at the API edge, the rewrite is completely invisible to consumers. A MAJOR version means breaking public API contract changes.
2. **Initial Development Phase (`0.x.y`)**: Versions `0.y.z` represent initial development where anything may evolve internally. The .NET line remains on `0.x.y` until the full production cutover gate is passed.
3. **Milestones vs. Work-Session Counters**:
   - `0.1.0-preview.N` must never be used as a counter for completed work sessions or batches of commits. A version string must convey compatibility and production readiness, not a developer commit counter.
   - Untagged commits are automatically versioned by MinVer (e.g. `0.1.0-preview.4.5` relative to the latest tag), ensuring every build is uniquely identified without requiring a new Git tag.
4. **Pre-Release Identifiers (`-preview` / `-rc`)**: Reserved strictly for builds that are actively deployed to a named verification or staging environment for human or external automated testing. Never use them as internal work counters.

---

## 3. .NET 10 Migration Milestone Plan

A Git tag marks a **milestone**—something worth naming, testing, and returning to. The .NET 10 migration follows this authoritative milestone roadmap:

| Milestone Tag | Phase & Scope | Exit Criteria & Verification Requirement |
| :--- | :--- | :--- |
| **`dotnet/v0.1.0`** | **Foundation**: BuildingBlocks, SharedKernel, Domain Core, Host, DB Migrations, CI | Core architecture compiles with 0 warnings/errors; `ParityRunner` invariant checks pass (64/64); baseline DB migrations apply cleanly. |
| **`dotnet/v0.2.0`** | **Identity & Audit Modules** | `Rge.Identity` and `Rge.Audit` routes and domain services active; session management, CSRF, RBAC, and hash-chained audit logging verified. |
| **`dotnet/v0.3.0`** | **Payments & Receipts Modules** | Core checkout session creation, PayMongo webhook receiver, HMAC verification, transactional outbox, receipt number generation, and `sha256:v2` digests verified. |
| **`dotnet/v0.4.0`** | **Finance Module** | Cash advances, disbursements, 5 state machines, approval matrix/router, SoD four-eyes enforcement, expense reports, vendor records, and accounts payable active. |
| **`dotnet/v0.5.0`** | **Integrations, CostAllocation & Documents** | Feature complete. Google Forms ingestion, department cost allocation, and secure document evidence storage verified. |
| **`dotnet/v0.5.1`, `v0.5.2`** | **Parity Stabilization** | Bug fixes and edge-case resolutions discovered while running contract parity test suites against legacy baselines. |
| **`dotnet/v1.0.0-rc.1`, `rc.2`** | **Release Candidates** | Feature complete, deployed to staging/canary; canary traffic split verified; load and security penetration suites passing. |
| **`dotnet/v1.0.0`** | **Production Cutover Gate** | 100% route parity verified; cryptographic audit-chain verifier passing on historical data; Python backend decommissioned. |

---

## 4. Status of Historical Preview Tags

The four historical tags (`dotnet/v0.1.0-preview.1` through `dotnet/v0.1.0-preview.4`) were pushed during initial scaffolding:
- **Status: Internal Checkpoints (Immutable)**.
- Under Git and SemVer governance, tags are immutable and must **never be deleted, overwritten, or moved**.
- MinVer correctly treats `dotnet/v0.1.0` as higher than `dotnet/v0.1.0-preview.4`. Therefore, upon completion and verification of the Foundation milestone, `dotnet/v0.1.0` naturally supersedes them.

---

## 5. Tagging Rules & Release Guardrails (.NET Line)

The following six rules govern all tagging operations across the repository:

1. **`0.x.y` Invariant**: The .NET line stays on `0.x.y` until the `1.0.0` production gate is met (100% route parity with Python, audit-chain verifier passing, security suite passing, canary cutover approved).
2. **No Per-Session Tagging**: Do NOT create a tag after each work session or batch of commits. Intermediate builds are versioned automatically by MinVer.
3. **Milestone Authorization**: Create a tag ONLY at a named milestone listed in the milestone plan, and ONLY after explicit user approval of the exact tag name and commit SHA.
4. **Autonomous Agent Tagging Strictly Forbidden**: AI agents and automated scripts must NEVER run `git tag` or `git push --tags` autonomously. The agent prints an evidence-based tag proposal; the user reviews and executes the commands.
5. **SemVer In `0.x`**: In `0.x`, bump MINOR for a milestone or breaking change, and PATCH for a bug fix. Use `-preview` or `-rc` only for builds deployed to an external testing environment.
6. **Language Migration Invariant**: A language rewrite preserving `/api/v1` contracts is NOT a MAJOR bump. MAJOR bumps occur only upon public API contract breaks or at the `1.0.0` production gate.

---

## 6. Evidence-Based Release Gating (The Migration Ledger)

Tags are derived **strictly from evidence recorded in the Migration Ledger**, never from time spent or batches of code written.

### The Migration Ledger Structure:
- **Route Ledger**: [`docs/migration/route-ledger.md`](file:///e:/Accounting/docs/migration/route-ledger.md) & [`docs/migration/route-ledger.csv`](file:///e:/Accounting/docs/migration/route-ledger.csv)
- **Feature Ledger**: [`docs/migration/feature-ledger.md`](file:///e:/Accounting/docs/migration/feature-ledger.md)
- **Gap Report**: [`docs/migration/gap-report.md`](file:///e:/Accounting/docs/migration/gap-report.md)

### Status State Machine:
```
not-started  -->  implemented  -->  tested  -->  parity-verified  -->  accepted-difference
```
- `implemented`: Handler exists in `Backend.DotNet/` and compiles cleanly.
- `tested`: Module unit or integration tests pass.
- `parity-verified`: Dual-run comparison proves equivalent HTTP status, payload schema, error codes, and DB side effects (audit append).
- `accepted-difference`: Deliberate improvement or difference reviewed and approved by CPA/system owner.

A milestone tag proposal is authorized **only when 100% of the ledger rows belonging to that milestone are in `parity-verified` or `accepted-difference` status**.

---

## 7. Python Line Maintenance Policy
- The Python legacy line (`legacy-python`) is in **maintenance mode**.
- Only critical security updates and regression PATCH releases (`python/v0.9.x` or `python/v2.x.x`) are permitted.
- No new commercial or accounting feature development may occur exclusively on the Python line.

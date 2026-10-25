# Architecture & Runtime Compatibility Matrix

This matrix tracks every authoritative tag and historical checkpoint across both Line A (.NET 10) and Line B (Python), recording frontend parity, API contracts, database migrations, and certified rollback targets.

## Authoritative Releases

| Release Tag | Line | Commit SHA | API Contract | DB Schema Version | Status | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `v0.9.34` | Legacy Unified | `c053aff` | `v1` | `V0001` | Active Release | Baseline tag: 18-column Google Sheets schema & FormBuilder harmonization |
| `v0.9.33` | Legacy Unified | `1ac8350` | `v1` | `V0001` | Prior Release | Diagnostic checkout links to live hosted checkout |
| `v0.9.32` | Legacy Unified | `af4be20` | `v1` | `V0001` | Prior Release | Harden sendPaymentInstructionsEmail against undefined args |
| `v0.9.31` | Legacy Unified | `2fe5cee` | `v1` | `V0001` | Prior Release | Automated SemVer calculation and monorepo sync |
| `python/v0.9.34` | Line B (Python) | `0b81a2f` | `v1` | `V0001` | Maintenance | Namespaced continuation of legacy Python line |

---

## Historical Checkpoints (.NET Migration Line)

The following tags were pushed during the initial scaffolding of the .NET 10 Modular Monolith. Under the evidence-based versioning policy, they are classified as **internal checkpoints**, not milestone releases:

| Checkpoint Tag | Line | Commit SHA | API Contract | DB Schema | Status | Scope / Description |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `dotnet/v0.1.0-preview.1` | Line A (.NET 10) | `8afc72b` | `v1` | `V0001` | Checkpoint | Initial Modular Monolith scaffold, BuildingBlocks, Host |
| `dotnet/v0.1.0-preview.2` | Line A (.NET 10) | `114ae2c` | `v1` | `V0001` | Checkpoint | Four security pillars (IAM, AES-GCM, Edge, Ops) |
| `dotnet/v0.1.0-preview.3` | Line A (.NET 10) | `e2409ff` | `v1` | `V0001` | Checkpoint | PayMongo v1 checkout, webhooks, HMAC verification |
| `dotnet/v0.1.0-preview.4` | Line A (.NET 10) | `2f26abd` | `v1` | `V0001` | Checkpoint | Approval matrix & router, API filters, IUnitOfWork |

> [!NOTE]
> These checkpoint tags remain immutable in repository history. They are superseded by the upcoming **`dotnet/v0.1.0`** milestone release upon completion and verification of the Foundation exit criteria.

---

## Targeted Migration Milestone Releases (Planned)

Milestone tags are derived strictly when all rows in the [Migration Ledger](file:///e:/Accounting/docs/migration/route-ledger.md) for that module are verified:

| Planned Milestone | Line | Target Scope | Target Gate | Status |
| :--- | :--- | :--- | :--- | :--- |
| **`dotnet/v0.1.0`** | Line A (.NET 10) | Foundation (BuildingBlocks, Host, Invariants) | 64/64 invariant checks passed, 0 warnings/errors | Verification In Progress |
| **`dotnet/v0.2.0`** | Line A (.NET 10) | Identity & Audit Modules | Session, RBAC, hash-chain audit append parity | Pending Ledger Verification |
| **`dotnet/v0.3.0`** | Line A (.NET 10) | Payments & Receipts Modules | PayMongo checkout & webhook parity, BIR OR digest | Pending Ledger Verification |
| **`dotnet/v0.4.0`** | Line A (.NET 10) | Finance Module | 5 State machines, SoD four-eyes, expenses, AP | Pending Ledger Verification |
| **`dotnet/v0.5.0`** | Line A (.NET 10) | Integrations, CostAlloc, Documents | Google Forms, department allocations, files | Pending Ledger Verification |
| **`dotnet/v1.0.0-rc.1`** | Line A (.NET 10) | Release Candidate 1 | 100% route parity, load & security suites | Pending RC Gate |
| **`dotnet/v1.0.0`** | Line A (.NET 10) | Production Cutover Release | Audit verifier green on historical data, Python retires | Production Cutover Gate |

# Dual-Line Semantic Versioning Policy

## 1. Scope & Topology

The R.G.E Accounting Platform operates across two distinct, long-lived technology lines:

```
Line A (New):     Next.js + TypeScript  +  C# .NET 10        (Backend.DotNet/)
Line B (Legacy):  Next.js + TypeScript  +  Python FastAPI    (Backend/)
```

Both lines share a unified Next.js frontend codebase located at `Frontend/`. To eliminate version collisions and maintain clean auditability across disparate runtimes, each line maintains its own independent SemVer tag namespace.

---

## 2. Tag Namespacing Scheme

All release tags adhere strictly to Semantic Versioning 2.0.0 (`MAJOR.MINOR.PATCH`) prefixed by runtime line:

| Line | Release Tag Pattern | Pre-Release Patterns | Meaning |
| :--- | :--- | :--- | :--- |
| **Line A (.NET 10)** | `dotnet/vX.Y.Z` | `dotnet/vX.Y.Z-preview.N`, `dotnet/vX.Y.Z-rc.N` | Full-stack release of .NET backend + Frontend |
| **Line B (Python)** | `python/vX.Y.Z` | `python/vX.Y.Z-beta.N`, `python/vX.Y.Z-rc.N` | Full-stack release of Python backend + Frontend |

### Single Source of Truth: The Git Tag
- The **Git tag is the sole authoritative source of version information**.
- Manual editing of static version strings in code is strictly prohibited.
- `package.json` in root and `Frontend/package.json` remain pinned to `0.0.0-managed` to prevent synthetic frontend drift across branches. The runtime version is injected at build time from the Git tag.

---

## 3. SemVer Bump Table for Regulated Financial System

In an accounting system handling BIR-regulated Official Receipts and an immutable cryptographic audit hash chain, version increments follow strict criteria:

| Category | PATCH (`vX.Y.Z+1`) | MINOR (`vX.Y+1.0`) | MAJOR (`vX+1.0.0`) |
| :--- | :--- | :--- | :--- |
| **HTTP Routes** | Internal bug fixes, response latency optimizations | Non-breaking backward-compatible route additions | Route removals, path renames, altered request/response schema |
| **Payload Shapes** | Optional fields added to internal logs | New optional fields added to existing API contracts | Required fields added, field removals, renamed JSON properties |
| **Error Codes & Types** | Message wording fixes preserving existing error codes | New error codes added to existing `ErrorType` catalogs | Renamed or removed error codes, changed HTTP status mappings |
| **Integration Events** | Bug fixes to event payload serializers | New integration event types added to outbox | Renamed event types, removed payload fields, altered semantics |
| **Database Schema** | Non-functional index additions, comment updates | Expand-phase backward-compatible column/table additions | Contract-phase drops, column renames, nullability constraints |
| **Configuration / Env** | Optional logging or tuning environment variables | New environment variables with safe defaults | Mandatory environment variables without defaults |
| **Financial / Tax Logic** | Strictly forbidden | Non-breaking reporting categorization additions | **Any change** to VAT calculation (12%), EWT, or rounding rules |
| **State Machines** | Fixes to internal guard exceptions preserving states | New non-terminal states with valid transition paths | Modified terminal states, removed states, altered SoD checks |

> [!CAUTION]
> **Financial & Tax Treatment Invariant**: Any change to monetary arithmetic (`Money`), Philippine VAT calculation (1200 bps), Expanded Withholding Tax (EWT), or the 5-state cash advance transition table is considered breaking and **must receive a MAJOR or approved MINOR increment**. It can never be released under a PATCH tag.

---

## 4. Milestone Definiton: .NET Line `1.0.0`
The .NET 10 line (`dotnet/v1.0.0`) is achieved when and only when:
1. **100% Endpoint Parity**: All 112 API endpoints match legacy Python response contracts.
2. **Audit Chain Verification**: Golden Master verification scripts and `AuditChain.Verifier` prove mathematical equivalence with historical records.
3. **Automated Parity Gate**: `tests/Contract.Parity.Tests` passes against live or containerized baselines.

---

## 5. Python Line Maintenance Policy
- The Python legacy line (`legacy-python`) is in **maintenance mode**.
- Only critical security updates and regression PATCH releases (`python/v0.9.x` or `python/v2.x.x`) are permitted.
- No new commercial or accounting feature development may occur exclusively on the Python line.


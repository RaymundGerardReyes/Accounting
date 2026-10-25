# Architecture & Runtime Compatibility Matrix

This matrix tracks every authoritative tag across both Line A (.NET 10) and Line B (Python), recording frontend parity, API contracts, database migrations, and certified rollback targets.

| Release Tag | Line | Frontend Commit / SHA | API Contract | DB Schema Version | Min Compatible Previous Release | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `v0.9.34` | Legacy Unified | `c053aff` | `v1` | `V0001` | `v0.9.33` | Baseline tag: 18-column Google Sheets schema & FormBuilder harmonization |
| `v0.9.33` | Legacy Unified | `1ac8350` | `v1` | `V0001` | `v0.9.32` | Diagnostic checkout links to live hosted checkout |
| `v0.9.32` | Legacy Unified | `af4be20` | `v1` | `V0001` | `v0.9.31` | Harden sendPaymentInstructionsEmail against undefined args |
| `v0.9.31` | Legacy Unified | `2fe5cee` | `v1` | `V0001` | `v0.9.30` | Automated SemVer calculation and monorepo sync |

---

## Namespaced Release Tracking (Targeted Baselines)

The following entries document the planned transition into namespaced dual-line releases:

| Release Tag | Line | Frontend Commit / SHA | API Contract | DB Schema Version | Min Compatible Previous Release | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `dotnet/v0.1.0-preview.1` | Line A (.NET 10) | *(Pending Tag Gate)* | `v1` | `V0001` | N/A (Initial preview) | Initial Phase 0 .NET 10 Modular Monolith baseline |
| `python/v0.9.34` | Line B (Python) | `0b81a2f` | `v1` | `V0001` | `v0.9.33` | Namespaced continuation of legacy Python line |


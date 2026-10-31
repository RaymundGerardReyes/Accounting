# Frontend Synchronization & Drift Control Runbook

## 1. Overview & Dual-Line Architecture

The **R.G.E Accounting Platform** operates two concurrent, production-grade release lines sharing an identical Next.js 15.2.4 frontend:
- **Line A (.NET 10)**: `Backend/` hosted on branch `migrating-C#`.
- **Line B (FastAPI Legacy)**: `Backend/` hosted on branch `legacy-python`.

Because the UI codebase is ~95–100% identical across both lines, maintaining parity without duplicating UI development effort requires strict **drift control** and **asymmetric cherry-picking**.

```
        ┌─────────────────────────────────────────────────────────┐
        │                 Shared Next.js Frontend                 │
        │              (package.json: 0.0.0-managed)              │
        └────────────────────────────┬────────────────────────────┘
                                     │
                 ┌───────────────────┴───────────────────┐
                 ▼                                       ▼
    ┌─────────────────────────┐             ┌─────────────────────────┐
    │  Line A: migrating-C#   │             │ Line B: legacy-python   │
    │  Backend (.NET)         │             │ Backend (FastAPI)       │
    │  Tag: dotnet/vX.Y.Z     │             │ Tag: python/vX.Y.Z      │
    └─────────────────────────┘             └─────────────────────────┘
```

---

## 2. When to Synchronize

Synchronization MUST be performed under any of the following triggers:

1. **Before any release tag is cut** on either `migrating-C#` (`dotnet/v*`) or `legacy-python` (`python/v*`).
2. **Whenever a Frontend PR merges** into either long-lived line branch.
3. **Weekly scheduled sync** triggered by the automated CI drift audit (`ci-frontend-drift.yml`).

---

## 3. Ground Rules & Invariants

> [!IMPORTANT]
> **Single-Scope Rule**: A commit touching `Frontend/` MUST NEVER modify backend files (`Backend/`, `db/`). If a commit mixes scopes, it cannot be cleanly cherry-picked and must be split before merging.

> [!WARNING]
> **No Direct Merges Between Long-Lived Branches**: Never run `git merge legacy-python` into `migrating-C#` or vice versa. Doing so merges the backend implementations into each other, destroying line isolation. Always use `git cherry-pick -x` via synchronization branches (`sync/frontend-*`).

---

## 4. Manual Synchronization Procedure

### Step 1: Identify Unsynchronized Frontend Commits
Compare frontend commit histories between the source line (where work was done) and the target line:

```bash
# Example: Find Frontend commits in migrating-C# that are not in legacy-python
git fetch origin
git log --oneline legacy-python..migrating-C# -- Frontend/
```

### Step 2: Validate Commit Scope Isolation
Ensure all candidate commits touch **only** `Frontend/` files:
```powershell
./tools/versioning/check-commit-scope.ps1 -CommitRange "legacy-python..migrating-C#"
```

### Step 3: Create a Dedicated Synchronization Branch
Always perform the cherry-picks on an ephemeral sync branch:
```bash
git checkout legacy-python
git pull origin legacy-python
git checkout -b sync/frontend-$(date +%Y%m%d%H%M)
```

### Step 4: Cherry-Pick with Provenance (`-x`)
Preserve the original commit SHA reference using the `-x` flag:
```bash
git cherry-pick -x <COMMIT_SHA_1>
git cherry-pick -x <COMMIT_SHA_2>
```

### Step 5: Resolve Conflicts (Frontend Scope Only)
If git reports conflicts:
1. Resolve conflicting files inside `Frontend/` strictly.
2. Confirm no backend files are added or modified (`git status`).
3. Complete the cherry-pick: `git cherry-pick --continue`.

### Step 6: Verify Drift
Run the automated drift audit to ensure parity:
```powershell
./tools/versioning/check-frontend-drift.ps1 -SourceBranch "migrating-C#" -TargetBranch "HEAD"
```

### Step 7: Open Pull Request
Push the sync branch and open a PR into the target line branch:
```bash
git push -u origin sync/frontend-202610251500
# PR Title: sync(web): propagate frontend changes from migrating-C#
# Base branch: legacy-python
```

---

## 5. Automated Synchronization via Tooling

The repository provides `tools/versioning/sync-frontend.ps1` to automate this workflow safely.

### Dry-Run Audit (Default)
Inspect unpropagated commits without altering repository state:
```powershell
./tools/versioning/sync-frontend.ps1 -SourceBranch "migrating-C#" -TargetBranch "legacy-python"
```

### Automated Branch Creation & Cherry-Picking
When verified, invoke with `-Apply`:
```powershell
./tools/versioning/sync-frontend.ps1 -SourceBranch "migrating-C#" -TargetBranch "legacy-python" -Apply
```
This cmdlet:
1. Validates clean working tree.
2. Analyzes commit candidate scopes; halts if any mixed-scope commit is detected.
3. Creates `sync/frontend-<timestamp>`.
4. Cherry-picks commits with `-x`.
5. Executes `check-frontend-drift.ps1` to confirm alignment.

---

## 6. Drift Allowlist (`.frontend-drift-allowlist`)

Under rare circumstances, the frontend on one line may require temporary, intentional divergence (e.g. feature-flagged UI during backend API cutover).
Such differences must be explicitly registered in [`.frontend-drift-allowlist`](file:///e:/Accounting/.frontend-drift-allowlist) with an associated ADR or issue ticket.

Any un-allowlisted drift immediately fails PR CI pipelines via `.github/workflows/ci-frontend-drift.yml`.

---

## 7. Ownership & Escalation

- **Primary Owner**: Lead Frontend Engineer / Release Engineer.
- **Review Requirement**: Any sync PR requires review and approval from at least one engineer from the corresponding backend line.
- **SLA**: Frontend sync PRs must be merged within 24 hours of creation to prevent merge debt.


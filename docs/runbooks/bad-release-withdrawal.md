# Runbook: Bad Release Withdrawal & Deprecation

## 1. Cardinal Invariant: Git Tags Are Immutable

> [!CRITICAL]
> **NEVER DELETE OR MOVE A PUBLISHED GIT TAG.**
> Deleting or re-pointing a tag like `dotnet/v0.1.0-preview.1` breaks downstream build caching, Docker image digest references, audit trail reproducibility, and team git clones (`fatal: tag '...' already exists`).

When a release is found to have severe defects, security vulnerabilities, or data integrity flaws, it must be **formally withdrawn** and superseded by a subsequent PATCH release.

---

## 2. Withdrawal Procedure (Step-by-Step)

### Step 1: Mark the Release as Withdrawn in GitHub Releases
1. Navigate to **GitHub Repository** $\rightarrow$ **Releases**.
2. Locate the faulty release tag (e.g. `dotnet/v0.1.2` or `python/v0.9.35`).
3. Click **Edit Release**:
   - Prepend `[WITHDRAWN - DO NOT DEPLOY]` to the Release Title.
   - Insert an incident alert box at the top of the description:
     ```markdown
     > [!CAUTION]
     > **WITHDRAWN DUE TO INCIDENT INC-2026-XXXX**
     > This release contains a critical regression in payment fee calculation.
     > Do not deploy to staging or production.
     > Superseded by: `dotnet/v0.1.3` (or `python/v0.9.36`).
     ```
   - Check the **This is a pre-release** checkbox to prevent automated ingestion by package managers or CD tools.
   - Save changes.

### Step 2: Update the Authoritative Compatibility Matrix
Open [`docs/release/compatibility-matrix.md`](file:///e:/Accounting/docs/release/compatibility-matrix.md) and update the status column of the withdrawn tag:

```markdown
| `dotnet/v0.1.2` | `0.1.2` | `migrating-C#` | `V0004` | `v1` | `15.2.4` | **Withdrawn (INC-2026-1025)** |
```

Commit this update:
```bash
git checkout migrating-C#
git commit -m "docs(release): mark dotnet/v0.1.2 as withdrawn due to INC-2026-1025"
```

### Step 3: Revert the Regression on the Branch
Use [revert-a-bad-commit.md](file:///e:/Accounting/docs/runbooks/revert-a-bad-commit.md) to cleanly revert the regression commit:
```bash
git revert <BAD_COMMIT_SHA> --no-edit
git push origin migrating-C#
```

### Step 4: Cut a New PATCH Release
Cut the next PATCH tag immediately:
```bash
# Example for .NET line:
NEW_TAG="dotnet/v0.1.3"
git tag -a "$NEW_TAG" -m "release: withdraw and supersede v0.1.2 (INC-2026-1025)"
git push origin "$NEW_TAG"
```

The automated CI/CD pipeline (`cd-release-dotnet.yml` or `cd-release-python.yml`) will build, test, and publish the clean PATCH release.


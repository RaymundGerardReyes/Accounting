# Runbook: Urgent Production Hotfix Procedure

## 1. Trigger & Qualification Criteria
A **Hotfix** is an out-of-band release permitted **only** for critical production incidents:
- Active security vulnerability (CVE / ASVS violation).
- Critical data corruption or financial calculation error.
- Total outage of payment webhook processing or user authentication.

For all non-critical bug fixes, follow the normal feature branch $\rightarrow$ PR $\rightarrow$ mainline cycle.

---

## 2. Hotfix Workflow Matrix

```
                 Production Regressing Tag (e.g. dotnet/v0.1.0)
                                    │
                                    ▼
                     Branch: hotfix/dotnet-0.1.1
                                    │
                  ┌─────────────────┴─────────────────┐
                  ▼                                   ▼
         Backend Fix Only                     Frontend Fix
                  │                                   │
                  │                          Cherry-pick to
                  │                          alternate line
                  │                                   │
                  └─────────────────┬─────────────────┘
                                    ▼
                      Verify & Cut Tag: dotnet/v0.1.1
                                    │
                  ┌─────────────────┴─────────────────┐
                  ▼                                   ▼
          Deploy to Production                Merge back to
          (cd-release-dotnet)                 migrating-C#
```

---

## 3. Step-by-Step Hotfix Execution

### Step 3.1: Create Hotfix Branch from the Release Tag
Never branch a hotfix from dirty or unreleased HEAD. Always branch from the exact tag in production:

```bash
# Example: Hotfix for .NET Line
git fetch --tags
git checkout -b hotfix/dotnet-0.1.1 tags/dotnet/v0.1.0-preview.1

# Example: Hotfix for Python Line
git checkout -b hotfix/python-0.9.35 tags/python/v0.9.34
```

### Step 3.2: Implement Minimal, Focused Remediation
Apply the minimal fix required to resolve the incident:
- Do **not** refactor surrounding code.
- Do **not** mix frontend and backend changes in a single commit!

### Step 3.3: Commit Using Conventional Commits
```bash
git commit -m "fix(payments): correct payment fee deduction rounding

Closes #INC-2026-1025"
```

Verify scope isolation:
```powershell
./tools/versioning/check-commit-scope.ps1 -CommitRange "HEAD~1..HEAD"
```

### Step 3.4: Special Handling for Frontend Hotfixes
If the hotfix modified `Frontend/`:
1. Verify that `Frontend/package.json` remains `0.0.0-managed`.
2. Port the commit immediately to the alternate line:
   ```powershell
   ./tools/versioning/sync-frontend.ps1 -SourceBranch "hotfix/dotnet-0.1.1" -TargetBranch "legacy-python" -Apply
   ```
3. Audit drift:
   ```powershell
   ./tools/versioning/check-frontend-drift.ps1 -SourceBranch "hotfix/dotnet-0.1.1" -TargetBranch "HEAD"
   ```

### Step 3.5: Run Pre-Flight Validation
```powershell
# For .NET line:
dotnet test Backend/Rge.Accounting.slnx --configuration Release

# For Python line:
cd Backend && pytest
```

### Step 3.6: Tag the Hotfix Release
Tag with the incremented PATCH version:

```bash
# Example: .NET line
git tag -a "dotnet/v0.1.1" -m "release(hotfix): fix payment fee deduction rounding (INC-2026-1025)"
git push origin "hotfix/dotnet-0.1.1"
git push origin "dotnet/v0.1.1"
```

The GitHub Actions CD pipeline (`cd-release-dotnet.yml` or `cd-release-python.yml`) will automatically trigger, build the release artifact, run tests, and publish the release.

### Step 3.7: Back-Merge into Mainline
Merge the hotfix back into the main line branch so future releases include the fix:

```bash
git checkout migrating-C#  # or legacy-python
git pull origin migrating-C#
git merge hotfix/dotnet-0.1.1 -m "chore(release): merge hotfix/dotnet-0.1.1 into migrating-C#"
git push origin migrating-C#

# Delete local and remote hotfix branch
git branch -d hotfix/dotnet-0.1.1
git push origin --delete hotfix/dotnet-0.1.1
```


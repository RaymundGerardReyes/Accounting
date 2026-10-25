# Runbook: Revert a Bad Commit Cleanly

## 1. Principles & Ground Rules

1. **Never Rebase or Force-Push Shared Branches**: Always append a forward `git revert` commit to `migrating-C#`, `legacy-python`, or `main`.
2. **Preserve Scope Isolation**: The revert commit must touch **only** the paths affected by the original commit. Never mix frontend and backend fixes into a single revert commit.
3. **Conventional Commit Syntax**: The commit message must follow:
   ```
   revert(<scope>): revert "<original commit message>"
   
   Refs: <original-commit-sha>
   Reason: <brief explanation of regression>
   ```

---

## 2. Reverting a Backend-Only Commit

### Step 1: Locate the Faulty Commit
```bash
git checkout migrating-C#  # or legacy-python
git pull origin migrating-C#
git log -n 5 --oneline -- Backend.DotNet/
```
Example target commit: `a1b2c3d` (`fix(payments): alter webhook retry backoff`)

### Step 2: Execute Git Revert
```bash
# Clean non-interactive revert
git revert a1b2c3d --no-edit

# Or customize commit message with reason:
git revert a1b2c3d -e
```
Recommended commit message format:
```
revert(payments): revert "fix(payments): alter webhook retry backoff"

This reverts commit a1b2c3d4e5f6...
Reason: Caused unexpected SQL deadlock on payment_attempts table under load.
```

### Step 3: Validate Scope
```powershell
./tools/versioning/check-commit-scope.ps1 -CommitRange "HEAD~1..HEAD"
```

### Step 4: Push
```bash
git push origin migrating-C#
```

---

## 3. Reverting a Frontend-Only Commit

Because frontend code is mirrored between `migrating-C#` and `legacy-python`, a frontend revert must land on both lines.

### Step 1: Revert on the Originating Branch
```bash
git checkout migrating-C#
git pull origin migrating-C#
git revert <FRONTEND_COMMIT_SHA> --no-edit
git push origin migrating-C#
```

### Step 2: Validate Scope Isolation
Verify the revert touched **only** `Frontend/`:
```powershell
./tools/versioning/check-commit-scope.ps1 -CommitRange "HEAD~1..HEAD"
```

### Step 3: Propagate the Revert to the Alternate Line
Run the frontend synchronizer to port the revert commit to `legacy-python`:
```powershell
./tools/versioning/sync-frontend.ps1 -SourceBranch "migrating-C#" -TargetBranch "legacy-python" -Apply
```

### Step 4: Verify Zero Drift
```powershell
./tools/versioning/check-frontend-drift.ps1 -SourceBranch "legacy-python" -TargetBranch "migrating-C#"
```

---

## 4. Reverting a Merge Commit
If a PR was merged that needs to be completely undone:
```bash
# Revert to mainline parent (-m 1)
git revert -m 1 <MERGE_COMMIT_SHA>
```
> [!NOTE]
> If re-introducing the feature later, you must revert the revert before applying further changes.


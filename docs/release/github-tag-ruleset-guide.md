# GitHub Tag Protection & Ruleset Configuration Guide

Because local Git hooks (such as `.githooks/pre-push`) can be bypassed locally using `git push --no-verify`, server-side tag protection on GitHub provides defense-in-depth against accidental or autonomous tag creation.

---

## Recommended GitHub Repository Ruleset

### Step 1: Navigate to Repository Rulesets
1. Open your repository on GitHub: `https://github.com/RaymundGerardReyes/Accounting`
2. Click **Settings** $\rightarrow$ **Rules** $\rightarrow$ **Rulesets** (in the left sidebar).
3. Click **New ruleset** $\rightarrow$ **New tag ruleset**.

### Step 2: Configure Ruleset Basics
- **Ruleset Name**: `Protect Release & Checkpoint Tags`
- **Enforcement Status**: `Active`

### Step 3: Define Target Tags
Under **Target tags**, select **Include by fnmatch pattern**:
- `dotnet/v*`
- `python/v*`
- `v*`

### Step 4: Define Bypass List
- Under **Bypass list**, add:
  - **Repository Admin** (User / Role): Set to yourself (`RaymundGerardReyes`) with `Always allow`.
  - **CI / GitHub Actions**: (If automated releases are orchestrated via GitHub Actions workflows).
  - Explicitly exclude automated bots or personal access tokens without 2FA.

### Step 5: Enable Rules
Check the following protections:
1. [x] **Restrict creations**: Only allowed bypass actors can create tags matching `dotnet/v*` and `python/v*`.
2. [x] **Restrict updates**: Prevent moving or re-pointing existing tags (enforces tag immutability).
3. [x] **Restrict deletions**: Prevent deleting historical release and checkpoint tags.

Click **Create** or **Save changes**.

---

## Local Emergency / Manual Tag Push Runbook

When you have reviewed the evidence in `docs/migration/route-ledger.md` and explicitly decide to push an authorized milestone tag:

```powershell
# 1. Ensure you are on the clean migration branch:
git status

# 2. Set the bypass environment variable in your session:
$env:ALLOW_TAG_PUSH = "1"

# 3. Push the specific tag to origin:
git push origin dotnet/v0.X.0

# 4. Clean up the environment variable:
Remove-Item Env:ALLOW_TAG_PUSH
```

---
name: git-auto-workflow
description: >-
  Automate Git staging, Conventional Commits by functional scope, and Semantic
  Version release tagging and incrementing in E:\Accounting.
---

# Git Auto-Workflow & Release Tagging Skill

Use this skill when staging, committing, bumping versions, or creating Semantic Version release tags in `E:\Accounting`.

## 1. Quick Inspection by Functional Scope
Inspect the working tree grouped by functional scope:
```powershell
python scripts/git_workflow.py status
```
Or using native Git:
```powershell
git status --short
git diff --stat
```

## 2. Version Inspection & Automated Increments
Check current version status and projected increments:
```powershell
python scripts/git_workflow.py version
```
Output displays:
- Authoritative latest Git release tag
- Next patch release candidate (`vX.Y.Z+1`)
- Next minor release candidate (`vX.Y+1.0`)
- Next major release candidate (`vX+1.0.0`)
- Synchronized versions across `package.json`, `Frontend/package.json`, and `VERSION`

## 3. Automated Version Bumping & File Synchronization
Synchronize version files across the monorepo without committing:
```powershell
# Bump patch (e.g. v0.9.30 -> v0.9.31)
python scripts/git_workflow.py bump --type patch

# Bump minor (e.g. v0.9.30 -> v0.10.0)
python scripts/git_workflow.py bump --type minor

# Set an explicit target version
python scripts/git_workflow.py bump -v v1.0.0 --stage
```

## 4. Scoped Staging & Commit Commands
Stage and commit by functional scope using the automation helper:
```powershell
# Stage and commit repository configuration:
python scripts/git_workflow.py add --scope repo
python scripts/git_workflow.py commit --type chore --scope repo -m "update root gitignore and project readme"

# Stage and commit backend changes:
python scripts/git_workflow.py add --scope backend
python scripts/git_workflow.py commit --type feat --scope backend -m "implement payment refund endpoint"

# Stage and commit frontend changes:
python scripts/git_workflow.py add --scope frontend
python scripts/git_workflow.py commit --type feat --scope frontend -m "add invoice download button"

# Stage and commit tests:
python scripts/git_workflow.py add --scope test
python scripts/git_workflow.py commit --type test --scope backend -m "cover refund webhook verification"

# Stage and commit documentation:
python scripts/git_workflow.py add --scope docs
python scripts/git_workflow.py commit --type docs --scope git -m "update staging workflow documentation"

# Stage and commit versioning scripts:
python scripts/git_workflow.py add --scope versioning
python scripts/git_workflow.py commit --type feat --scope versioning -m "add automated semver bump engine"
```

## 5. Pre-Commit Security Audit
Verify no sensitive credentials or binary databases are staged:
```powershell
python scripts/git_workflow.py check-secrets
```

## 6. Full Automated Release Pipeline
Calculate the next SemVer increment, synchronize all package files, stage, commit, and create an annotated tag in a single command:
```powershell
# Automated patch release (e.g. v0.9.30 -> v0.9.31):
python scripts/git_workflow.py release --type patch -m "Fix Google Forms URL resolution and remove personal emails"

# Automated release with immediate push to GitHub:
python scripts/git_workflow.py release --type patch -m "Fix Google Forms URL resolution" --push

# Manual tagging fallback:
python scripts/git_workflow.py tag v0.9.31 -m "Release v0.9.31: Live Payment Platform Update"
```

## 7. Publishing to GitHub
Inspect commit history and tags before pushing:
```powershell
git log --oneline --graph -n 10
git tag -l -n
git push -u origin main --tags
```

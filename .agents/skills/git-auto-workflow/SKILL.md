---
name: git-auto-workflow
description: >-
  Automate Git staging, Conventional Commits by functional scope, and Semantic
  Version release tagging in E:\Accounting.
---

# Git Auto-Workflow & Release Tagging Skill

Use this skill when staging, committing, or creating Semantic Version release tags in `E:\Accounting`.

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

## 2. Scoped Staging & Commit Commands
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
```

## 3. Pre-Commit Security Audit
Verify no sensitive credentials or binary databases are staged:
```powershell
python scripts/git_workflow.py check-secrets
```

## 4. Semantic Version Release Tagging
Create an annotated release tag adhering to SemVer 2.0.0:
```powershell
python scripts/git_workflow.py tag v0.1.0 -m "Release v0.1.0: Live Payment Orchestration Platform"
```
Or using native Git:
```powershell
git tag -a v0.1.0 -m "Release v0.1.0: Live Payment Orchestration Platform"
```

## 5. Publishing to GitHub
Inspect commit history and tags before pushing:
```powershell
git log --oneline --graph -n 10
git tag -l -n
git push -u origin main --tags
```

# Git Commit & Staging Documentation for the Accounting Project

Based on our implementation of automated Semantic Versioning 2.0.0 and multi-tier architecture (FastAPI backend + Next.js frontend + PayMongo live payment orchestration), this guide defines our Git contribution, staging, and commit workflow separating changes by functional area: `repo`, `backend`, `frontend`, `test`, `docs`, `agents`, and `versioning`.

The objective is to ensure every commit is understandable, reviewable, testable, and correctly associated with the affected part of your application.

---

## 1. The Fundamental Rule: Stage Only What You Intend to Commit

Git separates file management into three distinct areas:

```
Working Directory           Staging Area (Index)           Local Git History
[Modified/Created Files] ──(git add <files>)──> [Staged Snapshot] ──(git commit)──> [Permanent Commit]
```

- **Working directory**: All modified, created, and deleted files.
- **Staging area (index)**: Only the changes explicitly selected for the next commit snapshot.
- **Local Git history**: A recorded commit snapshot with an author, message, and unique hash.

> [!IMPORTANT]
> `git add` does not publish code, and `git commit` does not automatically push to GitHub. Furthermore, a commit records only what is in the staging area, never your entire working directory unless you staged it.

---

## 2. Conventional Commits 1.0.0 Specification

We use the [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/) specification. Every commit message must follow this format:

```text
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

### Commit Types
| Type | Purpose | SemVer Impact |
|---|---|---|
| `feat` | New feature or capability | MINOR (e.g. `0.1.0` -> `0.2.0`) |
| `fix` | Bug fix or error resolution | PATCH (e.g. `0.1.0` -> `0.1.1`) |
| `chore` | Maintenance, configuration, dependencies, `.gitignore` | None / Patch |
| `docs` | Documentation updates | None |
| `test` | Adding or updating tests | None |
| `refactor`| Code restructuring without feature or bug change | None |
| `style` | Formatting, whitespace, semicolon fixes | None |
| `perf` | Performance improvement | Patch |

*Note: Any type with `!` (e.g., `feat!:`) or `BREAKING CHANGE:` footer represents a breaking change and triggers a MAJOR release (`1.0.0` -> `2.0.0`).*

### Scopes for the Accounting Project
| Scope | Identifies |
|---|---|
| `backend` | FastAPI application, database models, payment providers, orchestration services |
| `frontend` | Next.js App Router, React components, Tailwind styling, accessibility, UI hooks |
| `test` | Pytest test suites, unit tests, mock fixtures, verification scripts |
| `repo` | Root configuration, `.gitignore`, license, repository setup |
| `docs` | Technical documentation, user manuals, git workflows |
| `agents` | AI workspace rules, skills, and customization hooks (`.agents/`) |
| `versioning`| Release tagging scripts, version bump automation (`scripts/`) |

---

## 3. Scoped Staging Commands by Area

Always run these commands from the repository root (`E:\Accounting`):

### A. Repository & Configuration Changes
```powershell
git add .gitignore README.md
git diff --cached
git commit -m "chore(repo): update root gitignore and documentation"
```

### B. Backend Changes
```powershell
git add Backend/app.py Backend/services/ Backend/providers/
git diff --cached
git commit -m "feat(backend): add payment refund reconciliation endpoint"
```

### C. Frontend Changes
```powershell
git add Frontend/app/ Frontend/components/ Frontend/lib/
git diff --cached
git commit -m "feat(frontend): improve payment status polling indicator"
```

### D. Test Changes
```powershell
git add Backend/test_orchestration.py
git diff --cached
git commit -m "test(backend): add coverage for expired session reconciliation"
```

### E. Documentation Changes
```powershell
git add docs/
git diff --cached
git commit -m "docs(git): document conventional commit scopes and release tagging"
```

### F. Agent Rules and Skills Changes
```powershell
git add .agents/
git diff --cached
git commit -m "feat(agents): add workspace rules for payment idempotency"
```

---

## 4. How to Commit Only Part of a File (`git add -p`)

When a file contains multiple unrelated changes (for example, a bug fix and a new feature in `app.py`), use interactive hunk staging:

```powershell
git add -p Backend/app.py
```
Git will prompt for each hunk:
- `y`: Stage this hunk
- `n`: Do not stage this hunk
- `s`: Split the hunk into smaller hunks
- `e`: Manually edit the hunk

Verify the separation before committing:
```powershell
git diff --cached   # Shows what will be committed
git diff            # Shows what remains unstaged
```

---

## 5. Complete Step-by-Step Workflow for Every Change

```powershell
# 1. Confirm your active branch
git branch --show-current

# 2. Inspect your working tree
git status --short
git diff

# 3. Stage only the relevant files
git add path/to/changed_file.py

# 4. Verify exactly what is staged
git diff --cached
git status --short

# 5. Run the relevant automated tests
python -m pytest Backend/test_orchestration.py

# 6. Commit with a Conventional Commit message
git commit -m "feat(backend): add PayMongo webhook HMAC verification"

# 7. Verify the recorded commit
git show --stat --oneline HEAD
```

---

## 6. Semantic Versioning & Release Tagging

We use **Semantic Versioning 2.0.0** (`vMAJOR.MINOR.PATCH`):
- `v0.1.0`: Initial feature-complete release with Cost Analysis and PayMongo Live Payment Orchestration.
- `v0.2.0`: Minor feature additions (backward-compatible).
- `v0.1.1`: Patch bug fixes.
- `v1.0.0`: Production-ready release.

### Creating an Annotated Release Tag
```powershell
# Create tag with annotation
git tag -a v0.1.0 -m "Release v0.1.0: Cost Analysis and PayMongo Payment Orchestration Platform"

# List tags with annotations
git tag -l -n

# Push tags to GitHub
git push origin --tags
```

---

## 7. Mandatory Repository Rules

1. **One Logical Change per Commit**: Keep backend, frontend, test, and documentation work separated.
2. **Stage Explicitly**: Prefer `git add path/to/file` over `git add .` or `git add -A`.
3. **Never Commit Secrets**: Check that `.env`, private keys, and credentials are never staged.
4. **Do Not Commit Generated Artifacts**: Ensure `.db`, `__pycache__/`, `.pytest_cache/`, and `.next/` remain ignored.
5. **Verify Before Pushing**: Always run tests and inspect `git log` before running `git push`.

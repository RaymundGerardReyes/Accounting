# Branching and Long-Lived Line Governance Policy

## 1. Long-Lived Branch Topology

The repository maintains two long-lived branches corresponding to the active runtime stacks:

| Branch Name | Runtime Line | Purpose | Target Production URL |
| :--- | :--- | :--- | :--- |
| `main` / `migrating-C#` | **Line A (.NET 10)** | Primary development of .NET 10 Modular Monolith + Next.js | Future Production Host |
| `legacy-python` | **Line B (Python)** | Stable production line running FastAPI + Next.js | Current Live Production Host |

---

## 2. GitHub Protected Branch Settings (Manual Configuration)

To enforce governance and prevent unreviewed code from entering production, configure the following protection rules in the GitHub repository settings:

### Rules for `main` and `legacy-python`
- [x] **Require pull request reviews before merging**:
  - Require at least 1 approving review.
  - Dismiss stale pull request approvals when new commits are pushed.
- [x] **Require status checks to pass before merging**:
  - Require branch to be up to date before merging.
  - Required checks: `ci-build-test`, `ci-commitlint`, `ci-frontend-drift`.
- [x] **Require conversation resolution before merging**.
- [x] **Require signed commits**.
- [x] **Require linear history** (strictly enforce **Squash and Merge**).
- [x] **Do not allow bypassing the above settings**.
- [x] **Block force pushes**: Strictly checked (no `git push --force`).
- [x] **Block branch deletions**: Strictly checked.

---

## 3. Tag Protection Rules

In GitHub Repository Settings $\rightarrow$ **Tags** $\rightarrow$ **Protected tags**, configure:
- `dotnet/v*` $\rightarrow$ Allowed creators: Maintainers & Release Automation Service Account only.
- `python/v*` $\rightarrow$ Allowed creators: Maintainers & Release Automation Service Account only.

---

## 4. Merge Policy & Conventional Commit Enforcement

- **Squash and Merge Only**: All feature and bugfix branches must be squash-merged into the respective long-lived branch.
- **Commit Format**: The squash commit subject must follow Conventional Commits 1.0.0 (`type(scope): description`).
- **Never Mix Scopes**: Frontend changes (`Frontend/`) must never be combined with backend or database changes in the same commit.
- **Release Authorization**: Only designated Lead Engineers / Release Managers may trigger or approve release PRs.


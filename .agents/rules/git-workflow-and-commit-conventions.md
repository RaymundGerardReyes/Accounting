---
description: Repository rules for Conventional Commits, scoped staging, secret prevention, and SemVer tagging
---

# Git Contribution & Commit Rules

1. **Conventional Commits 1.0.0 Formatting**:
   - Every commit message must strictly follow: `<type>(<scope>): <description>`.
   - **Allowed Types**:
     - `feat`: New feature or capability.
     - `fix`: Bug fix or error resolution.
     - `chore`: Maintenance, dependencies, tooling, `.gitignore`.
     - `docs`: Documentation updates.
     - `test`: Adding or updating test suites.
     - `refactor`: Code restructuring without functional change.
     - `style`: Whitespace, formatting, semicolon corrections.
     - `perf`: Performance optimizations.
   - **Allowed Scopes**:
     - `repo`: Repository configuration, `.gitignore`, licensing, root setup.
     - `backend`: FastAPI routes, database models, payment providers, orchestration services.
     - `frontend`: Next.js pages, React components, Tailwind CSS styles, accessibility, UI hooks.
     - `test`: Test cases, fixtures, verification scripts.
     - `docs`: Technical documentation, workflow guides.
     - `agents`: Custom agent rules, skills, and configuration hooks (`.agents/`).
     - `versioning`: Version bump scripts, release automation (`scripts/`).
     - `content-pack`: Domain metadata and accounting catalogs.
   - **Example**: `feat(backend): implement FastAPI payment orchestration and PayMongo adapter`.

2. **Scoped Staging Discipline**:
   - Never stage unrelated changes together using blanket `git add .` or `git add -A`.
   - Stage changes explicitly by functional area or use `git add -p` for interactive hunk-level staging.
   - Keep tests with the corresponding behavior or in a dedicated `test(<scope>)` commit.

3. **Secret & Artifact Prevention**:
   - Never commit `.env`, private API keys, credentials, or authentication tokens.
   - Never commit binary databases (`*.db`, `*.sqlite`, `accounting_payments.db`).
   - Never commit Python caches (`__pycache__/`, `*.pyc`), test caches (`.pytest_cache/`), or build traces (`.next/`).
   - Ensure the root `.gitignore` remains active and enforced.

4. **Semantic Release Tagging (SemVer 2.0.0)**:
   - Release tags must follow Semantic Versioning 2.0.0 (`vMAJOR.MINOR.PATCH`).
   - Tags must always be annotated (`git tag -a vX.Y.Z -m "..."`).
   - Before tagging, ensure the working tree is clean and all automated test suites pass.

5. **Major Migration & Legacy Preservation Branching Invariant**:
   - Before executing any major framework, runtime, or architectural language rewrite (such as migrating Python FastAPI to C# ASP.NET Core), create a dedicated preservation branch (`legacy-<runtime>`, e.g. `legacy-python`) pinned to the last verified release commit/tag.
   - All migration and rewrite development must occur on an isolated migration branch (`migrating-<target>`, e.g. `migrating-C#`).
   - The production branch (`main`) must remain untouched, clean, and deployable until 100% functional, contract, and test parity is proven on the migration branch.
   - Preserved legacy branches serve as the authoritative baseline for regression testing and side-by-side parity benchmarking.

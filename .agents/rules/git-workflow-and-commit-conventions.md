---
description: Repository rules for Conventional Commits, scoped staging, secret prevention, SemVer tagging, and migration release guardrails
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
     - `dotnet`: C# ASP.NET Core (.NET 10) Modular Monolith backend (`Backend.DotNet/`).
     - `frontend`: Next.js pages, React components, Tailwind CSS styles, accessibility, UI hooks.
     - `test`: Test cases, fixtures, verification scripts.
     - `docs`: Technical documentation, workflow guides.
     - `agents`: Custom agent rules, skills, and configuration hooks (`.agents/`).
     - `versioning`: Version bump scripts, release automation (`scripts/`).
     - `migration`: Migration ledgers, route parity exporters, compatibility verifiers.
     - `content-pack`: Domain metadata and accounting catalogs.
   - **Example**: `feat(dotnet): implement ApprovalMatrix and Router with four-eyes SoD enforcement`.

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

6. **Dual-Line Release Discipline & Scope Isolation**:
   - Commits must NEVER mix changes to `Frontend/` with backend directories (`Backend/`, `Backend.DotNet/`, `db/`), except for `chore(release)` or `build(deps)` synchronization commits.
   - Commits with scope `web` must strictly and exclusively modify files within the `Frontend/` directory.
   - Release tags are strictly immutable. Autonomous agents and developers must NEVER create, move, or delete Git tags without explicit user approval at designated release gates.
   - Release tags must adhere to namespaced line identifiers (`dotnet/vX.Y.Z` and `python/vX.Y.Z`).

7. **Windows Sandbox Execution Boundaries for Git Operations**:
   - In sandboxed execution environments on Windows, operations modifying the `.git` directory (`git add`, `git commit`, `git tag`, `git checkout`) fail on `.git/index.lock` creation with `Permission denied`.
   - Read-only inspections (`git status`, `git log`, `git diff`, `git rev-parse`) must always run inside the standard sandbox first.
   - Modifying Git commands (`add`, `commit`, `tag`, `checkout`, `stash`) must run with `BypassSandbox: true` to avoid permission errors.

8. **MSBuild Single-Node Execution in Restricted/Sandboxed Environments**:
   - In sandboxed or containerized environments on Windows, default multi-node MSBuild worker IPC (`-maxcpucount`) and persistent MSBuild server daemons fail silently during `_GetAllRestoreProjectPathItems` due to named-pipe/socket restrictions.
   - All `dotnet build`, `dotnet restore`, and `dotnet test` commands run inside the sandbox must append `-m:1 --disable-build-servers` to guarantee deterministic, isolated single-node execution.

9. **Evidence-Based Milestone Tagging & Release Guardrails (.NET Line)**:
   - **`0.x.y` Invariant**: The .NET line stays on `0.x.y` until the `1.0.0` gate is met: 100% route parity with Python, audit-chain verifier passing, security suite passing, and canary cutover approved.
   - **No Work-Session Counters**: Do NOT create a tag after each work session or batch of commits. Untagged builds are versioned automatically by MinVer.
   - **Milestone Plan Authorization**: Create a tag ONLY at a named milestone listed in the milestone plan (`docs/release/versioning-policy.md`), and ONLY after the user explicitly approves the exact tag name, commit SHA, and ledger evidence.
   - **Autonomous Tagging Forbidden**: Agents must NEVER run `git tag`, `git push --tags`, `git push --follow-tags`, or push to `refs/tags/*`. The agent proposes the exact commands; the user executes them.
   - **SemVer 0.x Meaning**: In `0.x`: MINOR = milestone or breaking change, PATCH = fix. Do not use `-preview.N` as a work counter. Use `-preview`/`-rc` only for builds that are actually deployed to a named environment for testing.
   - **Tag Immutability & Historical Checkpoints**: Never delete or move an existing tag. Existing tags `dotnet/v0.1.0-preview.1` through `.4` are internal checkpoints, not releases.
   - **Migration Invariant**: A language migration is not a MAJOR change. MAJOR changes only when the public API contract breaks, or at the `1.0.0` production gate.

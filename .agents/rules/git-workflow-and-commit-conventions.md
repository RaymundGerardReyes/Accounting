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
     - `dotnet`: C# ASP.NET Core (.NET 10) Modular Monolith backend (`Backend/`).
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
   - Commits must NEVER mix changes to `Frontend/` with backend directories (`Backend/`, `db/`), except for `chore(release)` or `build(deps)` synchronization commits.
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

10. **Refspec Case-Sensitivity & Shell-Specific Tag Push Syntax**:
    - **Branch Name Casing Invariant**: All branch refspecs in Git are strictly case-sensitive. Always verify branch casing (e.g., `migrating-C#` with capital `C`). Attempting to push `migrating-c#` fails immediately with `fatal: migrating-c# cannot be resolved to branch`.
    - **Pre-Push Tag Authorization by Shell**:
      - **Git Bash / MINGW64 / Linux / macOS**:
        ```bash
        git push origin migrating-C#
        ALLOW_TAG_PUSH=1 git push origin dotnet/v0.5.0-preview.parity
        # or together:
        ALLOW_TAG_PUSH=1 git push origin migrating-C# --tags
        ```
      - **Windows PowerShell**:
        ```powershell
        git push origin migrating-C#
        $env:ALLOW_TAG_PUSH = '1'; git push origin dotnet/v0.5.0-preview.parity; Remove-Item Env:ALLOW_TAG_PUSH
        # or together:
        $env:ALLOW_TAG_PUSH = '1'; git push origin migrating-C# --tags; Remove-Item Env:ALLOW_TAG_PUSH
        ```
      - Never execute PowerShell `$env:` syntax inside Bash, as Bash treats `$env` as a variable and throws `:ALLOW_TAG_PUSH: command not found`.

11. **Backend Moniker Alignment & Seed Preservation Invariant**:
    - The .NET 10 Modular Monolith backend has been permanently transitioned to `Backend/` following the retirement of the legacy Python codebase on branch `migrating-C#` (preserved on `legacy-python`).
    - Authoritative seed data (`configuration.json`, `product_data.json`) resides in `Backend/data/` and is configured for build output preservation (`CopyToOutputDirectory=PreserveNewest`).
    - All in-memory and infrastructure store loaders (`JsonConfigurationStore.cs`, `JsonProductCatalog.cs`), CI/CD workflows, build scripts, and test harnesses must exclusively target `Backend/`.

12. **Heroku .NET Buildpack Invariants (Bare Filenames & Single PropertyGroup)**:
    - The official `heroku/dotnet` buildpack strictly prohibits path separators (`/` or `\`) in `SOLUTION_FILE`:
      1. `SOLUTION_FILE` in Heroku Config Vars or `project.toml` MUST be a bare filename without directory components (e.g. `Rge.Accounting.Host.slnx`).
      2. The solution file (`Rge.Accounting.Host.slnx`) must reside directly in the root directory of the repository.
      3. If `SOLUTION_FILE` was previously set to a nested path in Heroku config vars, it must be updated to `Rge.Accounting.Host.slnx` or removed with `heroku config:unset SOLUTION_FILE` so Heroku auto-detects the root solution.
    - `Directory.Build.props` must have a **single** `<PropertyGroup>` tag: The buildpack's Rust serde XML deserializer expects a single `PropertyGroup` and aborts with `duplicate field PropertyGroup` if multiple groups exist.

13. **Heroku Production Runtime .NET SDK Absence Invariant**:
    - Heroku production dynos contain ONLY the .NET Runtime layer (`/app/.heroku/cnb/dotnet/layers/runtime/dotnet`), NOT the .NET SDK (`No .NET SDKs were found`).
    - The CLI command `dotnet run` requires an SDK and is strictly prohibited in production dynos or startup scripts (`scripts/start_production.py`).
    - The production supervisor MUST launch pre-compiled assets directly:
      1. Managed assembly: `dotnet <path_to_Host.dll> [args]`
      2. Native binary: `<path_to_Host> [args]`
    - Startup scripts must check `dotnet --list-sdks` before ever attempting `dotnet run` fallback, reserving `dotnet run` exclusively for local development environments.

14. **Production Secrets Auto-Remediation & Rotation Invariant**:
    - In containerized production deployments (e.g. Heroku dynos), missing optional/system secrets (`FIELD_ENCRYPTION_KEY_BASE64`) or legacy placeholder keys (`rge_gforms_live_sec_89f3a21`) must NOT abort process startup.
    - `scripts/start_production.py` and `SecretsConfiguration.cs` must provide symmetric defense-in-depth:
      1. Deriving a deterministic 32-byte Base64 key from `DATABASE_URL` or environment seeds when `FIELD_ENCRYPTION_KEY_BASE64` is missing.
      2. Auto-rotating compromised legacy keys with a logged warning instead of throwing unhandled exceptions in production.

15. **OAuth 2.0 Redirect URI Dual-Path Routing and Dynamic Host Resolution**:
    - **Dual-Path Callback Registration**:
      Every OAuth callback endpoint MUST bind all standard routing conventions to the same handler:
      - `/api/auth/{provider}/callback` (e.g. `/api/auth/google/callback`)
      - `/api/auth/callback/{provider}` (e.g. `/api/auth/callback/google`)
      - `/auth/{provider}/callback`
      - `/auth/callback/{provider}`
    - **Reverse-Proxy Origin Priority**:
      When constructing authorization `redirect_uri` parameters, the backend MUST inspect the client's actual incoming authority from `X-Forwarded-Host` (enforced as `https://` for all non-loopback hosts) before falling back to static environment variables (`NEXT_PUBLIC_APP_URL`). This guarantees that multi-domain, custom domain, and staging environments dynamically match the origin in the user's browser.
    - **Explicit Override Configuration**:
      The runtime MUST support `GOOGLE_REDIRECT_URI` (or `Authentication:Google:RedirectUri`) to allow zero-code overrides matching pre-existing Google Cloud Console credentials. When unspecified, it defaults to `/api/auth/{provider}/callback`.

16. **Automated Dual-Line Versioning & Migration Ledger Invariant**:
    - **Mandatory Build Environment Injection**:
      - Before building the frontend or releasing, the agent or supervisor MUST execute `tools/versioning/compute-build-env.ps1 -Format DotEnv -WriteToFrontend` (or its platform supervisor equivalent) to populate `Frontend/.env.production.local`.
      - `package.json` and `Frontend/package.json` version fields MUST strictly remain `"0.0.0-managed"`. Autonomous tools and developers must never overwrite them with raw version strings.
    - **Namespaced SemVer 2.0.0 Line Awareness**:
      - Version inspections, bumps, and tagging MUST respect `.release-line`:
        - Line A (`dotnet`): tag pattern `dotnet/vX.Y.Z[-preview.N]`
        - Line B (`python`): tag pattern `python/vX.Y.Z[-beta.N]`
      - `scripts/git_workflow.py` and related tooling must inspect `.release-line` and only calculate increments relative to that line's authoritative tag history.
    - **Migration Ledger State Promotion Invariant**:
      - Whenever an endpoint or handler is added or verified with passing automated tests (e.g. `OAuthFlowTests.cs`), the corresponding row in `docs/migration/route-ledger.csv` MUST be promoted from `implemented` to `tested` or `parity-verified` with the specific test path recorded in `parity_test`.
      - Milestone tag proposals require 100% of the ledger rows for that milestone to be in `parity-verified` or `accepted-difference` status as audited by `tools/migration/ledger-check.ps1`.
    - **Conventional Commit Scope Harmonization**:
      - All commit validation tools (`.githooks/commit-msg`, `commitlint.config.js`, `scripts/git_workflow.py`, `check-commit-scope.ps1`) must accept both functional monorepo scopes (`repo`, `backend`, `dotnet`, `frontend`, `test`, `docs`, `agents`, `versioning`, `migration`, `content-pack`) and domain-module scopes (`web`, `identity`, `finance`, `payments`, `receipts`, `audit`, `costalloc`, `documents`, `integrations`, `db`, `deploy`, `release`, `deps`).

17. **Dynamic User Credentials, RBAC Persistence, and OAuth Identity Resolution Invariants**:
    - **Dynamic Credential & RBAC Role Mutations via PUT**:
      - All user credential and RBAC role modifications MUST be executed through transactional endpoints (`PUT /api/v1/users/credentials`), persisting changes to both the relational persistence layer (`users_finance`, `user_credentials`, `role_assignments`) and in-memory caches.
      - Any mutation MUST trigger immediate cache invalidation (`_actorCache.Invalidate(userId)`) to guarantee immediate authorization propagation across all subsequent requests without requiring user re-authentication.
    - **Dynamic UI Persona & Operational Actor Population**:
      - Frontend components (`login/page.tsx`, `finance-operations-hub.tsx`, `configuration.tsx`) must NEVER hardcode static persona lists or static actor selection `<option>` elements.
      - All personas and actors must be queried dynamically from backend registries (`GET /api/v1/finance/users`, live auth state) with resilient fallback to authenticated user state.
    - **Live OAuth Identity Verification & Unregistered User 401 Rejection**:
      - OAuth token exchange and callback handlers must NEVER return static mock identities or synthesize arbitrary personas.
      - All OAuth authenticated identities must be resolved dynamically against stored database user credentials. If an authenticating OAuth profile (e.g. Google account email/sub) is not pre-registered in user credentials, the backend MUST reject authentication with HTTP 401 Unauthorized (`Identity.OAuth.UnregisteredUser`), preventing unauthorized access.
    - **Commit Scope Separation & Quality Gates**:
      - Identity and RBAC changes touching backend APIs and frontend components must strictly maintain scope isolation across separate commits (`feat(backend)` vs `feat(frontend)`), preserving ADR 0011 `"0.0.0-managed"` and passing all 76 parity runner checks.

18. **Heroku Cloud Native Buildpack (CNB) Upstream Asset Outage Invariant**:
    - The `heroku/dotnet` buildpack relies on downloading pre-compiled `.cnb` assets from `https://github.com/heroku/buildpacks-dotnet/releases/download/vX.Y.Z/...` during slug compilation.
    - When GitHub's release download redirection service experiences upstream degradation or returns `504 Gateway Time-out`:
      1. This is an external infrastructure failure between Heroku build dynos and GitHub Releases, NOT an application compilation error.
      2. **Primary Remediation**: Check [githubstatus.com](https://www.githubstatus.com) and retry deployment once the release asset CDN recovers.
      3. **Fallback Remediation**: If an urgent deployment is required during an extended CDN outage, switch from the CNB shim to a git-cloned buildpack via the Heroku CLI:
         ```bash
         heroku buildpacks:remove heroku/dotnet -a <app-name>
         heroku buildpacks:add --index 2 https://github.com/jincod/dotnetcore-buildpack.git -a <app-name>
         ```






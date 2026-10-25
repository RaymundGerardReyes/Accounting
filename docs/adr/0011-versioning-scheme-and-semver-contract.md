# ADR 0011: Namespaced Dual-Line Semantic Versioning & Release Isolation

## Status
Accepted

## Context
The platform is undergoing a strangler-fig migration from Python FastAPI (`Backend/`) to a C# .NET 10 Modular Monolith (`Backend.DotNet/`). Both backends serve a single shared Next.js frontend (`Frontend/`).
Using a single monolithic Git tag (e.g. `v1.0.0`) presents major flaws:
1. **Version Collisions**: A tag `v1.0.0` cannot distinguish between an update to the Python production runtime vs. the .NET migration candidate.
2. **Frontend Drift**: If `package.json` maintained the version string, identical frontend commits across both branches would diverge due to package version differences.
3. **Audit Immutability**: The system enforces BIR-compliant receipts and SHA-256 chained audit logs where releases must be deterministically traceable to exact source commits.

## Decision
1. **Tag Namespacing**: Adopt prefixed SemVer 2.0.0 tags:
   - Line A (.NET 10): `dotnet/vMAJOR.MINOR.PATCH`
   - Line B (Python): `python/vMAJOR.MINOR.PATCH`
2. **Build-Time Injected Frontend Version**: Set `package.json` version to `0.0.0-managed` across all branches. The true version is computed at build time from `git describe --tags --match "<line>/v*"` and injected via `NEXT_PUBLIC_APP_VERSION`.
3. **Runtime `/version` Endpoint**: Both backends must expose an unauthenticated, rate-limited `/version` endpoint returning a standardized JSON schema:
   ```json
   {
     "line": "dotnet" | "python",
     "version": "1.0.0",
     "gitSha": "commit-sha",
     "buildTime": "iso-8601-utc",
     "apiContract": "v1",
     "schemaVersion": "V0004"
   }
   ```
4. **Separation of Commit Scopes**: Commits must never mix `Frontend/` and backend paths, ensuring clean cherry-picking between branches.

## Consequences
- **Positive**: Zero tag collisions; zero frontend code drift due to version bumps; complete runtime traceability.
- **Negative**: Release automation requires line-aware tag filtering in CI/CD.


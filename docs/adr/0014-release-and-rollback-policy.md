# ADR 0014: Release, Rollback, and Revert Governance Policy

## Status
Accepted

## Context
In a regulated financial platform with a forward-only database migration strategy and an append-only cryptographic audit hash chain, traditional rollback mechanisms like dropping tables, running `git push --force`, or deleting git tags can corrupt audit integrity and cause catastrophic data loss.

## Decision
1. **Immutable Release Tags**:
   - Git tags are strictly immutable. Once created and pushed, tags are never moved or deleted.
   - Faulty releases are neutralized by publishing a forward patch release (`vX.Y.Z+1`) or redeploying the previous certified container image digest.
2. **Revert Strategy**:
   - History rewriting (`git rebase -i`, `git reset --hard` on shared branches) is strictly prohibited.
   - Code regressions are resolved via `git revert -m 1 <commit-sha>` with commit message format:
     `revert: "<original commit subject>" (refs <incident-id>)`.
3. **Database Migration Governance (Expand/Contract)**:
   - All migrations applied during the strangler-fig migration period must follow the **Expand/Contract** pattern.
   - Destructive migrations (dropping columns, changing data types, or deleting tables) are forbidden until 100% .NET parity cutover is complete.
   - For rollback scenarios, application code rollback is preferred over database rollback.
4. **Audit Immutability**:
   - The audit log (`audit_events`) and advance ledger (`advance_ledger_entries`) are strictly append-only.
   - Erroneous accounting entries must be rectified solely through compensating adjustments or reversal entries (`LedgerEntryType.Reversal`).

## Consequences
- **Positive**: Strict compliance with Philippine regulatory audit requirements; deterministic and automated rollback runbooks.
- **Negative**: Requires forward-only discipline and dual-write compatibility during migration.


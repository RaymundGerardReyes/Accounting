# Runbook: Roll Back Application (.NET Line A)

## 1. Trigger & Scope
Use this runbook when a newly deployed release of the **.NET Line** (`Backend/`, branch `migrating-C#`, tag `dotnet/vX.Y.Z`) exhibits critical production regressions (e.g. crash loops, HTTP 500 spikes, outbox delivery halts, or memory leaks).

> [!CRITICAL]
> **Zero Git History Rewrites**: Never run `git push --force` or delete tags. Application rollback is achieved by flipping container / deployment targets to the previously known good release artifact.

---

## 2. Prerequisites & Information Gathering
Before rolling back, identify the target known-good version and the faulty running version:

```bash
# 1. Identify currently running version
curl -s https://api.rge.internal/version
# Or via local host:
curl -s http://localhost:5000/version

# 2. List the last 5 .NET line release tags
git tag -l "dotnet/v*" --sort=-creatordate | head -n 5

# 3. Target good version example: dotnet/v0.1.0-preview.1
TARGET_TAG="dotnet/v0.1.0-preview.1"
TARGET_VERSION="0.1.0-preview.1"
```

---

## 3. Step-by-Step Rollback Procedure

### Step 3.1: Redeploy Previous Container Image
Update the deployment orchestrator (Kubernetes / Docker Compose / Container Apps) to pull the previous image tag:

#### Kubernetes Example:
```bash
# Set deployment image back to the previous stable tag
kubectl set image deployment/rge-accounting-dotnet \
  rge-accounting-dotnet=ghcr.io/rge/accounting-dotnet:${TARGET_VERSION} \
  --record

# Watch rollout status
kubectl rollout status deployment/rge-accounting-dotnet --timeout=120s
```

#### Docker Compose Example:
```bash
# In production environment file (.env.production):
# DOTNET_IMAGE_TAG=0.1.0-preview.1
sed -i 's/DOTNET_IMAGE_TAG=.*/DOTNET_IMAGE_TAG='"${TARGET_VERSION}"'/' .env.production
docker compose up -d rge-accounting-dotnet
```

### Step 3.2: Verify Health & Version Endpoints
Execute immediate post-deployment probes:

```bash
# Verify health endpoint returns 200 OK
curl -f -s http://localhost:5000/health | jq .

# Verify version matches target
curl -f -s http://localhost:5000/version | jq .
```
Expected output:
```json
{
  "line": "dotnet",
  "version": "0.1.0-preview.1",
  "gitSha": "0d2a4d9",
  "buildTime": "2026-10-25T...",
  "apiContract": "v1",
  "schemaVersion": "V0004"
}
```

### Step 3.3: Inspect Transactional Outbox Worker
Confirm that the outbox dispatcher resumed processing events:
```bash
# Check worker log stream for outbox dispatch success
docker logs --tail 100 rge-accounting-dotnet | grep -i "outbox"

# Query pending outbox events in database
psql $DATABASE_URL -c "SELECT status, count(*) FROM fulfillment_outbox GROUP BY status;"
```

---

## 4. Post-Rollback Git Record
Once production is stable on the rolled-back container image:
1. Do **not** delete the bad tag.
2. Follow [bad-release-withdrawal.md](file:///e:/Accounting/docs/runbooks/bad-release-withdrawal.md) to record the incident and withdraw the tag.
3. Open a fix PR following [hotfix-procedure.md](file:///e:/Accounting/docs/runbooks/hotfix-procedure.md).


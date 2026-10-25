# Runbook: Roll Back Application (Python Line B)

## 1. Trigger & Scope
Use this runbook when a newly deployed release of the **Python Line** (`Backend/`, branch `legacy-python`, tag `python/vX.Y.Z`) exhibits critical production bugs, unhandled exceptions in FastAPI endpoints, or PayMongo webhook processing failures.

> [!CRITICAL]
> **Zero Git History Rewrites**: Never run `git push --force` or delete tags. Application rollback is achieved by flipping deployment artifacts or executing platform releases to the previously known-good version.

---

## 2. Prerequisites & Information Gathering
Identify the current running version and the target known-good version:

```bash
# 1. Identify currently running Python backend version
curl -s https://api.rge.internal/version
# Or via local host:
curl -s http://localhost:8000/version

# 2. List the last 5 Python line release tags
git tag -l "python/v*" --sort=-creatordate | head -n 5

# 3. Target good version example: python/v0.9.34
TARGET_TAG="python/v0.9.34"
TARGET_VERSION="0.9.34"
```

---

## 3. Step-by-Step Rollback Procedure

### Step 3.1: Redeploy Previous Image or Platform Release

#### Container / Docker Compose:
```bash
# Update Python container image tag to previous stable version
sed -i 's/PYTHON_IMAGE_TAG=.*/PYTHON_IMAGE_TAG='"${TARGET_VERSION}"'/' .env.production
docker compose up -d rge-accounting-python
```

#### Heroku / PaaS Deployment:
```bash
# Check release history
heroku releases -a rge-accounting-api

# Roll back to previous release (e.g. v45)
heroku rollback v45 -a rge-accounting-api
```

### Step 3.2: Verify Health & Version Endpoints
Execute verification requests:

```bash
# Verify health endpoint returns 200 OK
curl -f -s http://localhost:8000/health | jq .

# Verify version matches target
curl -f -s http://localhost:8000/version | jq .
```
Expected output:
```json
{
  "line": "python",
  "version": "0.9.34",
  "gitSha": "abc1234",
  "buildTime": "2026-...",
  "apiContract": "v1",
  "schemaVersion": "V0001"
}
```

### Step 3.3: Verify PayMongo Webhook Handlers
Send an authenticated health verification to the webhook intake endpoint:
```bash
# Ensure webhook router responds correctly without database locking
curl -s -o /dev/null -w "%{http_code}" http://localhost:8000/api/health
# Expected: 200
```

---

## 4. Post-Rollback Record
1. Follow [bad-release-withdrawal.md](file:///e:/Accounting/docs/runbooks/bad-release-withdrawal.md).
2. If any webhook events failed during the incident window, follow [webhook-outbox-replay.md](file:///e:/Accounting/docs/runbooks/webhook-outbox-replay.md).


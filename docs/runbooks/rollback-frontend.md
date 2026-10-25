# Runbook: Roll Back Frontend (Next.js 15)

## 1. Trigger & Scope
Use this runbook when a newly deployed release of the **Next.js 15 Frontend** introduces UI regressions, broken payment checkout flows, client-side hydration crashes, or routing failures.

Because both lines share an identical `Frontend/` codebase (`package.json: 0.0.0-managed`), rollback procedures apply identically regardless of whether the frontend serves the .NET line or Python line.

---

## 2. Fast Rollback via Platform Orchestration

### Option A: Vercel Instant Rollback (Recommended)
If hosting Next.js on Vercel:

1. Navigate to the **Vercel Project Dashboard** $\rightarrow$ **Deployments**.
2. Locate the previous production deployment (tagged with the prior stable commit).
3. Click the three dots (`...`) $\rightarrow$ select **Instant Rollback**.
4. Confirm promotion. Traffic is immediately shifted in $< 5$ seconds without a rebuild.

Or via Vercel CLI:
```bash
# List recent deployments
vercel ls

# Promote previous deployment directly to production
vercel alias set <PREVIOUS_DEPLOYMENT_URL> rge-accounting.com
```

### Option B: Docker / Containerized Next.js Rollback
If hosting Next.js as a containerized Node service:

```bash
# Flip to previous frontend container image
docker pull ghcr.io/rge/accounting-frontend:${TARGET_VERSION}
docker stop rge-frontend-active
docker run -d --name rge-frontend-new -p 3000:3000 ghcr.io/rge/accounting-frontend:${TARGET_VERSION}
```

---

## 3. Client-Side Version Verification

Verify that the active production frontend serves the target version metadata:

```bash
# Query the Next.js frontend HTML for injected public environment variables
curl -s https://app.rge-accounting.com | grep -o 'NEXT_PUBLIC_APP_VERSION[^"]*'
```

In the browser console:
```javascript
// Open DevTools Console on https://app.rge-accounting.com:
console.log(window.__NEXT_DATA__.props.pageProps?.versionInfo || "Check /api/version");
```

---

## 4. Post-Rollback Drift Verification
If the faulty frontend commit was already propagated to the other line via `sync-frontend.ps1`:
1. Revert the commit on the origin branch using [revert-a-bad-commit.md](file:///e:/Accounting/docs/runbooks/revert-a-bad-commit.md).
2. Re-synchronize using `./tools/versioning/sync-frontend.ps1 -Apply`.
3. Verify zero drift using `./tools/versioning/check-frontend-drift.ps1`.


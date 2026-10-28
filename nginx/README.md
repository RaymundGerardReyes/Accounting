# NGINX Reverse Proxy & Cloudflare Edge Integration

This directory contains hardened NGINX reverse proxy configurations designed for the Acme Cost Accounting & Live Payment Orchestration stack (Next.js 15 + FastAPI PostgreSQL).

## Architecture Overview

```
                      [ Client / Browser ]
                                |
                                v
               [ Cloudflare Edge Reverse Proxy ]
               - Full (Strict) SSL Termination
               - CF-Connecting-IP injection
               - CF-Ray tracing header
                                |
                                v
               [ Hardened NGINX Traffic Controller ]
        -------------------------------------------------
        | Real-IP Restoration (sets $remote_addr)      |
        | Structured JSON Security Logging              |
        | Rate Limiting Zones (api_rate & frontend_rate)|
        | Connection Concurrency Limiting               |
        -------------------------------------------------
                  /                           \
                 /                             \
        location /api/                      location /
               v                                 v
    [ .NET 10 Backend ]                 [ Next.js Frontend ]
    Port 5080                           Port 3000
    - Live PostgreSQL                   - Server Components
    - PayMongo Payments                 - React UI
    - Auth & Cost Allocation            - API Proxy Handler
```

## Configuration Variants

### 1. Direct SSL with Cloudflare Origin CA (`nginx.conf`)
- **Use Case:** Bare metal, Dedicated VPS, VM instances (e.g. Ubuntu / Debian / RHEL).
- **SSL Termination:** NGINX listens on port 443 with Cloudflare Origin CA certificates installed at `/etc/ssl/certs/cloudflare_origin.pem` and `/etc/ssl/private/cloudflare_origin.key`.
- **Default Server Block:** Listens on port 80 and 443 with `server_name _`, returning `444` to immediately sever connections from bots scanning raw IP addresses.

### 2. PaaS / Container Edge Termination (`nginx.paas.conf`)
- **Use Case:** Heroku, Render, AWS ECS, Fly.io, or Docker containers behind an edge load balancer.
- **SSL Termination:** Terminated upstream by Cloudflare or the platform edge load balancer. NGINX listens on port 80 (or dynamic `$PORT`).
- **Scheme Restoration:** Uses NGINX `map` to reconstruct `$forwarded_proto` from `$http_x_forwarded_proto`.
- **Health Check:** Includes `/healthz` for container liveness probes.

## Key Security Features

1. **Cloudflare Real-IP Restoration:** Restores the client's actual IP into `$remote_addr` from `CF-Connecting-IP` across all official Cloudflare IPv4 and IPv6 CIDRs. This prevents `limit_req` and `limit_conn` from blocking Cloudflare's edge nodes.
2. **Rate Limiting Zones:**
   - `api_rate`: 10 req/sec with burst 20 for `/api/` endpoints (Auth, Payments, Allocation).
   - `frontend_rate`: 50 req/sec with burst 100 for static assets and React bundles.
   - `per_ip_conn`: Max 30 concurrent connections per client IP.
3. **Structured JSON Logging:** Outputs security audit logs with timestamps, request IDs, real client IPs, CF-Ray headers, upstream statuses, and TLS ciphers to `/var/log/nginx/security_access.log`.
4. **Hardened HTTP Headers:** Enforces `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`, and `Strict-Transport-Security`.

## Environment Variable Coordination

- **`NEXT_PUBLIC_APP_DOMAIN`**: Canonical hostname (e.g. `acme-finance.yourdomain.com`).
  - Read by Next.js `Frontend/app/api/[...path]/route.ts` to set `x-forwarded-host`, `x-forwarded-proto`, and resolve redirects.
  - Read by FastAPI `Backend/app.py` to allow dynamic CORS requests from the production domain.

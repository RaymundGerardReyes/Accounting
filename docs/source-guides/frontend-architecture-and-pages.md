# Frontend Architecture & Next.js Routes Guide

This guide details the Next.js App Router layout, design tokens, route proxy, and specialized checkout and payment verification pages.

---

## 1. `Frontend/app/layout.tsx` & `Frontend/app/globals.css`
- **Paths**: [`Frontend/app/layout.tsx`](file:///e:/Accounting/Frontend/app/layout.tsx), [`Frontend/app/globals.css`](file:///e:/Accounting/Frontend/app/globals.css)
- **Role**: Root application scaffolding, web font optimization, and design tokens.

### Key Features
- **Font Optimization**: Uses `next/font/google` for `Geist` and `Geist Mono`, eliminating external Google Fonts runtime requests.
- **Theme Provider**: Wraps the React tree with `<ThemeProvider attribute="class" defaultTheme="light" enableSystem>` from `next-themes`.
- **CSS Design Tokens**:
  - Defines OKLCH color variables for `--primary`, `--secondary`, `--accent`, `--background`, `--foreground`, `--muted`, `--border`.
  - Configures high-contrast dark mode classes under `.dark`.

---

## 2. `Frontend/app/page.tsx`
- **Path**: [`Frontend/app/page.tsx`](file:///e:/Accounting/Frontend/app/page.tsx)
- **Role**: Main application container and tab navigation controller.

### Performance & Loading Strategy
- Implements `next/dynamic` code splitting for heavy sub-views:
  - `PaymentsHub`: Payment operations console and order manager.
  - `DepartmentDetail`: Drill-down analysis for specific cost centers.
  - `ProductLineAnalysis`: Scatter plot and gross margin table.
  - `VarianceAnalysis`: Budget variance reports and CSV export.
  - `Configuration`: Cost center, user roster, and audit log manager.
- Ensures cold-start execution loads only the lightweight dashboard and sidebar modules initially.

---

## 3. `Frontend/app/api/[...path]/route.ts`
- **Path**: [`Frontend/app/api/[...path]/route.ts`](file:///e:/Accounting/Frontend/app/api/%5B...path%5D/route.ts)
- **Role**: Next.js App Router catch-all reverse proxy handler.

### Proxy Mechanism
- Intercepts all requests matching `/api/*`.
- Forwards requests to the backend server: `http://localhost:5000/api/{...path}`.
- Preserves HTTP methods: `GET`, `POST`, `PUT`, `DELETE`, `PATCH`.
- **Security & Header Sanitization**:
  - Strips the `host` request header (`headers.delete("host")`) to prevent FastAPI Host header mismatch exceptions.
  - Transmits binary payloads via `request.arrayBuffer()`.
  - Relays response headers and status codes transparently.
  - Returns HTTP 502 Bad Gateway with diagnostic error details if the FastAPI backend is offline.

---

## 4. `Frontend/app/checkout/page.tsx`
- **Path**: [`Frontend/app/checkout/page.tsx`](file:///e:/Accounting/Frontend/app/checkout/page.tsx)
- **Role**: Pre-payment review and channel selection page.

### Features & Workflow
- Resolves `order_id` or `orderId` from search parameters.
- Fetches authoritative order details from `GET /api/v1/orders/{order_id}`.
- Renders order summary, line items, and unit price in Philippine Peso (`₱`).
- Displays Philippine payment rails:
  - QR Ph (BSP Interoperable National Standard)
  - E-Wallets (GCash, Maya)
  - Direct Online Banking (BPI, UnionBank)
  - Credit & Debit Cards (Visa, Mastercard)
- Embeds the `<CheckoutButton />` component to initiate payment attempts.

---

## 5. `Frontend/app/payment/return/page.tsx`
- **Path**: [`Frontend/app/payment/return/page.tsx`](file:///e:/Accounting/Frontend/app/payment/return/page.tsx)
- **Role**: Gateway return redirect landing page.

### Security Invariant
- **Strict Read-Only Verification**:
  - The client NEVER updates order or payment state directly from this page.
  - Browser query parameters are treated as untrusted hints.
  - Resolves the payment attempt ID from `order_id` or `payment_id`.
  - Immediately redirects to `/payment/result/{paymentId}` to initiate server-side status verification.

---

## 6. `Frontend/app/payment/result/[paymentId]/page.tsx`
- **Path**: [`Frontend/app/payment/result/[paymentId]/page.tsx`](file:///e:/Accounting/Frontend/app/payment/result/%5BpaymentId%5D/page.tsx)
- **Role**: Settlement verification and receipt confirmation page.

### Polling & Settlement Lifecycle
- **Active Polling Loop**:
  - Repeatedly queries `GET /api/v1/payments/{paymentId}` every 2 seconds.
  - Checks for terminal states: `succeeded`, `failed`, `cancelled`, `expired`.
  - Terminates after reaching a terminal state or after 30 attempts (60-second safety cutoff).
- **Manual Reconciliation**:
  - If a payment remains in a non-terminal status (`requires_action` or `processing`) due to webhook latency, the user can click **"Reconcile Status Now"**.
  - Calls `POST /api/v1/admin/payments/{paymentId}/reconcile` to force an immediate backend check against PayMongo.
- **Receipt Presentation**:
  - When `status === "succeeded"`, renders a formatted receipt with transaction ID, payment method, settled amount, and print button.

---

## 7. `Frontend/app/payment/sandbox/[attemptId]/page.tsx`
- **Path**: [`Frontend/app/payment/sandbox/[attemptId]/page.tsx`](file:///e:/Accounting/Frontend/app/payment/sandbox/%5BattemptId%5D/page.tsx)
- **Role**: Interactive local sandbox simulator runner.

### Interactive Testing Capabilities
- Emulates the PayMongo Hosted Checkout UI locally without live API keys.
- Supports simulating all payment channels: QR Ph, GCash, Maya, Card.
- **Webhook Arrival Order Testing**:
  - `before_redirect`: Triggers the signed webhook before redirecting the browser back to the app (standard gateway behavior).
  - `after_redirect`: Redirects immediately to `/payment/return` before the webhook arrives, testing the frontend polling and reconciliation fallback loops.
- Dispatches HMAC-SHA256 signed simulations via `POST /api/v1/sandbox/simulate-webhook`.

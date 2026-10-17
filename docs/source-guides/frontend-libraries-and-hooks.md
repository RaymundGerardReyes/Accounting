# Frontend Libraries & Custom Hooks Guide

This guide details the mathematical cost allocation engine, typed payment API client, design system tokens, class utilities, and custom React hooks.

---

## 1. `Frontend/lib/cost-allocation.ts`
- **Path**: [`Frontend/lib/cost-allocation.ts`](file:///e:/Accounting/Frontend/lib/cost-allocation.ts)
- **Role**: Mathematical cost allocation models and variance computation engine.

### Allocation Models

#### 1. Direct Cost Allocation (`directCostAllocation`)
- Proportional distribution based on each department's direct expenses:
  $$\text{Allocated Cost}_i = \left(\frac{\text{Direct Cost}_i}{\sum \text{Direct Cost}}\right) \times \text{Total Cost}$$

#### 2. Volume-Based Allocation (`volumeBasedAllocation`)
- Allocates overhead according to unit production counts:
  $$\text{Allocated Cost}_i = \left(\frac{\text{Volume}_i}{\sum \text{Volume}}\right) \times \text{Total Cost}$$

#### 3. Machine-Hours Allocation (`machineHoursAllocation`)
- Allocates capital equipment depreciation and maintenance based on logged machine hours:
  $$\text{Allocated Cost}_i = \left(\frac{\text{Machine Hours}_i}{\sum \text{Machine Hours}}\right) \times \text{Total Cost}$$

#### 4. Activity-Based Costing (`activityBasedAllocation`)
- Computes activity cost driver rates:
  $$\text{Driver Rate} = \frac{\text{Activity Pool Cost}}{\text{Driver Value}}$$
  $$\text{Department Cost} = \sum (\text{Driver Rate} \times \text{Department Weight})$$

### Cost Variance Computation (`calculateVariance`)
- **Domain Accounting Invariant**:
  $$\text{Cost Variance} = \text{Allocated Cost (Budget)} - \text{Actual Cost}$$
- **Status Classification**:
  - $\text{Variance} > 0$: **`"favorable"`** (spending was under budget; cost savings achieved).
  - $\text{Variance} < 0$: **`"unfavorable"`** (spending exceeded budget; cost overrun).
  - $\text{Variance} = 0$: **`"neutral"`** (spending matched budget exactly).
- **Proportional Percentage**:
  $$\text{Variance \%} = \left(\frac{\text{Variance}}{\text{Allocated Cost}}\right) \times 100$$

### Product Cost Calculations (`calculateProductCosts`)
- Computes `costPerUnit = totalCost / unitVolume`.
- Computes `profitPerUnit = sellingPrice - costPerUnit`.
- Computes `grossMargin = (profitPerUnit / sellingPrice) * 100`.

---

## 2. `Frontend/lib/payment-api.ts`
- **Path**: [`Frontend/lib/payment-api.ts`](file:///e:/Accounting/Frontend/lib/payment-api.ts)
- **Role**: TypeScript interfaces and asynchronous fetch client for the payment orchestration layer.

### Key Types
- `Order`: Represents customer orders or invoices (`amount_minor`, `amount`, `status`, `latest_attempt`).
- `PaymentAttempt`: Tracks individual gateway attempts (`idempotency_key`, `checkout_url`, `status`).
- `WebhookEventRecord`: Gateway audit event record.
- `FulfillmentOutboxRecord`: Post-payment task record.

### Security & Formatting Utilities
- `isApprovedGatewayOrigin(urlStr: string): boolean`:
  - Enforces origin validation before browser redirection.
  - Whitelist: `checkout.paymongo.com`, `api.paymongo.com`, `localhost:3000`, `127.0.0.1:3000`, `localhost:5000`, `127.0.0.1:5000`.
- `formatPhpFromMinor(amountMinor: number): string`:
  - Formats centavos to Philippine Peso currency strings: `₱1,234.56` using `Intl.NumberFormat("en-PH")`.

### API Methods
- `fetchOrders(status?)`: Retrieves order catalog with latest attempts.
- `fetchOrder(orderId)`: Retrieves single order with complete attempt history.
- `createOrder(data)`: Posts new order using authoritative pricing.
- `createPaymentAttempt(orderId, params)`: Dispatches attempt creation with `Idempotency-Key` header.
- `pollPaymentStatus(paymentId)`: Polling endpoint for attempt lifecycle updates.
- `reconcilePayment(paymentId)`: Forces single-attempt gateway status check.
- `reconcileAllPayments()`: Triggers bulk reconciliation for unresolved attempts.
- `fetchWebhookEvents()`: Fetches gateway event log.
- `fetchFulfillmentOutbox()`: Fetches outbox task log.
- `simulateWebhook(paymentAttemptId, eventType, duplicate)`: Dispatches signed simulation request to the backend.

---

## 3. `Frontend/lib/design-system.ts` & `Frontend/lib/utils.ts`
- **Paths**: [`Frontend/lib/design-system.ts`](file:///e:/Accounting/Frontend/lib/design-system.ts), [`Frontend/lib/utils.ts`](file:///e:/Accounting/Frontend/lib/utils.ts)
- **Role**: Design token constants and class merger utility.

### Features
- `designSystem`:
  - Color definitions using high-gamut OKLCH: Primary Black `oklch(0.15 0 0)`, Secondary Red `oklch(0.55 0.2 25)`, Background Off-white `oklch(0.98 0 0)`.
  - Typography scale: `xs`, `sm`, `base`, `lg`, `xl`, `2xl`, `3xl`, `4xl`.
  - Spacing and border-radius tokens.
- `cn(...inputs)`: Combines `clsx` for conditional CSS class joining with `twMerge` to eliminate conflicting Tailwind utility classes.

---

## 4. `Frontend/hooks/use-mobile.ts` & `Frontend/hooks/use-toast.ts`
- **Paths**: [`Frontend/hooks/use-mobile.ts`](file:///e:/Accounting/Frontend/hooks/use-mobile.ts), [`Frontend/hooks/use-toast.ts`](file:///e:/Accounting/Frontend/hooks/use-toast.ts)
- **Role**: Viewport responsiveness detection and accessible toast notifications.

### Features
- `useIsMobile()`:
  - Listens to `window.matchMedia("(max-width: 767px)")`.
  - Updates responsive sidebar and navigation layouts dynamically.
- `useToast()` / `toast()`:
  - React reducer-based toast manager supporting `title`, `description`, `action`, `dismiss`, and auto-timeout removal.

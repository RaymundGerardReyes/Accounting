# Frontend Components & UI Hub Guide

This guide details the React components, analytics views, administrative controls, and reusable UI primitives powering the user interface.

---

## 1. `Frontend/components/dashboard.tsx`
- **Path**: [`Frontend/components/dashboard.tsx`](file:///e:/Accounting/Frontend/components/dashboard.tsx)
- **Role**: Manufacturing cost overview dashboard with Recharts visualizations.

### Features
- Fetches `/api/dashboard` data.
- **Visualizations**:
  - `AreaChart`: 12-month manufacturing cost trend (Total, Labor, Materials, Overhead).
  - `PieChart`: Proportional department expense distribution with strict OKLCH theme colors.
  - `BarChart`: Department allocated vs. actual spending comparisons.
- **Metric Summary Cards**:
  - Total Allocated Budget, Total Actual Spend, Net Variance, and Department Count.
  - Adheres to $\text{Net Variance} = \text{Total Allocated} - \text{Total Actual}$.
  - Clickable department cards dispatch `onSelectDepartment(slug)` callbacks for deep drill-down navigation.

---

## 2. `Frontend/components/department-detail.tsx`
- **Path**: [`Frontend/components/department-detail.tsx`](file:///e:/Accounting/Frontend/components/department-detail.tsx)
- **Role**: Granular drill-down analysis for specific production cost centers.

### Features
- Fetches `/api/department/{slug}`.
- Displays category expense breakdowns across Labor, Materials, Overhead, and Utilities.
- Compares line-item variances, identifying favorable ($\ge 0$) vs. unfavorable ($< 0$) lines.
- Historical spending curve visualization.
- Accessible back-navigation button to return to the executive dashboard.

---

## 3. `Frontend/components/product-line-analysis.tsx`
- **Path**: [`Frontend/components/product-line-analysis.tsx`](file:///e:/Accounting/Frontend/components/product-line-analysis.tsx)
- **Role**: Profitability and unit cost evaluation for manufacturing product lines.

### Features
- Fetches `/api/products`.
- Evaluates gross margin percentage:
  $$\text{Gross Margin} = \left(\frac{\text{Selling Price} - \text{Unit Cost}}{\text{Selling Price}}\right) \times 100$$
- Visualizes volume vs. unit cost distribution using Recharts scatter plots.
- Badges product life-cycle stages: `growing`, `stable`, `declining`.

---

## 4. `Frontend/components/variance-analysis.tsx`
- **Path**: [`Frontend/components/variance-analysis.tsx`](file:///e:/Accounting/Frontend/components/variance-analysis.tsx)
- **Role**: Budget vs. Actual cost variance monitor and reporting engine.

### Core Features & Domain Invariants
- **Accounting Sign Invariant**:
  - Enforces $\text{Cost Variance} = \text{Budget} - \text{Actual}$.
  - Favorable variances ($\ge 0$) render in neutral/foreground text.
  - Unfavorable overruns ($< 0$) render in high-visibility accent (`text-accent` / red).
- **Excel Mojibake Prevention (UTF-8 BOM)**:
  - Generates RFC 4180 compliant CSV reports.
  - Prepends the UTF-8 Byte Order Mark (`\uFEFF`) to the Blob payload:
    ```typescript
    const blob = new Blob(["\uFEFF", csvContent], { type: "text/csv;charset=utf-8;" })
    ```
  - Ensures Microsoft Excel on Windows parses Philippine Peso symbols (`₱`) and accented characters without ANSI CP-1252 corruption (`â,±`).
  - Formats numeric financial columns with raw numeric values for direct spreadsheet calculations.

---

## 5. `Frontend/components/configuration.tsx`
- **Path**: [`Frontend/components/configuration.tsx`](file:///e:/Accounting/Frontend/components/configuration.tsx)
- **Role**: Administrative control panel for enterprise settings, cost centers, user rosters, and audit records.

### Features
- **General Configuration**: Fiscal year, company title, reporting currency, decimal precision.
- **Department Registry**: Interactive modal to create, update, or remove cost centers and assigned budgets.
- **User Roster**: Table of active operators, analysts, viewers, and administrators with role assignment.
- **Gateway Audit Logs**: Real-time display of fulfillment outbox events and webhook deliveries.

---

## 6. `Frontend/components/payments-hub.tsx`
- **Path**: [`Frontend/components/payments-hub.tsx`](file:///e:/Accounting/Frontend/components/payments-hub.tsx)
- **Role**: Comprehensive payment orchestration console.

### Key Workflows
1. **Orders & Invoices Management**:
   - Lists all orders with status badges (`pending`, `paid`, `cancelled`).
   - Search bar filtering by title, reference code, or order number.
   - Status filtering tabs (`all`, `pending`, `paid`, `cancelled`).
2. **Order Creation Modal**:
   - Selects between `product_order` and `department_invoice`.
   - Real-time price previews using authoritative server rates.
3. **Webhook Inspector**:
   - Live table of received gateway events (`checkout_session.payment.paid`, `payment.failed`, etc.).
   - Expandable modal to inspect raw cryptographic payloads.
4. **Outbox Queue Monitor**:
   - Inspects asynchronous fulfillment tasks (`order.paid`).
5. **Bulk Reconciliation**:
   - Triggers `POST /api/v1/admin/payments/reconcile-all` to sync any stuck or pending sessions.

---

## 7. `Frontend/components/checkout-button.tsx`
- **Path**: [`Frontend/components/checkout-button.tsx`](file:///e:/Accounting/Frontend/components/checkout-button.tsx)
- **Role**: Reusable checkout trigger component.

### Security & Invariant Workflow
1. Generates client-side idempotency keys: `idemp_${orderId}_${Date.now()}`.
2. Dispatches `createPaymentAttempt(orderId, params)`.
3. Validates gateway destination origin using `isApprovedGatewayOrigin(checkoutUrl)`.
4. Redirects browser via `window.location.assign(checkoutUrl)` to PayMongo or local sandbox.

---

## 8. `Frontend/components/sidebar.tsx` & `Frontend/components/theme-provider.tsx`
- **Paths**: [`Frontend/components/sidebar.tsx`](file:///e:/Accounting/Frontend/components/sidebar.tsx), [`Frontend/components/theme-provider.tsx`](file:///e:/Accounting/Frontend/components/theme-provider.tsx)
- **Role**: Accessible navigation sidebar and light/dark theme provider wrapper.

---

## 9. `Frontend/components/ui/*`
- **Path**: `Frontend/components/ui/`
- **Role**: Modular Radix UI and Tailwind CSS design components.
- **Components Included**: `button.tsx`, `card.tsx`, `dialog.tsx`, `badge.tsx`, `table.tsx`, `tabs.tsx`, `alert.tsx`, `alert-dialog.tsx`, `accordion.tsx`, `avatar.tsx`, `checkbox.tsx`, `dropdown-menu.tsx`, `input.tsx`, `label.tsx`, `select.tsx`, `separator.tsx`, `toast.tsx`, `tooltip.tsx`, `chart.tsx`.

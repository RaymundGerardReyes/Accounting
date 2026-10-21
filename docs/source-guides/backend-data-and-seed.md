# Backend Master Data & Flat Files Guide

This guide details the structure, schema, and accounting semantics of the master data files stored in `Backend/data/`.

---

## 1. `Backend/data/configuration.json`
- **Path**: [`Backend/data/configuration.json`](file:///e:/Accounting/Backend/data/configuration.json)
- **Role**: System-wide configuration, cost center registry, user roster, and payment fulfillment audit logs.

### Key Sections
1. **`config`**:
   - `companyName`: Entity legal title (`"R.G.E Accounting Firm Inc."`).
   - `fiscalYear`: Active financial year (`"2025"`).
   - `allocationMethod`: Default overhead distribution method (`"activity-based"`).
   - `reportingCurrency`: Set to `"PHP"` (Philippine Peso).
   - `decimalPlaces`: Standard decimal precision (`2`).
   - `autoBackup`: Automatic persistence flag.
   - `notificationEmail`: Administrative alert recipient.
2. **`departments`**:
   - Array of cost centers:
     - `manufacturing`: Cost Center `CC-001`, Budget: ₱450,000.00.
     - `assembly`: Cost Center `CC-002`, Budget: ₱300,000.00.
     - `qa`: Cost Center `CC-003`, Budget: ₱150,000.00.
     - `logistics`: Cost Center `CC-004`, Budget: ₱100,000.00.
3. **`users`**:
   - Role-based user accounts with `id`, `name`, `role` (`Admin`, `Analyst`, `Viewer`, `Auditor`), `email`, `status`.
4. **`auditLogs`**:
   - Prepended by `PaymentOrchestrator.process_fulfillment_outbox()` whenever a payment attempt succeeds:
     ```json
     {
         "id": 1,
         "action": "Payment received: ₱1,250.00 for ORD-202510-0001 (Batch Production Run)",
         "user": "PayMongo Gateway",
         "time": "Just now",
         "severity": "success"
     }
     ```

---

## 2. `Backend/data/dashboard_data.json`
- **Path**: [`Backend/data/dashboard_data.json`](file:///e:/Accounting/Backend/data/dashboard_data.json)
- **Role**: Aggregate high-level cost trends, department allocations, and metrics.

### Key Data Structures
- **`costTrendData`**:
  - 12-month array of total production costs, broken down by:
    - `total`: Total manufacturing expense.
    - `labor`: Direct labor wages.
    - `materials`: Raw material purchases.
    - `overhead`: Factory overhead and utilities.
- **`departmentData`**:
  - Proportional cost distributions used by PieChart and BarChart components.
- **`departmentMetrics`**:
  - Per-department performance cards containing `allocated`, `actual`, `variance`, `variancePercent`, `status` (`favorable` | `unfavorable`), `trend`, and `change`.
- **`varianceTrendData`**:
  - Monthly tracking of favorable vs. unfavorable variance instances.

---

## 3. `Backend/data/department_detail_data.json`
- **Path**: [`Backend/data/department_detail_data.json`](file:///e:/Accounting/Backend/data/department_detail_data.json)
- **Role**: Granular drill-down data keyed by normalized department slugs (`manufacturing`, `assembly`, `qa`, `logistics`).

### Data Elements per Department
- **`departmentStats`**:
  - `allocated`: Budgeted allowance.
  - `actual`: Realized spending.
  - `variance`: Computed as $\text{Budget} - \text{Actual}$.
  - `variancePercent`: Proportional variance percentage.
  - `status`: `"favorable"` if $\ge 0$, `"unfavorable"` if $< 0$.
- **`costBreakdown`**:
  - Line-item breakdown across Labor, Materials, Overhead, and Utilities.
- **`monthlyTrend`**:
  - 6-month historical spending curves comparing allocated vs. actual costs.

---

## 4. `Backend/data/product_data.json`
- **Path**: [`Backend/data/product_data.json`](file:///e:/Accounting/Backend/data/product_data.json)
- **Role**: Master product catalog providing authoritative unit costs for order pricing.

### Catalog Items
| Product | Unit Cost | Selling Price | Monthly Volume | Gross Margin | Status |
|---|---|---|---|---|---|
| **Product A** | ₱125.00 | ₱185.00 | 2,500 units | 32.4% | Growing |
| **Product B** | ₱89.00 | ₱135.00 | 4,200 units | 34.1% | Stable |
| **Product C** | ₱156.00 | ₱220.00 | 1,800 units | 29.1% | Growing |
| **Product D** | ₱203.00 | ₱275.00 | 950 units | 26.2% | Declining |
| **Product E** | ₱67.00 | ₱110.00 | 5,600 units | 39.1% | Growing |

---

## 5. `Backend/data/variance_data.json`
- **Path**: [`Backend/data/variance_data.json`](file:///e:/Accounting/Backend/data/variance_data.json)
- **Role**: Budget vs. actual cost comparisons and root cause tracking.

### Semantics
- **Sign Invariant**:
  $$\text{Variance} = \text{Budget} - \text{Actual}$$
  - Negative values (e.g. Labor: Budget ₱180,000, Actual ₱185,000, Variance **-₱5,000**) represent cost overruns (**unfavorable**).
  - Positive values (e.g. Materials: Budget ₱150,000, Actual ₱148,000, Variance **+₱2,000**) represent cost savings (**favorable**).
- **`varianceReasons`**:
  - Explanatory audit items documenting why discrepancies occurred (e.g., "Overtime due to machine breakdown", "Bulk purchasing discount").

---
description: Frontend defensive aggregation invariants, collection reducers, API response contract normalization, and empty-state UX
---

# Frontend Resilience & API Contract Guardrails

## 1. Resilient Reducer and Collection Aggregation Invariant
- **Invariant**: Any client-side calculation using `.reduce()`, `.map()`, or `.filter()` across API response collections MUST defensively default missing or non-array payloads to `[]` and guard every element property against `null` or `undefined`.
- **Division-by-Zero Guard**: Never compute averages using `sum / items.length` without checking `items.length > 0`. Computing `0 / 0` produces `NaN`, which renders corrupted UI text such as `NaN%` or `₱NaN`.
- **Standard Pattern**:
  ```typescript
  const list = Array.isArray(rawData) ? rawData : (rawData?.items || [])
  const count = list.length
  const total = list.reduce((sum, item) => sum + (item?.amount || 0), 0)
  const average = count > 0 ? (total / count).toFixed(1) : "0.0"
  ```

## 2. Dual Naming Normalization on Ingestion
- **Invariant**: Frontend modules consuming backend APIs must support both camelCase and snake_case payload structures, as well as flat array vs wrapper object payloads.
- **Rationale**: Minimal APIs and legacy controllers may return data under snake_case keys (e.g., `product_data`, `cost_breakdown`) or camelCase keys (`productData`, `costBreakdown`) depending on serializer settings or backward-compatibility transforms.
- **Standard Pattern**:
  ```typescript
  // Support { product_data: [...] }, { productData: [...] }, { products: [...] }, or raw [...]
  const rawList = Array.isArray(data)
    ? data
    : (data?.product_data || data?.productData || data?.products || [])
  const normalizedList = Array.isArray(rawList) ? rawList : []
  ```

## 3. Empty State UI Fallbacks
- **Invariant**: When an analytical or reporting dataset has 0 items, components must render an informative empty state placeholder rather than empty, broken charts or tables with `0` or `NaN` metrics.
- **Standard Pattern**: Provide a distinct card or banner (e.g. `EmptyState`, alert callout, or "No product performance data available for this reporting period") to provide clear feedback to the operator.


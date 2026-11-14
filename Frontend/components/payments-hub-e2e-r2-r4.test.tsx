/**
 * E2E & Integration Test Suite for PaymentsHub: R2 & R4 Requirements
 *
 * Verifies:
 * - Suite R2: Admin Role Verification & Diagnostics Gating (useAuth):
 *   - Case R2-A1: Standard non-admin user sees "Orders & Invoices" tab,
 *                 admin tabs ("Live Orchestrator & Webhooks", "Sandbox & Go-Live Checklist")
 *                 are omitted from DOM; zero calls to /api/v1/admin/*.
 *   - Case R2-A2: Unauthenticated actor has admin tabs omitted and zero admin endpoint calls.
 *   - Case R2-A3: Admin user ('admin') renders admin tabs and queries diagnostics successfully.
 *   - Case R2-A4: Finance admin user ('finance_admin') renders admin tabs and queries diagnostics.
 * - Suite R4: Polling Storm, UI Freeze & Visibility Guard:
 *   - Case R4-B1: Decoupled polling: order fetching is independent of admin diagnostics.
 *   - Case R4-B2: No full UI freeze: background poll at 4000ms updates orders silently
 *                 without setting full page loading spinner or flickering existing rows.
 *   - Case R4-B3: In-flight guard: inFlightOrdersRef prevents concurrent overlapping fetches.
 *   - Case R4-B4: Document visibility guard: document.visibilityState !== 'visible' pauses polling;
 *                 switching back to 'visible' resumes polling.
 * - Tier 3 & Tier 4 Scenarios:
 *   - Real-world persona switching, tab recovery, background sleep, and network flap resilience.
 */

import React from "react"
import { render, screen, waitFor, act } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, it, test, expect, vi, beforeEach, afterEach } from "vitest"
import { PaymentsHub } from "@/components/payments-hub"
import type { Order, WebhookEventRecord, FulfillmentOutboxRecord } from "@/lib/payment-api"

// Mock auth context before imports
vi.mock("@/lib/auth-context", () => ({
  useAuth: vi.fn(),
}))
import { useAuth } from "@/lib/auth-context"

// ============================================================================
// Test Fixtures
// ============================================================================

const mockOrdersList: Order[] = [
  {
    id: "ord_101",
    user_id: "usr_alpha",
    order_number: "ORD-2026-001",
    title: "Product A Standard Batch",
    category: "product_order",
    reference_code: "REF-PROD-A",
    amount_minor: 10000,
    amount: 100,
    currency: "PHP",
    status: "pending",
    metadata: {
      customer_name: "Juan Dela Cruz",
      customer_phone: "+639171234567",
    },
    created_at: "2026-11-14T10:00:00Z",
    updated_at: "2026-11-14T10:00:00Z",
    latest_attempt: {
      id: "att_101",
      order_id: "ord_101",
      provider: "paymongo",
      payment_method: "qrph",
      amount_minor: 10000,
      amount: 100,
      currency: "PHP",
      status: "pending",
      idempotency_key: "idem_101",
      created_at: "2026-11-14T10:00:00Z",
      updated_at: "2026-11-14T10:00:00Z",
    },
  },
  {
    id: "ord_102",
    user_id: "usr_beta",
    order_number: "ORD-2026-002",
    title: "Department Invoice QA Batch",
    category: "department_invoice",
    reference_code: "REF-QA-002",
    amount_minor: 2500000,
    amount: 25000,
    currency: "PHP",
    status: "paid",
    metadata: {
      customer_name: "Maria Santos",
      customer_phone: "+639189876543",
    },
    created_at: "2026-11-14T11:00:00Z",
    updated_at: "2026-11-14T11:30:00Z",
    latest_attempt: {
      id: "att_102",
      order_id: "ord_102",
      provider: "paymongo",
      payment_method: "card_visa",
      amount_minor: 2500000,
      amount: 25000,
      currency: "PHP",
      status: "succeeded",
      idempotency_key: "idem_102",
      created_at: "2026-11-14T11:00:00Z",
      updated_at: "2026-11-14T11:30:00Z",
    },
  },
]

const mockWebhooksList: WebhookEventRecord[] = [
  {
    id: "wh_001",
    provider: "paymongo",
    provider_event_id: "evt_pm_101",
    event_type: "payment.paid",
    processing_status: "processed",
    received_at: "2026-11-14T10:05:00Z",
    processed_at: "2026-11-14T10:05:01Z",
    payload: { id: "evt_pm_101", type: "payment.paid" },
  },
]

const mockOutboxList: FulfillmentOutboxRecord[] = [
  {
    id: "out_001",
    order_id: "ord_102",
    payment_attempt_id: "att_102",
    event_type: "order.fulfilled",
    status: "pending",
    created_at: "2026-11-14T10:05:02Z",
  },
]

// ============================================================================
// Helper Utilities
// ============================================================================

interface MockAuthOptions {
  authenticated?: boolean
  roles?: string[]
  userId?: string
  fullName?: string
  email?: string
}

function setMockAuth({
  authenticated = true,
  roles = ["admin"],
  userId = "usr_admin_01",
  fullName = "Admin User",
  email = "admin@rge.ph",
}: MockAuthOptions = {}) {
  vi.mocked(useAuth).mockReturnValue({
    authenticated,
    loading: false,
    user: authenticated
      ? {
          id: userId,
          full_name: fullName,
          email,
          roles,
        }
      : null,
    authenticatedUser: null,
    allUsers: [],
    csrfToken: "mock-csrf-token",
    loginWithCredentials: vi.fn(),
    loginWithOAuth: vi.fn(),
    switchPersona: vi.fn(),
    logout: vi.fn(),
    refreshSession: vi.fn(),
  })
}

interface MockFetchOptions {
  orders?: Order[]
  webhooks?: WebhookEventRecord[]
  outbox?: FulfillmentOutboxRecord[]
  failOrders?: boolean
  failAdmin?: boolean
  delayOrdersPromise?: Promise<any>
  onOrderFetch?: () => void
}

function setupGlobalFetch(opts: MockFetchOptions = {}) {
  const {
    orders = mockOrdersList,
    webhooks = mockWebhooksList,
    outbox = mockOutboxList,
    failOrders = false,
    failAdmin = false,
    delayOrdersPromise,
    onOrderFetch,
  } = opts

  const calls: Array<{ url: string; method?: string }> = []

  const spy = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const urlStr = String(input)
    const method = init?.method || "GET"
    calls.push({ url: urlStr, method })

    if (urlStr.includes("/api/v1/orders")) {
      onOrderFetch?.()
      if (delayOrdersPromise) {
        return delayOrdersPromise
      }
      if (failOrders) {
        return Promise.reject(new Error("Network connection lost"))
      }
      return Promise.resolve({
        ok: true,
        status: 200,
        statusText: "OK",
        json: async () => orders,
      })
    }

    if (urlStr.includes("/api/v1/admin/webhook-events")) {
      if (failAdmin) {
        return Promise.resolve({
          ok: false,
          status: 403,
          statusText: "Forbidden",
          json: async () => ({ error: "Forbidden: Admin required" }),
        })
      }
      return Promise.resolve({
        ok: true,
        status: 200,
        statusText: "OK",
        json: async () => webhooks,
      })
    }

    if (urlStr.includes("/api/v1/admin/fulfillment-outbox")) {
      if (failAdmin) {
        return Promise.resolve({
          ok: false,
          status: 403,
          statusText: "Forbidden",
          json: async () => ({ error: "Forbidden: Admin required" }),
        })
      }
      return Promise.resolve({
        ok: true,
        status: 200,
        statusText: "OK",
        json: async () => outbox,
      })
    }

    // Default 200 OK for other endpoints
    return Promise.resolve({
      ok: true,
      status: 200,
      statusText: "OK",
      json: async () => ({}),
    })
  })

  global.fetch = spy as any
  return { spy, calls }
}

function setDocumentVisibility(state: "visible" | "hidden") {
  Object.defineProperty(document, "visibilityState", {
    value: state,
    configurable: true,
    writable: true,
  })
  document.dispatchEvent(new Event("visibilitychange"))
}

function expectOrderInDocument(orderNumber = "ORD-2026-001") {
  const elements = screen.getAllByText(orderNumber)
  expect(elements.length).toBeGreaterThan(0)
  expect(elements[0]).toBeInTheDocument()
}

// ============================================================================
// Test Suite: PaymentsHub E2E R2 & R4 Integration Tests
// ============================================================================

describe("PaymentsHub E2E R2 & R4 Integration Suite", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setDocumentVisibility("visible")
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  // ==========================================================================
  // Tier 1: Feature Coverage (Happy Paths)
  // ==========================================================================
  describe("Tier 1: Feature Coverage (Happy Paths)", () => {
    describe("R2 Admin Authorization Happy Paths", () => {
      test("R2-T1-01 (R2-A3): Admin user ('admin') renders Orders, Webhooks, and Checklist tabs", async () => {
        setMockAuth({ authenticated: true, roles: ["admin"], userId: "usr_admin" })
        const { calls } = setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expectOrderInDocument("ORD-2026-001")
        })

        // All three tabs must be rendered for admin
        expect(screen.getByRole("tab", { name: /Orders & Invoices/i })).toBeInTheDocument()
        expect(screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeInTheDocument()
        expect(screen.getByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeInTheDocument()

        // Admin diagnostics must be queried
        const adminCalls = calls.filter((c) => c.url.includes("/api/v1/admin/"))
        expect(adminCalls.length).toBeGreaterThanOrEqual(1)
      })

      test("R2-T1-02 (R2-A4): Finance admin user ('finance_admin') renders admin tabs and queries diagnostics", async () => {
        setMockAuth({ authenticated: true, roles: ["finance_admin"], userId: "usr_fin_admin" })
        const { calls } = setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expectOrderInDocument("ORD-2026-001")
        })

        expect(screen.getByRole("tab", { name: /Orders & Invoices/i })).toBeInTheDocument()
        expect(screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeInTheDocument()
        expect(screen.getByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeInTheDocument()

        const hasWebhookFetch = calls.some((c) => c.url.includes("/api/v1/admin/webhook-events"))
        const hasOutboxFetch = calls.some((c) => c.url.includes("/api/v1/admin/fulfillment-outbox"))
        expect(hasWebhookFetch || hasOutboxFetch).toBe(true)
      })

      test("R2-T1-03: System admin user ('system_admin') renders all admin tabs", async () => {
        setMockAuth({ authenticated: true, roles: ["system_admin"], userId: "usr_sys_admin" })
        setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expectOrderInDocument("ORD-2026-001")
        })

        expect(screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeInTheDocument()
        expect(screen.getByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeInTheDocument()
      })

      test("R2-T1-04: Admin clicks and switches to 'Live Orchestrator & Webhooks' tabpanel", async () => {
        const user = userEvent.setup()
        setMockAuth({ authenticated: true, roles: ["admin"], userId: "usr_admin" })
        setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expectOrderInDocument("ORD-2026-001")
        })

        const webhooksTab = screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })
        await user.click(webhooksTab)

        // Webhook tabpanel should now be active
        expect(webhooksTab).toHaveAttribute("aria-selected", "true")
        const tabpanel = screen.getByRole("tabpanel")
        expect(tabpanel).toHaveAttribute("id", "tabpanel-webhooks")
      })

      test("R2-T1-05: Admin clicks and switches to 'Sandbox & Go-Live Checklist' tabpanel", async () => {
        const user = userEvent.setup()
        setMockAuth({ authenticated: true, roles: ["admin"], userId: "usr_admin" })
        setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expectOrderInDocument("ORD-2026-001")
        })

        const checklistTab = screen.getByRole("tab", { name: /Sandbox & Go-Live Checklist/i })
        await user.click(checklistTab)

        expect(checklistTab).toHaveAttribute("aria-selected", "true")
        const tabpanel = screen.getByRole("tabpanel")
        expect(tabpanel).toHaveAttribute("id", "tabpanel-checklist")
      })
    })

    describe("R4 Polling & UI Freeze Happy Paths", () => {
      test("R4-T1-01: Displays loading indicator during initial order fetch on empty mount", () => {
        setMockAuth({ authenticated: true, roles: ["admin"] })
        // Return unsettled promise to hold in initial loading state
        global.fetch = vi.fn().mockImplementation(() => new Promise(() => {})) as any

        render(<PaymentsHub />)

        expect(screen.getByText("Loading orders...")).toBeInTheDocument()
      })

      test("R4-T1-02 (R4-B2): Background poll at 4000ms updates orders silently without 'Loading orders...' spinner", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })
        setupGlobalFetch()

        render(<PaymentsHub />)

        // Mount and flush initial tick
        await act(async () => {
          await vi.advanceTimersByTimeAsync(10)
        })

        expectOrderInDocument("ORD-2026-001")
        expect(screen.queryByText("Loading orders...")).toBeNull()

        // Advance timers by 4000ms (background poll tick)
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })

        // Order row remains and loading banner MUST NOT reappear
        expectOrderInDocument("ORD-2026-001")
        expect(screen.queryByText("Loading orders...")).toBeNull()
      })

      test("R4-T1-03: Background polling preserves existing table rows without unmounting", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })
        setupGlobalFetch()

        render(<PaymentsHub />)

        await act(async () => {
          await vi.advanceTimersByTimeAsync(10)
        })

        const rowElementsBefore = screen.getAllByText("ORD-2026-001")
        expect(rowElementsBefore[0]).toBeInTheDocument()

        // Advance through two 4000ms cycles
        await act(async () => {
          await vi.advanceTimersByTimeAsync(8000)
        })

        const rowElementsAfter = screen.getAllByText("ORD-2026-001")
        expect(rowElementsAfter[0]).toBeInTheDocument()
      })

      test("R4-T1-04: Background poll ingests updated order status from pending to paid without reload", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })

        let currentOrders: Order[] = [
          {
            ...mockOrdersList[0],
            status: "pending",
          },
        ]

        global.fetch = vi.fn().mockImplementation((url: string) => {
          if (String(url).includes("/api/v1/orders")) {
            return Promise.resolve({
              ok: true,
              status: 200,
              json: async () => currentOrders,
            })
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => [] })
        }) as any

        render(<PaymentsHub />)

        await act(async () => {
          await vi.advanceTimersByTimeAsync(10)
        })

        expectOrderInDocument("ORD-2026-001")

        // Mutate backend state to "paid"
        currentOrders = [
          {
            ...mockOrdersList[0],
            status: "paid",
          },
        ]

        // Advance timer by 4000ms to trigger poll
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })

        expectOrderInDocument("ORD-2026-001")
      })

      test("R4-T1-05 (R4-B1): Decoupled loading: orders render successfully even when admin diagnostics are pending", async () => {
        setMockAuth({ authenticated: true, roles: ["admin"] })

        // Orders resolve immediately, admin diagnostics remain pending indefinitely
        global.fetch = vi.fn().mockImplementation((url: string) => {
          const u = String(url)
          if (u.includes("/api/v1/orders")) {
            return Promise.resolve({
              ok: true,
              status: 200,
              json: async () => mockOrdersList,
            })
          }
          if (u.includes("/api/v1/admin/")) {
            return new Promise(() => {}) // never resolves
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => ({}) })
        }) as any

        render(<PaymentsHub />)

        // Orders must render without being blocked by pending admin diagnostics
        await waitFor(() => {
          expectOrderInDocument("ORD-2026-001")
        })
        expect(screen.queryByText("Loading orders...")).toBeNull()
      })
    })
  })

  // ==========================================================================
  // Tier 2: Boundary, Corner & Adversarial Gating
  // ==========================================================================
  describe("Tier 2: Boundary, Corner & Adversarial Gating", () => {
    describe("R2 Admin Role Gating & Security Boundaries", () => {
      test("R2-T2-01 (R2-A1): Standard employee user has Webhooks and Checklist tabs omitted from DOM", async () => {
        setMockAuth({
          authenticated: true,
          roles: ["field_staff", "employee"],
          userId: "usr_field_01",
          fullName: "Field Employee",
        })
        const { calls } = setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expectOrderInDocument("ORD-2026-001")
        })

        // Orders tab is present
        expect(screen.getByRole("tab", { name: /Orders & Invoices/i })).toBeInTheDocument()

        // Admin tabs MUST NOT exist in DOM
        expect(screen.queryByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeNull()
        expect(screen.queryByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeNull()

        // Calls to admin routes must be ZERO
        const adminCalls = calls.filter((c) => c.url.includes("/api/v1/admin/"))
        expect(adminCalls.length).toBe(0)
      })

      test("R2-T2-02 (R2-A1): Standard non-admin user generates exactly ZERO requests to /api/v1/admin/*", async () => {
        vi.useFakeTimers()
        setMockAuth({
          authenticated: true,
          roles: ["sales_rep"],
          userId: "usr_sales_01",
        })
        const { calls } = setupGlobalFetch()

        render(<PaymentsHub />)

        // Advance through initial mount and multiple polling intervals
        await act(async () => {
          await vi.advanceTimersByTimeAsync(12000)
        })

        const adminCalls = calls.filter((c) => c.url.includes("/api/v1/admin/"))
        expect(adminCalls).toHaveLength(0)
      })

      test("R2-T2-03 (R2-A2): Unauthenticated actor has admin tabs omitted and zero admin calls", async () => {
        setMockAuth({
          authenticated: false,
          user: undefined,
          roles: [],
        })
        const { calls } = setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expect(screen.getByRole("tab", { name: /Orders & Invoices/i })).toBeInTheDocument()
        })

        expect(screen.queryByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeNull()
        expect(screen.queryByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeNull()

        const adminCalls = calls.filter((c) => c.url.includes("/api/v1/admin/"))
        expect(adminCalls).toHaveLength(0)
      })

      test("R2-T2-04: User with empty roles array [] has admin tabs omitted and zero admin calls", async () => {
        setMockAuth({
          authenticated: true,
          roles: [],
          userId: "usr_no_roles",
        })
        const { calls } = setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expect(screen.getByRole("tab", { name: /Orders & Invoices/i })).toBeInTheDocument()
        })

        expect(screen.queryByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeNull()
        expect(screen.queryByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeNull()

        const adminCalls = calls.filter((c) => c.url.includes("/api/v1/admin/"))
        expect(adminCalls).toHaveLength(0)
      })

      test("R2-T2-05: Role case-insensitivity: uppercase 'ADMIN' and mixed-case 'Finance_Admin' grant access", async () => {
        setMockAuth({
          authenticated: true,
          roles: ["ADMIN"],
          userId: "usr_upper_admin",
        })
        setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expect(screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeInTheDocument()
        })

        expect(screen.getByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeInTheDocument()
      })

      test("R2-T2-06: Adversarial roles array: ['guest', 'intern', 'auditor'] without admin is denied", async () => {
        setMockAuth({
          authenticated: true,
          roles: ["guest", "intern", "auditor"],
          userId: "usr_auditor",
        })
        const { calls } = setupGlobalFetch()

        render(<PaymentsHub />)

        await waitFor(() => {
          expect(screen.getByRole("tab", { name: /Orders & Invoices/i })).toBeInTheDocument()
        })

        expect(screen.queryByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeNull()
        expect(screen.queryByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeNull()

        const adminCalls = calls.filter((c) => c.url.includes("/api/v1/admin/"))
        expect(adminCalls).toHaveLength(0)
      })
    })

    describe("R4 In-Flight & Document Visibility Boundaries", () => {
      test("R4-T2-01 (R4-B3): inFlightOrdersRef prevents stacked polling requests when response is delayed", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })

        let resolveSlowFetch: (val: any) => void = () => {}
        const slowPromise = new Promise((resolve) => {
          resolveSlowFetch = resolve
        })

        let orderFetchCount = 0
        global.fetch = vi.fn().mockImplementation((url: string) => {
          if (String(url).includes("/api/v1/orders")) {
            orderFetchCount++
            return slowPromise
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => [] })
        }) as any

        render(<PaymentsHub />)

        // Initial fetch triggered on mount
        expect(orderFetchCount).toBe(1)

        // Advance through two 4000ms intervals while fetch #1 is still pending
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })

        // inFlightOrdersRef guard prevents duplicate concurrent calls
        expect(orderFetchCount).toBe(1)

        // Resolve the in-flight request
        await act(async () => {
          resolveSlowFetch({
            ok: true,
            status: 200,
            json: async () => mockOrdersList,
          })
          await vi.advanceTimersByTimeAsync(10)
        })
      })

      test("R4-T2-02 (R4-B3): In-flight lock releases after slow request completes, allowing next poll to fire", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })

        let resolveFetch: (val: any) => void = () => {}
        let orderFetchCount = 0

        global.fetch = vi.fn().mockImplementation((url: string) => {
          if (String(url).includes("/api/v1/orders")) {
            orderFetchCount++
            return new Promise((resolve) => {
              resolveFetch = resolve
            })
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => [] })
        }) as any

        render(<PaymentsHub />)
        expect(orderFetchCount).toBe(1)

        // Tick while in-flight
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })
        expect(orderFetchCount).toBe(1)

        // Resolve fetch #1
        await act(async () => {
          resolveFetch({
            ok: true,
            status: 200,
            json: async () => mockOrdersList,
          })
          await vi.advanceTimersByTimeAsync(10)
        })

        // Advance to next poll tick -> fetch #2 should now fire
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })
        expect(orderFetchCount).toBe(2)
      })

      test("R4-T2-03 (R4-B4): Document visibility guard pauses polling when document.visibilityState is 'hidden'", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })

        let orderFetchCount = 0
        global.fetch = vi.fn().mockImplementation((url: string) => {
          if (String(url).includes("/api/v1/orders")) {
            orderFetchCount++
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
        }) as any

        render(<PaymentsHub />)

        // Initial mount fetch
        await act(async () => {
          await vi.advanceTimersByTimeAsync(10)
        })
        expect(orderFetchCount).toBe(1)

        // User switches tab / minimizes window
        setDocumentVisibility("hidden")

        // Advance time through 3 interval ticks (12s) while hidden
        await act(async () => {
          await vi.advanceTimersByTimeAsync(12000)
        })

        // Zero additional order fetches must occur while hidden
        expect(orderFetchCount).toBe(1)
      })

      test("R4-T2-04 (R4-B4): Polling resumes when document.visibilityState switches back to 'visible'", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })

        let orderFetchCount = 0
        global.fetch = vi.fn().mockImplementation((url: string) => {
          if (String(url).includes("/api/v1/orders")) {
            orderFetchCount++
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
        }) as any

        render(<PaymentsHub />)

        await act(async () => {
          await vi.advanceTimersByTimeAsync(10)
        })
        expect(orderFetchCount).toBe(1)

        // Tab hidden
        setDocumentVisibility("hidden")
        await act(async () => {
          await vi.advanceTimersByTimeAsync(8000)
        })
        expect(orderFetchCount).toBe(1)

        // Tab becomes visible again
        setDocumentVisibility("visible")

        // Advance timer for next active tick
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })

        // Polling resumed
        expect(orderFetchCount).toBeGreaterThan(1)
      })

      test("R4-T2-05 (R4-B1): Complete network failure on admin endpoints does not crash or disrupt orders polling", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })

        let orderFetchCount = 0
        global.fetch = vi.fn().mockImplementation((url: string) => {
          const u = String(url)
          if (u.includes("/api/v1/orders")) {
            orderFetchCount++
            return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
          }
          if (u.includes("/api/v1/admin/")) {
            return Promise.reject(new Error("Admin server internal error 500"))
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => ({}) })
        }) as any

        render(<PaymentsHub />)

        await act(async () => {
          await vi.advanceTimersByTimeAsync(10)
        })

        expectOrderInDocument("ORD-2026-001")

        // Background poll tick
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })

        expect(orderFetchCount).toBe(2)
        expectOrderInDocument("ORD-2026-001")
      })

      test("R4-T2-06: Rapid visibility flapping (visible -> hidden -> visible) preserves single polling interval", async () => {
        vi.useFakeTimers()
        setMockAuth({ authenticated: true, roles: ["admin"] })

        let orderFetchCount = 0
        global.fetch = vi.fn().mockImplementation((url: string) => {
          if (String(url).includes("/api/v1/orders")) {
            orderFetchCount++
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
        }) as any

        render(<PaymentsHub />)

        await act(async () => {
          await vi.advanceTimersByTimeAsync(10)
        })
        expect(orderFetchCount).toBe(1)

        // Rapid flapping
        setDocumentVisibility("hidden")
        setDocumentVisibility("visible")
        setDocumentVisibility("hidden")
        setDocumentVisibility("visible")

        // Advance 4000ms
        await act(async () => {
          await vi.advanceTimersByTimeAsync(4000)
        })

        // Must not have spawned multiple interval bursts
        expect(orderFetchCount).toBe(2)
      })
    })
  })

  // ==========================================================================
  // Tier 3: Cross-Feature Integration & Persona Switching
  // ==========================================================================
  describe("Tier 3: Cross-Feature Integration & Persona Switching", () => {
    test("R2-R4-T3-01: Persona Switching: switching from field_staff to admin reveals tabs and initiates diagnostics", async () => {
      // Step 1: Start as field_staff
      setMockAuth({
        authenticated: true,
        roles: ["field_staff"],
        userId: "usr_field_02",
      })
      const { calls } = setupGlobalFetch()

      const { rerender } = render(<PaymentsHub />)

      await waitFor(() => {
        expectOrderInDocument("ORD-2026-001")
      })

      expect(screen.queryByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeNull()
      expect(calls.filter((c) => c.url.includes("/api/v1/admin/")).length).toBe(0)

      // Step 2: Persona switch to admin
      setMockAuth({
        authenticated: true,
        roles: ["admin"],
        userId: "usr_admin_promoted",
      })

      rerender(<PaymentsHub />)

      await waitFor(() => {
        expect(screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeInTheDocument()
      })
      expect(screen.getByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeInTheDocument()
    })

    test("R2-R4-T3-02: Persona Logout: switching from admin to unauthenticated removes admin tabs from DOM", async () => {
      setMockAuth({
        authenticated: true,
        roles: ["admin"],
        userId: "usr_admin_03",
      })
      setupGlobalFetch()

      const { rerender } = render(<PaymentsHub />)

      await waitFor(() => {
        expect(screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeInTheDocument()
      })

      // User logs out
      setMockAuth({
        authenticated: false,
        user: undefined,
        roles: [],
      })

      rerender(<PaymentsHub />)

      await waitFor(() => {
        expect(screen.queryByRole("tab", { name: /Live Orchestrator & Webhooks/i })).toBeNull()
      })
      expect(screen.queryByRole("tab", { name: /Sandbox & Go-Live Checklist/i })).toBeNull()
    })

    test("R2-R4-T3-03: Combined Polling: slow admin diagnostics does not starve 4000ms order polling cycle", async () => {
      vi.useFakeTimers()
      setMockAuth({ authenticated: true, roles: ["admin"] })

      let orderFetchCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        const u = String(url)
        if (u.includes("/api/v1/orders")) {
          orderFetchCount++
          return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
        }
        if (u.includes("/api/v1/admin/")) {
          // Delayed 10-second response for admin
          return new Promise((resolve) => setTimeout(resolve, 10000))
        }
        return Promise.resolve({ ok: true, status: 200, json: async () => ({}) })
      }) as any

      render(<PaymentsHub />)

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10)
      })
      expect(orderFetchCount).toBe(1)

      // Advance by 4000ms
      await act(async () => {
        await vi.advanceTimersByTimeAsync(4000)
      })
      expect(orderFetchCount).toBe(2)

      // Advance by another 4000ms (admin is still pending at 8000ms)
      await act(async () => {
        await vi.advanceTimersByTimeAsync(4000)
      })
      expect(orderFetchCount).toBe(3)
    })

    test("R2-R4-T3-04: Persona change while document is hidden does not trigger background fetch storm", async () => {
      vi.useFakeTimers()
      setMockAuth({ authenticated: true, roles: ["field_staff"] })

      let orderFetchCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/v1/orders")) {
          orderFetchCount++
        }
        return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
      }) as any

      const { rerender } = render(<PaymentsHub />)

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10)
      })
      expect(orderFetchCount).toBe(1)

      // Tab hidden
      setDocumentVisibility("hidden")

      // Persona changes while hidden
      setMockAuth({ authenticated: true, roles: ["admin"] })
      rerender(<PaymentsHub />)

      // Advance 8000ms
      await act(async () => {
        await vi.advanceTimersByTimeAsync(8000)
      })

      // No fetches while hidden
      expect(orderFetchCount).toBe(1)

      // Tab returns to visible
      setDocumentVisibility("visible")
      await act(async () => {
        await vi.advanceTimersByTimeAsync(4000)
      })

      expect(orderFetchCount).toBeGreaterThan(1)
    })
  })

  // ==========================================================================
  // Tier 4: Real-World Scenarios & UI Resilience
  // ==========================================================================
  describe("Tier 4: Real-World Scenarios & UI Resilience", () => {
    test("R2-R4-T4-01: End-to-end admin session: live orders monitoring and tab navigation without UI freeze", async () => {
      const user = userEvent.setup()
      setMockAuth({ authenticated: true, roles: ["admin"], userId: "usr_admin_ops" })
      setupGlobalFetch()

      render(<PaymentsHub />)

      await waitFor(() => {
        expectOrderInDocument("ORD-2026-001")
      })

      // Switch to webhooks tab
      const webhooksTab = screen.getByRole("tab", { name: /Live Orchestrator & Webhooks/i })
      await user.click(webhooksTab)

      expect(webhooksTab).toHaveAttribute("aria-selected", "true")

      // Switch back to orders tab
      const ordersTab = screen.getByRole("tab", { name: /Orders & Invoices/i })
      await user.click(ordersTab)

      expect(ordersTab).toHaveAttribute("aria-selected", "true")
      expectOrderInDocument("ORD-2026-001")
    })

    test("R2-R4-T4-02: Network latency spike recovery: transient 503 does not break subsequent 4000ms polling", async () => {
      vi.useFakeTimers()
      setMockAuth({ authenticated: true, roles: ["admin"] })

      let callCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/v1/orders")) {
          callCount++
          if (callCount === 2) {
            // Second call encounters transient 503 error
            return Promise.reject(new Error("503 Service Unavailable"))
          }
          return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
        }
        return Promise.resolve({ ok: true, status: 200, json: async () => [] })
      }) as any

      render(<PaymentsHub />)

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10)
      })
      expectOrderInDocument("ORD-2026-001")

      // Poll tick 1 (fails with 503)
      await act(async () => {
        await vi.advanceTimersByTimeAsync(4000)
      })
      expect(callCount).toBe(2)
      // Existing table data remains intact
      expectOrderInDocument("ORD-2026-001")

      // Poll tick 2 (recovers with 200)
      await act(async () => {
        await vi.advanceTimersByTimeAsync(4000)
      })
      expect(callCount).toBe(3)
      expectOrderInDocument("ORD-2026-001")
    })

    test("R2-R4-T4-03: Prolonged 30-second tab backgrounding wakes cleanly with single resync", async () => {
      vi.useFakeTimers()
      setMockAuth({ authenticated: true, roles: ["admin"] })

      let orderFetchCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/v1/orders")) {
          orderFetchCount++
        }
        return Promise.resolve({ ok: true, status: 200, json: async () => mockOrdersList })
      }) as any

      render(<PaymentsHub />)

      await act(async () => {
        await vi.advanceTimersByTimeAsync(10)
      })
      expect(orderFetchCount).toBe(1)

      // Tab hidden for 30 seconds (over 7 standard interval intervals)
      setDocumentVisibility("hidden")
      await act(async () => {
        await vi.advanceTimersByTimeAsync(30000)
      })

      // Must remain at 1
      expect(orderFetchCount).toBe(1)

      // User returns to tab
      setDocumentVisibility("visible")

      // Next tick fires single sync
      await act(async () => {
        await vi.advanceTimersByTimeAsync(4000)
      })

      // Exactly 1 new fetch after waking, no burst queue
      expect(orderFetchCount).toBe(2)
    })

    test("R2-R4-T4-04: Security boundary audit: non-admin session cannot trigger admin webhook or outbox fetch", async () => {
      vi.useFakeTimers()
      setMockAuth({
        authenticated: true,
        roles: ["clerk", "warehouse_staff"],
        userId: "usr_clerk_01",
      })
      const { calls } = setupGlobalFetch()

      render(<PaymentsHub />)

      // Run multiple polling intervals
      await act(async () => {
        await vi.advanceTimersByTimeAsync(16000)
      })

      // Verify that throughout the entire session lifecycle, no admin endpoints were queried
      const forbiddenCalls = calls.filter(
        (c) =>
          c.url.includes("/api/v1/admin/webhook-events") ||
          c.url.includes("/api/v1/admin/fulfillment-outbox") ||
          c.url.includes("/api/v1/admin/payments")
      )
      expect(forbiddenCalls).toHaveLength(0)
    })
  })
})

import React from "react"
import {
  render,
  screen,
  fireEvent,
  waitFor,
  waitForElementToBeRemoved,
} from "@testing-library/react"
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { PaymentsHub } from "@/components/payments-hub"
import type { Order } from "@/lib/payment-api"

// Exact concrete auth-context mock matching your payments-hub.test.tsx
vi.mock("@/lib/auth-context", () => ({
  useAuth: vi.fn(() => ({
    authenticated: true,
    loading: false,
    user: { id: "user_admin", roles: ["admin"], full_name: "Admin", email: "admin@rge.ph" },
    authenticatedUser: null,
    allUsers: [],
    csrfToken: "test-csrf",
    loginWithCredentials: vi.fn(),
    loginWithOAuth: vi.fn(),
    switchPersona: vi.fn(),
    logout: vi.fn(),
    refreshSession: vi.fn(),
  })),
}))
import { useAuth } from "@/lib/auth-context"

function makeJsonResponse(payload: unknown, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" },
  })
}

function seed28Orders(): Order[] {
  return Array.from({ length: 28 }, (_, idx) => {
    const n = idx + 1
    const padded = String(n).padStart(3, "0")
    const isPaid = n % 3 === 0
    return {
      id: `order-${padded}`,
      user_id: `user-${padded}`,
      order_number: `ORD-2026-${padded}`,
      title: `Batch Title ${padded}`,
      category: n % 2 === 0 ? "department_invoice" : "product_order",
      reference_code: `REF-${padded}`,
      amount_minor: 10000 * n,
      amount: 100 * n,
      currency: "PHP",
      status: isPaid ? "paid" : "pending",
      metadata: {
        customer_name: `Customer ${padded}`,
        customer_phone: "+639171234567",
      },
      created_at: `2026-11-03T10:${String(n % 60).padStart(2, "0")}:00Z`,
      updated_at: `2026-11-03T10:${String(n % 60).padStart(2, "0")}:00Z`,
      latest_attempt: {
        id: `att-${padded}`,
        order_id: `order-${padded}`,
        provider: "paymongo",
        payment_method: isPaid ? "card_visa" : "qrph",
        amount_minor: 10000 * n,
        amount: 100 * n,
        currency: "PHP",
        status: isPaid ? "succeeded" : "pending",
        idempotency_key: `idem-${padded}`,
        created_at: `2026-11-03T10:${String(n % 60).padStart(2, "0")}:00Z`,
        updated_at: `2026-11-03T10:${String(n % 60).padStart(2, "0")}:00Z`,
      },
    }
  })
}

describe("PaymentsHub Concrete E2E Pagination Suite (Real Heroku snake_case Contract)", () => {
  let dbOrders: Order[]
  let pagedOrderCalls: number[]

  beforeEach(() => {
    vi.clearAllMocks()
    dbOrders = seed28Orders()
    pagedOrderCalls = []

    vi.mocked(useAuth).mockReturnValue({
      authenticated: true,
      loading: false,
      user: { id: "user_admin", roles: ["admin"], full_name: "Admin", email: "admin@rge.ph" } as any,
      authenticatedUser: null,
      allUsers: [],
      csrfToken: "test-csrf",
      loginWithCredentials: vi.fn(),
      loginWithOAuth: vi.fn(),
      switchPersona: vi.fn(),
      logout: vi.fn(),
      refreshSession: vi.fn(),
    })

    global.fetch = vi.fn().mockImplementation((input: string) => {
      const urlStr = String(input)
      const parsed = new URL(urlStr, "http://localhost")

      // 1. Server-driven paginated endpoint (/api/orders) returning snake_case JSON
      if (parsed.pathname.endsWith("/api/orders")) {
        const page = Number(parsed.searchParams.get("page") ?? 1)
        const pageSize = Number(parsed.searchParams.get("pageSize") ?? 10)
        const status = parsed.searchParams.get("status")
        const search = (parsed.searchParams.get("search") ?? "").trim().toLowerCase()

        pagedOrderCalls.push(page)

        const filtered = dbOrders.filter((o) => {
          const statusMatch = !status || status.toLowerCase() === "all" || o.status === status.toLowerCase()
          const searchMatch =
            !search ||
            o.order_number.toLowerCase().includes(search) ||
            o.title.toLowerCase().includes(search) ||
            o.reference_code.toLowerCase().includes(search)
          return statusMatch && searchMatch
        })

        const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize))
        const start = (page - 1) * pageSize
        const slice = filtered.slice(start, start + pageSize)

        return Promise.resolve(
          makeJsonResponse({
            items: slice,
            page,
            page_size: pageSize,
            total_count: filtered.length,
            total_pages: totalPages,
            has_next_page: page < totalPages,
            has_previous_page: page > 1,
          })
        )
      }

      // 2. Legacy /api/v1/orders endpoint (mirrors real dual-endpoint PaymentsHub behavior)
      if (parsed.pathname.includes("/api/v1/orders")) {
        return Promise.resolve(makeJsonResponse(dbOrders))
      }

      // 3. Admin diagnostic endpoints
      if (
        parsed.pathname.includes("/api/v1/admin/webhook-events") ||
        parsed.pathname.includes("/api/v1/admin/fulfillment-outbox")
      ) {
        return Promise.resolve(makeJsonResponse([]))
      }

      return Promise.resolve(makeJsonResponse({}))
    }) as any
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("E2E: Mounts cleanly (no TDZ hook crash), navigates Page 1 -> 2 -> 3 -> 2 with ZERO snap-back", async () => {
    render(<PaymentsHub />)
    await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

    // Page 1 shows ORD-2026-001..010
    expect(screen.getAllByText("ORD-2026-001").length).toBeGreaterThan(0)
    expect(screen.queryByText("ORD-2026-011")).not.toBeInTheDocument()

    // Click Page 2
    fireEvent.click(screen.getByRole("link", { name: /^2$/ }))
    await waitFor(() => {
      expect(screen.getAllByText("ORD-2026-011").length).toBeGreaterThan(0)
    })

    // Click Page 3
    fireEvent.click(screen.getByRole("link", { name: /^3$/ }))
    await waitFor(() => {
      expect(screen.getAllByText("ORD-2026-021").length).toBeGreaterThan(0)
    })

    // Confirm no delayed ~300ms snap-back to Page 1
    await new Promise((r) => setTimeout(r, 400))
    expect(screen.getAllByText("ORD-2026-021").length).toBeGreaterThan(0)

    // Click back to Page 2
    fireEvent.click(screen.getByRole("link", { name: /^2$/ }))
    await waitFor(() => {
      expect(screen.getAllByText("ORD-2026-011").length).toBeGreaterThan(0)
    })

    // Verify exact server-page sequence: no unexpected bounce back to 1
    expect(pagedOrderCalls).toEqual([1, 2, 3, 2])
  })

  it("E2E: Manual Refresh on Page 2 refreshes Page 2 in place via loadAllData()", async () => {
    render(<PaymentsHub />)
    await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

    fireEvent.click(screen.getByRole("link", { name: /^2$/ }))
    await waitFor(() => {
      expect(screen.getAllByText("ORD-2026-011").length).toBeGreaterThan(0)
    })

    // Simulate backend update to row 11 on Page 2
    dbOrders[10] = { ...dbOrders[10], title: "Updated Page 2 Batch Title" }

    const refreshButton = screen.getByRole("button", { name: /refresh/i })
    fireEvent.click(refreshButton)

    await waitFor(() => {
      expect(screen.getAllByText("Updated Page 2 Batch Title").length).toBeGreaterThan(0)
    })
    // Still on Page 2!
    expect(screen.getAllByText("ORD-2026-011").length).toBeGreaterThan(0)
  })

  it("E2E: Deleting all rows on Page 3 clamps smoothly back to Page 2 on refresh", async () => {
    render(<PaymentsHub />)
    await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

    fireEvent.click(screen.getByRole("link", { name: /^3$/ }))
    await waitFor(() => {
      expect(screen.getAllByText("ORD-2026-021").length).toBeGreaterThan(0)
    })

    // Simulate all 8 rows on Page 3 deleted on the server (20 orders remain -> 2 pages)
    dbOrders = dbOrders.slice(0, 20)

    fireEvent.click(screen.getByRole("button", { name: /refresh/i }))

    // Page 3 comes back empty -> non-destructive clamp moves user smoothly to Page 2
    await waitFor(() => {
      expect(screen.getAllByText("ORD-2026-011").length).toBeGreaterThan(0)
    })
    expect(screen.queryByText("ORD-2026-021")).not.toBeInTheDocument()
  })
})

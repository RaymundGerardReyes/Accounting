import React from "react"
import { render, screen, fireEvent, waitFor, waitForElementToBeRemoved } from "@testing-library/react"
import { describe, it, test, expect, vi, beforeEach, afterEach } from "vitest"
import { useRouter } from "next/navigation"
import { PaymentsHub } from "@/components/payments-hub"
import type { Order } from "@/lib/payment-api"

const mockOrders: Order[] = [
  {
    id: "order-101",
    user_id: "user-alpha",
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
    created_at: "2026-11-03T10:00:00Z",
    updated_at: "2026-11-03T10:00:00Z",
    latest_attempt: {
      id: "att-101",
      order_id: "order-101",
      provider: "paymongo",
      payment_method: "qrph",
      amount_minor: 10000,
      amount: 100,
      currency: "PHP",
      status: "pending",
      idempotency_key: "idem-101",
      created_at: "2026-11-03T10:00:00Z",
      updated_at: "2026-11-03T10:00:00Z",
    },
  },
  {
    id: "order-102",
    user_id: "user-beta",
    order_number: "ORD-2026-002",
    title: "Department Settlement CC-001",
    category: "department_invoice",
    reference_code: "REF-CC-001",
    amount_minor: 2500000,
    amount: 25000,
    currency: "PHP",
    status: "paid",
    metadata: {
      customer_name: "Maria Santos",
      customer_phone: "+639189876543",
    },
    created_at: "2026-11-03T11:00:00Z",
    updated_at: "2026-11-03T11:30:00Z",
    latest_attempt: {
      id: "att-102",
      order_id: "order-102",
      provider: "paymongo",
      payment_method: "card_visa",
      amount_minor: 2500000,
      amount: 25000,
      currency: "PHP",
      status: "succeeded",
      idempotency_key: "idem-102",
      created_at: "2026-11-03T11:00:00Z",
      updated_at: "2026-11-03T11:30:00Z",
    },
  },
]

function mockFetchEndpoints(orders: Order[] = mockOrders, failOrders = false) {
  global.fetch = vi.fn().mockImplementation((url: string) => {
    const urlStr = String(url)
    if (urlStr.includes("/api/v1/orders")) {
      if (failOrders) {
        return Promise.reject(new Error("API network failure"))
      }
      return Promise.resolve({
        ok: true,
        status: 200,
        statusText: "OK",
        json: async () => orders,
      })
    }
    if (urlStr.includes("/api/v1/admin/webhook-events") || urlStr.includes("/api/v1/admin/fulfillment-outbox")) {
      return Promise.resolve({
        ok: true,
        status: 200,
        statusText: "OK",
        json: async () => [],
      })
    }
    return Promise.resolve({
      ok: true,
      status: 200,
      statusText: "OK",
      json: async () => ({}),
    })
  }) as any
}

describe("PaymentsHub Component", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // --------------------------------------------------------------------------
  // Suite 1: Initial Loading State
  // --------------------------------------------------------------------------
  describe("Suite 1: Initial Loading State", () => {
    it("displays loading indicator while order data is being fetched", () => {
      // Return unresolved promise to verify loading view
      global.fetch = vi.fn().mockImplementation(() => new Promise(() => {})) as any

      render(<PaymentsHub />)

      expect(screen.getByText("Loading orders...")).toBeInTheDocument()
    })
  })

  // --------------------------------------------------------------------------
  // Suite 2: Successful Order Fetching & Ledger Display
  // --------------------------------------------------------------------------
  describe("Suite 2: Successful Order Fetching & Ledger Display", () => {
    it("renders ledger table rows with order numbers, titles, amounts, badges, and KPI sums", async () => {
      mockFetchEndpoints(mockOrders)

      render(<PaymentsHub />)

      await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

      // Header description
      expect(screen.getByText(/Showing 2 of 2 authoritative ledger orders/i)).toBeInTheDocument()

      // Order Records (matching in table)
      expect(screen.getAllByText("ORD-2026-001").length).toBeGreaterThan(0)
      expect(screen.getAllByText("ORD-2026-002").length).toBeGreaterThan(0)
      expect(screen.getAllByText("Product A Standard Batch").length).toBeGreaterThan(0)
      expect(screen.getAllByText("Department Settlement CC-001").length).toBeGreaterThan(0)
      expect(screen.getAllByText("REF-PROD-A").length).toBeGreaterThan(0)
      expect(screen.getAllByText("REF-CC-001").length).toBeGreaterThan(0)

      // Category labels
      expect(screen.getAllByText("Product Run").length).toBeGreaterThan(0)
      expect(screen.getAllByText("Cost Center").length).toBeGreaterThan(0)

      // Financial status badges
      expect(screen.getAllByText("Pending").length).toBeGreaterThan(0)
      expect(screen.getAllByText("Paid").length).toBeGreaterThan(0)

      // KPI Metric Cards
      expect(screen.getByText("Total Invoices & Orders")).toBeInTheDocument()
      expect(screen.getByText("1 Paid · 1 Pending")).toBeInTheDocument()
      expect(screen.getByText("Settled Volume (PHP)")).toBeInTheDocument()
      expect(screen.getByText("Pending Receivables")).toBeInTheDocument()
    })
  })

  // --------------------------------------------------------------------------
  // Suite 3: Empty State Display
  // --------------------------------------------------------------------------
  describe("Suite 3: Empty State Display", () => {
    it("renders empty state notification when orders endpoint returns empty array", async () => {
      mockFetchEndpoints([])

      render(<PaymentsHub />)

      await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

      expect(
        screen.getByText("No orders match your current filter. Try clearing your search query or status filter.")
      ).toBeInTheDocument()
      expect(screen.getByText(/Showing 0 of 0 authoritative ledger orders/i)).toBeInTheDocument()
    })
  })

  // --------------------------------------------------------------------------
  // Suite 4: Status Filter Interactions
  // --------------------------------------------------------------------------
  describe("Suite 4: Status Filter Interactions", () => {
    it("filters orders by 'all', 'pending', and 'paid' tabs correctly", async () => {
      mockFetchEndpoints(mockOrders)

      render(<PaymentsHub />)

      await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

      // Click "pending" filter button
      const pendingFilter = screen.getByRole("button", { name: /^pending$/i })
      fireEvent.click(pendingFilter)

      expect(screen.getByText(/Showing 1 of 2 authoritative ledger orders/i)).toBeInTheDocument()
      expect(screen.getAllByText("ORD-2026-001").length).toBeGreaterThan(0)
      expect(screen.queryByText("ORD-2026-002")).not.toBeInTheDocument()

      // Click "paid" filter button
      const paidFilter = screen.getByRole("button", { name: /^paid$/i })
      fireEvent.click(paidFilter)

      expect(screen.getByText(/Showing 1 of 2 authoritative ledger orders/i)).toBeInTheDocument()
      expect(screen.getAllByText("ORD-2026-002").length).toBeGreaterThan(0)
      expect(screen.queryByText("ORD-2026-001")).not.toBeInTheDocument()

      // Click "all" filter button
      const allFilter = screen.getByRole("button", { name: /^all$/i })
      fireEvent.click(allFilter)

      expect(screen.getByText(/Showing 2 of 2 authoritative ledger orders/i)).toBeInTheDocument()
      expect(screen.getAllByText("ORD-2026-001").length).toBeGreaterThan(0)
      expect(screen.getAllByText("ORD-2026-002").length).toBeGreaterThan(0)
    })
  })

  // --------------------------------------------------------------------------
  // Suite 5: Search Query Filtering
  // --------------------------------------------------------------------------
  describe("Suite 5: Search Query Filtering", () => {
    it("filters orders by order number, title, or reference code, and displays empty state on no match", async () => {
      mockFetchEndpoints(mockOrders)

      render(<PaymentsHub />)

      await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

      const searchInput = screen.getByPlaceholderText(
        /Search order #, title, or reference/i
      )

      // Search by specific order number
      fireEvent.change(searchInput, { target: { value: "ORD-2026-001" } })
      expect(screen.getByText(/Showing 1 of 2 authoritative ledger orders/i)).toBeInTheDocument()
      expect(screen.getAllByText("ORD-2026-001").length).toBeGreaterThan(0)
      expect(screen.queryByText("ORD-2026-002")).not.toBeInTheDocument()

      // Search by reference code
      fireEvent.change(searchInput, { target: { value: "REF-CC-001" } })
      expect(screen.getByText(/Showing 1 of 2 authoritative ledger orders/i)).toBeInTheDocument()
      expect(screen.getAllByText("ORD-2026-002").length).toBeGreaterThan(0)
      expect(screen.queryByText("ORD-2026-001")).not.toBeInTheDocument()

      // Search with non-matching query -> triggers empty state
      fireEvent.change(searchInput, { target: { value: "NON_EXISTENT_QUERY_XYZ" } })
      expect(screen.getByText(/Showing 0 of 2 authoritative ledger orders/i)).toBeInTheDocument()
      expect(
        screen.getByText("No orders match your current filter. Try clearing your search query or status filter.")
      ).toBeInTheDocument()

      // Clear search
      fireEvent.change(searchInput, { target: { value: "" } })
      expect(screen.getByText(/Showing 2 of 2 authoritative ledger orders/i)).toBeInTheDocument()
      expect(screen.getAllByText("ORD-2026-001").length).toBeGreaterThan(0)
      expect(screen.getAllByText("ORD-2026-002").length).toBeGreaterThan(0)
    })
  })

  // --------------------------------------------------------------------------
  // Suite 6: API Error Resilience
  // --------------------------------------------------------------------------
  describe("Suite 6: API Error Resilience", () => {
    it("gracefully catches fetch errors, clears loading, and displays empty ledger fallback", async () => {
      const consoleErrorSpy = vi.spyOn(console, "error").mockImplementation(() => {})
      mockFetchEndpoints(null as any, true)

      render(<PaymentsHub />)

      await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

      expect(
        screen.getByText("No orders match your current filter. Try clearing your search query or status filter.")
      ).toBeInTheDocument()
      expect(screen.getByText(/Showing 0 of 0 authoritative ledger orders/i)).toBeInTheDocument()

      consoleErrorSpy.mockRestore()
    })
  })

  // --------------------------------------------------------------------------
  // Suite 7: Action Navigation
  // --------------------------------------------------------------------------
  describe("Suite 7: Action Navigation", () => {
    it("invokes router.push with checkout URL and order ID when Pay Now is clicked", async () => {
      mockFetchEndpoints(mockOrders)
      const router = useRouter()

      render(<PaymentsHub />)

      await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

      // Find "Pay Now" button on pending order
      const payButtons = screen.getAllByRole("button", { name: /Pay Now/i })
      expect(payButtons.length).toBeGreaterThan(0)

      fireEvent.click(payButtons[0])

      expect(router.push).toHaveBeenCalledWith("/checkout?order_id=order-101")
    })
  })

  // --------------------------------------------------------------------------
  // Suite 8: Polling Interval Cleanup on Unmount
  // --------------------------------------------------------------------------
  describe("Suite 8: Polling Interval Cleanup", () => {
    it("clears polling interval timer when component unmounts", async () => {
      mockFetchEndpoints(mockOrders)
      const clearIntervalSpy = vi.spyOn(global, "clearInterval")

      const { unmount } = render(<PaymentsHub />)

      await waitForElementToBeRemoved(() => screen.queryByText("Loading orders..."))

      unmount()

      expect(clearIntervalSpy).toHaveBeenCalled()
      clearIntervalSpy.mockRestore()
    })
  })
})

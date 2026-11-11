import React from "react"
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { render, screen, waitFor, act } from "@testing-library/react"
import { pollPaymentStatus } from "@/lib/payment-api"
import PaymentReturnPage from "@/app/payment/return/page"
import PaymentResultPage from "@/app/payment/result/[paymentId]/page"

const mockReplace = vi.fn()
const mockPush = vi.fn()
let mockSearchParamsGet = vi.fn()

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    replace: mockReplace,
    push: mockPush,
  }),
  useSearchParams: () => ({
    get: (key: string) => mockSearchParamsGet(key),
  }),
}))

vi.mock("@/hooks/usePaymentSignalR", () => ({
  usePaymentSignalR: () => ({
    isConnected: true,
  }),
}))

describe("Payment Real-Time Synchronization & Result Resilience", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockSearchParamsGet.mockReturnValue(null)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe("pollPaymentStatus API client", () => {
    it("transmits client_reported_redirect_status query parameter when provided", async () => {
      let requestedUrl = ""
      global.fetch = vi.fn().mockImplementation((url: string) => {
        requestedUrl = String(url)
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_test_001",
              status: "processing",
              amount_minor: 50000,
            }),
        })
      })

      const result = await pollPaymentStatus("pay_test_001", true, "cancelled")
      expect(requestedUrl).toContain("/api/v1/payments/pay_test_001?auto_reconcile=true")
      expect(requestedUrl).toContain("&client_reported_redirect_status=cancelled")
      expect(result.id).toBe("pay_test_001")
    })

    it("omits client_reported_redirect_status when not supplied", async () => {
      let requestedUrl = ""
      global.fetch = vi.fn().mockImplementation((url: string) => {
        requestedUrl = String(url)
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_test_002",
              status: "pending",
              amount_minor: 25000,
            }),
        })
      })

      const result = await pollPaymentStatus("pay_test_002", false)
      expect(requestedUrl).toContain("/api/v1/payments/pay_test_002?auto_reconcile=false")
      expect(requestedUrl).not.toContain("client_reported_redirect_status")
      expect(result.status).toBe("pending")
    })
  })

  describe("PaymentReturnPage redirect forwarding", () => {
    it("preserves status query parameter and forwards to result URL", async () => {
      mockSearchParamsGet.mockImplementation((key: string) => {
        if (key === "payment_id" || key === "paymentId") return "pay_ret_888"
        if (key === "status" || key === "redirect_status") return "cancelled"
        return null
      })

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_ret_888?status=cancelled")
      })
    })

    it("preserves redirect_status parameter if status is absent", async () => {
      mockSearchParamsGet.mockImplementation((key: string) => {
        if (key === "payment_id" || key === "paymentId") return "pay_ret_999"
        if (key === "redirect_status") return "failed"
        return null
      })

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_ret_999?status=failed")
      })
    })

    it("redirects without status parameter when no redirect status is present", async () => {
      mockSearchParamsGet.mockImplementation((key: string) => {
        if (key === "payment_id" || key === "paymentId") return "pay_ret_clean"
        return null
      })

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_ret_clean")
      })
    })
  })

  describe("PaymentResultPage state locking & focus synchronization", () => {
    it("locks Succeeded payment status against stale poll downgrades", async () => {
      let pollCallCount = 0

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCallCount++

        // First call: returns authoritative Succeeded status
        if (pollCallCount === 1) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_lock_123",
                status: "succeeded",
                amount_minor: 100000,
                paid_at: "2026-11-10T12:00:00Z",
                order: {
                  order_number: "ORD-LOCK-001",
                  title: "Test Order",
                },
              }),
          })
        }

        // Subsequent poll calls: simulate late stale response returning pending
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_lock_123",
              status: "pending",
              amount_minor: 100000,
              order: {
                order_number: "ORD-LOCK-001",
                title: "Test Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_lock_123" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      // In Succeeded state, Payment Confirmed badge must render
      await waitFor(() => {
        expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      })

      // Ensure it NEVER downgraded to "Awaiting Settlement" or non-succeeded card
      expect(screen.queryByText(/Awaiting Payment Settlement/i)).toBeNull()
    })

    it("attaches focus listener and performs debounced status reconciliation", async () => {
      let fetchUrls: string[] = []

      global.fetch = vi.fn().mockImplementation((url: string) => {
        fetchUrls.push(String(url))
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_focus_sync",
              status: "processing",
              amount_minor: 50000,
              order: {
                order_number: "ORD-FOCUS-001",
                title: "Focus Test",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_focus_sync" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(fetchUrls.length).toBeGreaterThanOrEqual(1)
      })

      const countBeforeFocus = fetchUrls.length

      // Simulate tab switch back: window focus event
      await act(async () => {
        // Fast-forward Date.now to pass 3000ms debounce
        const originalDateNow = Date.now
        Date.now = () => originalDateNow() + 4000
        window.dispatchEvent(new Event("focus"))
        Date.now = originalDateNow
      })

      await waitFor(() => {
        expect(fetchUrls.length).toBeGreaterThan(countBeforeFocus)
      })

      // The focus reconciliation should request auto_reconcile=true
      const lastCall = fetchUrls[fetchUrls.length - 1]
      expect(lastCall).toContain("auto_reconcile=true")
    })
  })
})

import React from "react"
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { render, screen, waitFor, act } from "@testing-library/react"
import PaymentReturnPage from "@/app/payment/return/page"
import PaymentResultPage from "@/app/payment/result/[paymentId]/page"
import { pollPaymentStatus } from "@/lib/payment-api"

const mockReplace = vi.fn()
const mockPush = vi.fn()
let currentSearchParams: Record<string, string> = {}
let signalROnStatusChanged: ((payload: any) => void) | null = null

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    replace: mockReplace,
    push: mockPush,
  }),
  useSearchParams: () => ({
    get: (key: string) => currentSearchParams[key] ?? null,
  }),
}))

vi.mock("@/hooks/usePaymentSignalR", () => ({
  usePaymentSignalR: (options: any) => {
    signalROnStatusChanged = options?.onStatusChanged ?? null
    return {
      isConnected: true,
      connectionError: null,
    }
  },
}))

describe("Challenger M3 R2: Deep Empirical Challenge & Stress Harness", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    currentSearchParams = {}
    signalROnStatusChanged = null
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // =========================================================================
  // Challenge 1: Focus & Visibility Debounce Throttling
  // =========================================================================
  describe("Challenge 1: Focus & Visibility Debounce Throttling", () => {
    it("throttles bursts of 10 interleaved focus and visibilitychange events within 500ms to exactly 1 network poll per 3,000ms window", async () => {
      const pollCalls: { url: string; time: number }[] = []
      let mockTime = 1000000

      const originalDateNow = Date.now
      Date.now = vi.fn(() => mockTime)

      Object.defineProperty(document, "visibilityState", {
        value: "visible",
        configurable: true,
      })

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCalls.push({ url: String(url), time: mockTime })
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_throttle_001",
              status: "processing",
              amount_minor: 99000,
              order: {
                order_number: "ORD-THROTTLE-001",
                title: "Throttling Test Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_throttle_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      // Wait for mount initialization
      await waitFor(() => {
        expect(pollCalls.length).toBeGreaterThanOrEqual(1)
      })

      const mountCallsCount = pollCalls.length

      // Advance clock by 4000ms past initial mount
      mockTime += 4000

      // Fire a rapid burst of 10 interleaved focus and visibilitychange events within 500ms (50ms intervals)
      for (let i = 0; i < 10; i++) {
        mockTime += 50
        await act(async () => {
          if (i % 2 === 0) {
            window.dispatchEvent(new Event("focus"))
          } else {
            document.dispatchEvent(new Event("visibilitychange"))
          }
        })
      }

      // Assert that exactly 1 network poll was executed for the burst
      const burstCalls = pollCalls.slice(mountCallsCount)
      expect(burstCalls.length).toBe(1)
      expect(burstCalls[0].url).toContain("auto_reconcile=true")

      // Fire another event 1000ms later (total 1550ms since first poll, which is < 3000ms)
      mockTime += 1000
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })
      // Should still be throttled to 1
      expect(pollCalls.slice(mountCallsCount).length).toBe(1)

      // Advance past the 3000ms window (e.g. +2100ms, total > 3000ms)
      mockTime += 2100
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"))
      })

      // Now exactly 1 additional poll should execute
      expect(pollCalls.slice(mountCallsCount).length).toBe(2)

      Date.now = originalDateNow
    })

    it("prevents overlapping polls if an in-flight poll takes longer than 3,000ms", async () => {
      let resolveLongPoll: ((value: any) => void) | null = null
      let pollCallCount = 0
      let mockTime = 2000000

      const originalDateNow = Date.now
      Date.now = vi.fn(() => mockTime)

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCallCount++
        // Mount calls resolve immediately
        if (pollCallCount <= 2) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_inflight_001",
                status: "processing",
                amount_minor: 50000,
                order: { order_number: "ORD-001" },
              }),
          })
        }

        // Focus poll hangs until explicitly resolved
        return new Promise((resolve) => {
          resolveLongPoll = resolve
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_inflight_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(pollCallCount).toBeGreaterThanOrEqual(1)
      })

      const mountCount = pollCallCount
      mockTime += 4000

      // Trigger first focus event -> initiates long poll
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })

      expect(pollCallCount).toBe(mountCount + 1)

      // Advance time by 3500ms (> 3000ms debounce), but long poll is STILL in flight
      mockTime += 3500
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })

      // In-flight guard must prevent a new poll
      expect(pollCallCount).toBe(mountCount + 1)

      // Resolve the long poll
      await act(async () => {
        resolveLongPoll!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_inflight_001",
              status: "processing",
              amount_minor: 50000,
            }),
        })
      })

      // Now that in-flight is cleared and >3000ms have passed, next focus triggers a poll
      mockTime += 100
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })

      expect(pollCallCount).toBe(mountCount + 2)

      Date.now = originalDateNow
    })

    it("does not attach or trigger focus sync when status is already terminal", async () => {
      let pollCount = 0
      global.fetch = vi.fn().mockImplementation(() => {
        pollCount++
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_terminal_noop",
              status: "succeeded",
              amount_minor: 50000,
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_terminal_noop" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(pollCount).toBeGreaterThanOrEqual(1)
      })

      const mountCount = pollCount

      // Send focus events on terminal payment
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
        document.dispatchEvent(new Event("visibilitychange"))
      })

      // Zero additional polls should be triggered
      expect(pollCount).toBe(mountCount)
    })
  })

  // =========================================================================
  // Challenge 2: Return URL Query Preservation & Backend Forwarding
  // =========================================================================
  describe("Challenge 2: Return URL Query Preservation & Forwarding", () => {
    it("preserves ?status=failed on return page and forwards to result page and backend poll endpoint", async () => {
      // 1. Verify return page redirect
      currentSearchParams = {
        payment_id: "pay_fwd_failed",
        status: "failed",
      }
      render(<PaymentReturnPage />)
      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_fwd_failed?status=failed")
      })

      // 2. Verify result page forwards client_reported_redirect_status to backend poll endpoint
      currentSearchParams = { status: "failed" }
      const capturedUrls: string[] = []
      let pollCallCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCallCount++
        capturedUrls.push(String(url))

        // Initial local-fast path returns non-terminal processing state
        if (pollCallCount === 1) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_fwd_failed",
                status: "processing",
                amount_minor: 120000,
                order: { order_number: "ORD-FAIL" },
              }),
          })
        }

        // Remote gateway reconciliation resolves to failed using the client reported status
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_fwd_failed",
              status: "failed",
              failure_code: "source_failed",
              failure_message: "Customer failed transaction",
              amount_minor: 120000,
              order: { order_number: "ORD-FAIL" },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_fwd_failed" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(capturedUrls.length).toBeGreaterThanOrEqual(2)
      })

      // Check that background reconciliation poll contained client_reported_redirect_status=failed
      const reconCall = capturedUrls.find((u) => u.includes("client_reported_redirect_status=failed"))
      expect(reconCall).toBeDefined()
      expect(reconCall).toContain("auto_reconcile=true")
    })

    it("preserves ?redirect_status=cancelled on return page and forwards to result page and backend poll endpoint", async () => {
      currentSearchParams = {
        payment_id: "pay_fwd_cancel",
        redirect_status: "cancelled",
      }
      render(<PaymentReturnPage />)
      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_fwd_cancel?status=cancelled")
      })

      // Result page inspects searchParams (supporting status, redirect_status, and state)
      currentSearchParams = { redirect_status: "cancelled" }
      const capturedUrls: string[] = []
      let pollCallCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCallCount++
        capturedUrls.push(String(url))

        if (pollCallCount === 1) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_fwd_cancel",
                status: "requires_action",
                amount_minor: 85000,
                order: { order_number: "ORD-CANCEL" },
              }),
          })
        }

        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_fwd_cancel",
              status: "cancelled",
              failure_code: "source_cancelled",
              failure_message: "User cancelled on checkout page",
              amount_minor: 85000,
              order: { order_number: "ORD-CANCEL" },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_fwd_cancel" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(capturedUrls.length).toBeGreaterThanOrEqual(2)
      })

      const reconCall = capturedUrls.find((u) => u.includes("client_reported_redirect_status=cancelled"))
      expect(reconCall).toBeDefined()
      expect(reconCall).toContain("auto_reconcile=true")
    })

    it("preserves ?state=expired on return page and forwards to result page and backend poll endpoint", async () => {
      currentSearchParams = {
        payment_id: "pay_fwd_expired",
        state: "expired",
      }
      render(<PaymentReturnPage />)
      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_fwd_expired?status=expired")
      })

      // Result page inspects searchParams
      currentSearchParams = { state: "expired" }
      const capturedUrls: string[] = []
      let pollCallCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCallCount++
        capturedUrls.push(String(url))

        if (pollCallCount === 1) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_fwd_expired",
                status: "processing",
                amount_minor: 45000,
                order: { order_number: "ORD-EXPIRED" },
              }),
          })
        }

        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_fwd_expired",
              status: "expired",
              failure_code: "source_expired",
              failure_message: "Checkout session expired",
              amount_minor: 45000,
              order: { order_number: "ORD-EXPIRED" },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_fwd_expired" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(capturedUrls.length).toBeGreaterThanOrEqual(2)
      })

      const reconCall = capturedUrls.find((u) => u.includes("client_reported_redirect_status=expired"))
      expect(reconCall).toBeDefined()
      expect(reconCall).toContain("auto_reconcile=true")
    })

    it("pollPaymentStatus properly encodes query parameters and handles null redirect status", async () => {
      let urlWithStatus = ""
      let urlWithoutStatus = ""

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const u = String(url)
        if (u.includes("client_reported_redirect_status")) {
          urlWithStatus = u
        } else {
          urlWithoutStatus = u
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({ id: "pay_enc", status: "pending" }),
        })
      })

      await pollPaymentStatus("pay_enc", true, "failed & invalid")
      expect(urlWithStatus).toContain("client_reported_redirect_status=failed%20%26%20invalid")

      await pollPaymentStatus("pay_enc", false, null)
      expect(urlWithoutStatus).not.toContain("client_reported_redirect_status")
    })
  })

  // =========================================================================
  // Challenge 3: Result Page safeSetPayment Locking
  // =========================================================================
  describe("Challenge 3: Result Page safeSetPayment Terminal Locking", () => {
    it("locks 'succeeded' status against delayed poll responses returning 'pending', 'failed', 'processing', 'cancelled', or 'expired'", async () => {
      let latePollResolve: ((value: any) => void) | null = null
      let pollCount = 0

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCount++
        const urlStr = String(url)

        if (urlStr.includes("/receipt")) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                official_receipt_number: "OR-LOCK-9999",
                acknowledgement_number: "ACK-LOCK-9999",
                amount_php: "1,500.00",
                currency: "PHP",
                category_label: "Subscription",
                order_number: "ORD-LOCK-9999",
                paid_at_manila: "2026-11-11 12:00:00",
                payment_method_code: "card",
                payment_method_label: "Mastercard",
                provider_payment_id: "pay_prov_9999",
                vatable_sales_php: "1,339.29",
                vat_amount_php: "160.71",
              }),
          })
        }

        // Mount fast-path returns succeeded
        if (pollCount === 1) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_terminal_lock_001",
                status: "succeeded",
                amount_minor: 150000,
                order: {
                  order_number: "ORD-LOCK-9999",
                  title: "Annual Subscription",
                },
              }),
          })
        }

        // Any delayed poll hangs
        return new Promise((resolve) => {
          latePollResolve = resolve
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_terminal_lock_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      // Verify UI renders Payment Confirmed
      await waitFor(() => {
        expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      })

      // Attempt attack 1: Late poll returns 'pending'
      await act(async () => {
        if (latePollResolve) {
          latePollResolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_terminal_lock_001",
                status: "pending",
                amount_minor: 150000,
              }),
          })
        }
      })

      // UI MUST remain in Payment Confirmed state
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      expect(screen.queryByText(/Awaiting Payment Settlement/i)).toBeNull()

      // Attempt attack 2: Late poll returns 'failed'
      await act(async () => {
        if (latePollResolve) {
          latePollResolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_terminal_lock_001",
                status: "failed",
                failure_code: "PAYMENT_FAILED",
                failure_message: "Declined by card issuer",
                amount_minor: 150000,
              }),
          })
        }
      })

      // UI MUST remain in Payment Confirmed state
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      expect(screen.queryByText(/Payment Failed/i)).toBeNull()

      // Attempt attack 3: SignalR push sends 'failed'
      await act(async () => {
        if (signalROnStatusChanged) {
          signalROnStatusChanged({
            transactionId: "pay_terminal_lock_001",
            status: "failed",
          })
        }
      })

      // UI MUST still remain in Payment Confirmed state
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      expect(screen.queryByText(/Payment Failed/i)).toBeNull()

      // Attempt attack 4: SignalR push sends 'pending'
      await act(async () => {
        if (signalROnStatusChanged) {
          signalROnStatusChanged({
            transactionId: "pay_terminal_lock_001",
            status: "pending",
          })
        }
      })

      // UI MUST still remain in Payment Confirmed state
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      expect(screen.queryByText(/Awaiting Payment Settlement/i)).toBeNull()
    })

    it("permits safeSetPayment state enrichment while strictly preserving 'succeeded' terminal state", async () => {
      let pollCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCount++
        const urlStr = String(url)

        if (urlStr.includes("/receipt")) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                official_receipt_number: "OR-ENRICH-001",
                acknowledgement_number: "ACK-ENRICH-001",
                amount_php: "3,000.00",
                currency: "PHP",
                category_label: "Consulting",
                order_number: "ORD-ENRICH-001",
                paid_at_manila: "2026-11-11 15:00:00",
                payment_method_code: "qrph",
                payment_method_label: "QR Ph",
                provider_payment_id: "pay_prov_enrich",
                vatable_sales_php: "2,678.57",
                vat_amount_php: "321.43",
              }),
          })
        }

        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_enrich_001",
              status: "succeeded",
              amount_minor: 300000,
              order: { order_number: "ORD-ENRICH-001" },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_enrich_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      })

      // When receipt loads, receipt details (OR-ENRICH-001) must be rendered successfully
      await waitFor(() => {
        expect(screen.getByText("OR-ENRICH-001")).toBeDefined()
      })
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
    })
  })
})

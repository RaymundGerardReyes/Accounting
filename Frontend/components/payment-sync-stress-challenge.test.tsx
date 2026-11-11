import React from "react"
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { render, screen, waitFor, act, renderHook } from "@testing-library/react"
import PaymentReturnPage from "@/app/payment/return/page"
import PaymentResultPage from "@/app/payment/result/[paymentId]/page"
import { usePayMongoRealtimeSync } from "@/hooks/use-paymongo-realtime-sync"

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

vi.mock("@microsoft/signalr", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@microsoft/signalr")>()
  class MockHubConnectionBuilder {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return {
        start: vi.fn().mockImplementation(() => Promise.resolve()),
        stop: vi.fn().mockImplementation(() => Promise.resolve()),
        invoke: vi.fn().mockImplementation(() => Promise.resolve()),
        on: vi.fn(),
        off: vi.fn(),
        state: "Disconnected",
      }
    }
  }
  return {
    ...actual,
    HubConnectionBuilder: MockHubConnectionBuilder,
  }
})

describe("Empirical Challenge Suite: M3 Frontend Real-Time Sync & Resilience", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    currentSearchParams = {}
    signalROnStatusChanged = null
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // ---------------------------------------------------------------------------
  // Challenge 1: Simulated Rapid Tab Focus Switching (10 focus events in 500ms)
  // ---------------------------------------------------------------------------
  describe("Challenge 1: Rapid Tab Focus Switching Debounce", () => {
    it("suppresses 10 rapid window focus events within 500ms, executing exactly 1 network poll", async () => {
      const pollCalls: { url: string; time: number }[] = []
      let mockTime = 1000000

      const originalDateNow = Date.now
      Date.now = vi.fn(() => mockTime)

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCalls.push({ url: String(url), time: mockTime })
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_burst_001",
              status: "processing",
              amount_minor: 75000,
              order: {
                order_number: "ORD-BURST-001",
                title: "Rapid Tab Switching Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_burst_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      // Wait for initial mount reconciliation to settle
      await waitFor(() => {
        expect(pollCalls.length).toBeGreaterThanOrEqual(1)
      })

      const mountCallsCount = pollCalls.length

      // Advance clock by 4000ms from initial mount to ensure we are outside any mount debounce
      mockTime += 4000

      // Dispatch 10 rapid focus events over 500ms (every 50ms)
      for (let i = 0; i < 10; i++) {
        mockTime += 50
        await act(async () => {
          window.dispatchEvent(new Event("focus"))
        })
      }

      // Verify that after 10 focus events within 500ms, exactly 1 additional poll executed
      const focusCalls = pollCalls.slice(mountCallsCount)
      expect(focusCalls.length).toBe(1)
      expect(focusCalls[0].url).toContain("auto_reconcile=true")

      // Advance clock past the 3000ms debounce threshold (e.g. +3100ms)
      mockTime += 3100
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })

      // Now exactly 1 more call should be permitted
      expect(pollCalls.slice(mountCallsCount).length).toBe(2)

      Date.now = originalDateNow
    })

    it("suppresses rapid document visibilitychange events within 500ms, executing exactly 1 network poll", async () => {
      const pollCalls: { url: string; time: number }[] = []
      let mockTime = 2000000

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
              id: "pay_vis_burst_001",
              status: "processing",
              amount_minor: 50000,
              order: {
                order_number: "ORD-VIS-001",
                title: "Visibility Burst Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_vis_burst_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(pollCalls.length).toBeGreaterThanOrEqual(1)
      })

      const mountCallsCount = pollCalls.length
      mockTime += 4000

      // Dispatch 10 rapid visibilitychange events over 500ms
      for (let i = 0; i < 10; i++) {
        mockTime += 50
        await act(async () => {
          document.dispatchEvent(new Event("visibilitychange"))
        })
      }

      const visibilityCalls = pollCalls.slice(mountCallsCount)
      expect(visibilityCalls.length).toBe(1)

      Date.now = originalDateNow
    })
  })

  // ---------------------------------------------------------------------------
  // Challenge 2: Delayed Stale Poll Response Arriving AFTER Succeeded Status
  // ---------------------------------------------------------------------------
  describe("Challenge 2: Terminal Succeeded State Immutability", () => {
    it("locks Succeeded status in PaymentResultPage when a delayed stale poll arrives after SignalR Succeeded transition", async () => {
      let resolveDelayedPoll: ((value: any) => void) | null = null
      const delayedPollPromise = new Promise((resolve) => {
        resolveDelayedPoll = resolve
      })

      let pollCallIndex = 0

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCallIndex++
        const urlStr = String(url)

        // Receipt endpoint mock
        if (urlStr.includes("/receipt")) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                official_receipt_number: "OR-2026-CHALL-001",
                gross_amount_centavos: 100000,
              }),
          })
        }

        // Fast-path local fetch on mount returns processing
        if (pollCallIndex === 1) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_race_001",
                status: "processing",
                amount_minor: 100000,
                order: {
                  order_number: "ORD-RACE-001",
                  title: "Race Test Order",
                },
              }),
          })
        }

        // Background reconciliation poll is delayed in-flight
        if (pollCallIndex === 2) {
          return delayedPollPromise
        }

        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_race_001",
              status: "processing",
              amount_minor: 100000,
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_race_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      // Initial state is processing ("Awaiting Payment Settlement")
      await waitFor(() => {
        expect(screen.getByText(/Awaiting Payment Settlement/i)).toBeDefined()
      })

      // Real-time event: SignalR pushes Succeeded status while background poll is still pending
      await act(async () => {
        if (signalROnStatusChanged) {
          signalROnStatusChanged({
            transactionId: "pay_race_001",
            status: "succeeded",
          })
        }
      })

      // UI updates to Payment Confirmed
      await waitFor(() => {
        expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      })

      // Delayed background reconciliation poll completes LATE with stale processing/pending status
      await act(async () => {
        resolveDelayedPoll!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_race_001",
              status: "pending", // STALE non-succeeded data
              amount_minor: 100000,
              order: {
                order_number: "ORD-RACE-001",
                title: "Race Test Order",
              },
            }),
        })
      })

      // VERIFICATION: Succeeded status is strictly locked and NEVER overwritten by stale pending response
      await waitFor(() => {
        expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      })
      expect(screen.queryByText(/Awaiting Payment Settlement/i)).toBeNull()
    })

    it("locks Succeeded status in usePayMongoRealtimeSync when a stale non-succeeded poll response arrives", async () => {
      let resolvePendingPoll: ((value: any) => void) | null = null
      let pollCount = 0

      global.fetch = vi.fn().mockImplementation(() => {
        pollCount++
        if (pollCount === 1) {
          // In-flight poll started while in requiresaction
          return new Promise((resolve) => {
            resolvePendingPoll = resolve
          })
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_hook_race",
              status: "succeeded",
              is_payable: false,
            }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus }) =>
          usePayMongoRealtimeSync({
            attemptId: "att_hook_race",
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: { initialStatus: "requiresaction" },
        }
      )

      expect(result.current.status).toBe("requiresaction")

      // Parent re-renders or external event updates status to succeeded
      await act(async () => {
        rerender({ initialStatus: "succeeded" })
      })
      expect(result.current.status).toBe("succeeded")

      // Late in-flight poll resolves with stale "requiresaction" status
      await act(async () => {
        resolvePendingPoll!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_hook_race",
              status: "requiresaction", // Stale data
              is_payable: true,
            }),
        })
      })

      // Status must remain succeeded
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
    })
  })

  // ---------------------------------------------------------------------------
  // Challenge 3: Redirect from return page with various query parameters
  // ---------------------------------------------------------------------------
  describe("Challenge 3: Return Page Query Parameter Preservation & Forwarding", () => {
    it("preserves ?status=failed and forwards to /payment/result/{id}?status=failed", async () => {
      currentSearchParams = {
        payment_id: "pay_param_001",
        status: "failed",
      }

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_param_001?status=failed")
      })
    })

    it("preserves ?redirect_status=cancelled and forwards to /payment/result/{id}?status=cancelled", async () => {
      currentSearchParams = {
        payment_id: "pay_param_002",
        redirect_status: "cancelled",
      }

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_param_002?status=cancelled")
      })
    })

    it("preserves ?state=expired and forwards to /payment/result/{id}?status=expired", async () => {
      currentSearchParams = {
        payment_id: "pay_param_003",
        state: "expired",
      }

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_param_003?status=expired")
      })
    })

    it("resolves order_id, extracts latest attempt, and forwards preserved redirect status", async () => {
      currentSearchParams = {
        order_id: "ord_lookup_001",
        redirect_status: "failed",
      }

      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/v1/orders/ord_lookup_001")) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "ord_lookup_001",
                attempts: [{ id: "att_resolved_999", status: "failed" }],
              }),
          })
        }
        return Promise.reject(new Error("Not found"))
      })

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/att_resolved_999?status=failed")
      })
    })

    it("PaymentResultPage receives forwarded status and passes client_reported_redirect_status in reconciliation", async () => {
      currentSearchParams = {
        status: "failed",
      }

      let pollCallCount = 0
      const capturedUrls: string[] = []
      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCallCount++
        capturedUrls.push(String(url))

        // Local fast-path returns non-terminal processing attempt
        if (pollCallCount === 1) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_fwd_001",
                status: "processing",
                amount_minor: 50000,
                order: {
                  order_number: "ORD-FWD-001",
                  title: "Forwarded Status Order",
                },
              }),
          })
        }

        // Remote gateway reconciliation resolves to failed using the client reported status
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_fwd_001",
              status: "failed",
              failure_code: "source_failed",
              failure_message: "Customer cancelled payment on checkout page",
              amount_minor: 50000,
              order: {
                order_number: "ORD-FWD-001",
                title: "Forwarded Status Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_fwd_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(capturedUrls.length).toBeGreaterThanOrEqual(2)
      })

      // Verify that background reconciliation poll includes client_reported_redirect_status=failed
      const reconcileCall = capturedUrls.find((u) => u.includes("auto_reconcile=true"))
      expect(reconcileCall).toBeDefined()
      expect(reconcileCall).toContain("client_reported_redirect_status=failed")
    })
  })
})

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

type SignalRHandler = (...args: any[]) => void
const hookSignalRHandlers = new Map<string, SignalRHandler>()

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
        on: vi.fn().mockImplementation((event: string, handler: SignalRHandler) => {
          hookSignalRHandlers.set(event, handler)
        }),
        off: vi.fn().mockImplementation((event: string) => {
          hookSignalRHandlers.delete(event)
        }),
        state: "Connected",
      }
    }
  }
  return {
    ...actual,
    HubConnectionBuilder: MockHubConnectionBuilder,
  }
})

describe("Milestone 4 Challenger 2: Adversarial Quality Gate Test Suite", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    currentSearchParams = {}
    signalROnStatusChanged = null
    hookSignalRHandlers.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // =========================================================================
  // Mandate 1: Rapid Tab Focus Switching & Network Polling Throttling
  // =========================================================================
  describe("1. Rapid Tab Focus Switching & 3000ms Throttle Window", () => {
    it("throttles 15 rapid interleaved focus and visibilitychange events within 450ms to exactly 1 network poll", async () => {
      const pollCalls: { url: string; time: number }[] = []
      let mockTime = 5000000

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
              id: "pay_burst_m4_001",
              status: "processing",
              amount_minor: 125000,
              order: {
                order_number: "ORD-BURST-M4-001",
                title: "Adversarial Burst Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_burst_m4_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      // Wait for initial mount to settle
      await waitFor(() => {
        expect(pollCalls.length).toBeGreaterThanOrEqual(1)
      })

      const mountCallsCount = pollCalls.length

      // Advance clock past mount debounce
      mockTime += 4000

      // Fire 15 rapid interleaved events across 450ms (every 30ms)
      for (let i = 0; i < 15; i++) {
        mockTime += 30
        await act(async () => {
          if (i % 2 === 0) {
            window.dispatchEvent(new Event("focus"))
          } else {
            document.dispatchEvent(new Event("visibilitychange"))
          }
        })
      }

      // Assert that within this 450ms burst, exactly 1 additional poll executed
      const burstCalls = pollCalls.slice(mountCallsCount)
      expect(burstCalls.length).toBe(1)
      expect(burstCalls[0].url).toContain("auto_reconcile=true")

      Date.now = originalDateNow
    })

    it("verifies exact 3000ms boundary condition: event at 2999ms is suppressed, event at 3001ms executes", async () => {
      const pollCalls: { url: string; time: number }[] = []
      let mockTime = 6000000

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
              id: "pay_boundary_001",
              status: "processing",
              amount_minor: 60000,
              order: {
                order_number: "ORD-BOUND-001",
                title: "Boundary Test Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_boundary_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(pollCalls.length).toBeGreaterThanOrEqual(1)
      })
      const mountCallsCount = pollCalls.length

      // Advance 5000ms, then trigger first focus poll
      mockTime += 5000
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })
      expect(pollCalls.slice(mountCallsCount).length).toBe(1)
      const firstFocusTime = mockTime

      // Trigger at exactly 2,999ms after first focus poll (must be suppressed)
      mockTime = firstFocusTime + 2999
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })
      expect(pollCalls.slice(mountCallsCount).length).toBe(1) // Still 1!

      // Trigger at 3,001ms after first focus poll (must be accepted)
      mockTime = firstFocusTime + 3001
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
      })
      expect(pollCalls.slice(mountCallsCount).length).toBe(2) // Incremented to 2!

      Date.now = originalDateNow
    })

    it("ignores visibilitychange events when document.visibilityState is 'hidden'", async () => {
      const pollCalls: string[] = []
      let mockTime = 7000000
      const originalDateNow = Date.now
      Date.now = vi.fn(() => mockTime)

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCalls.push(String(url))
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_hidden_001",
              status: "processing",
              amount_minor: 45000,
              order: {
                order_number: "ORD-HIDDEN-001",
                title: "Hidden Visibility Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_hidden_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(pollCalls.length).toBeGreaterThanOrEqual(1)
      })
      const mountCallsCount = pollCalls.length

      // Advance past debounce
      mockTime += 5000

      // Tab becomes hidden
      Object.defineProperty(document, "visibilityState", {
        value: "hidden",
        configurable: true,
      })

      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"))
      })

      // No new poll should have fired
      expect(pollCalls.slice(mountCallsCount).length).toBe(0)

      Date.now = originalDateNow
    })

    it("prevents overlapping duplicate polls when a focus poll is already in-flight", async () => {
      let resolveInFlightPoll: ((value: any) => void) | null = null
      const inFlightPollPromise = new Promise((resolve) => {
        resolveInFlightPoll = resolve
      })

      let pollCallCount = 0
      let mockTime = 8000000
      const originalDateNow = Date.now
      Date.now = vi.fn(() => mockTime)

      Object.defineProperty(document, "visibilityState", {
        value: "visible",
        configurable: true,
      })

      global.fetch = vi.fn().mockImplementation(() => {
        pollCallCount++
        if (pollCallCount === 1) {
          // Fast-path mount poll resolves immediately
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_inflight_001",
                status: "processing",
                amount_minor: 50000,
                order: {
                  order_number: "ORD-INFLIGHT-001",
                  title: "Inflight Test Order",
                },
              }),
          })
        }
        // Focus sync poll is held in-flight
        return inFlightPollPromise
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_inflight_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(pollCallCount).toBeGreaterThanOrEqual(1)
      })

      // Advance clock past 3000ms
      mockTime += 5000

      // Trigger focus poll 1 (enters in-flight state)
      act(() => {
        window.dispatchEvent(new Event("focus"))
      })

      const callsAfterFirstFocus = pollCallCount

      // Even if clock advances another 3500ms, while first focus poll is in-flight,
      // second focus event must not create duplicate request
      mockTime += 3500
      act(() => {
        window.dispatchEvent(new Event("focus"))
      })
      expect(pollCallCount).toBe(callsAfterFirstFocus)

      // Resolve the in-flight poll
      await act(async () => {
        resolveInFlightPoll!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_inflight_001",
              status: "processing",
              amount_minor: 50000,
              order: {
                order_number: "ORD-INFLIGHT-001",
                title: "Inflight Test Order",
              },
            }),
        })
      })

      Date.now = originalDateNow
    })
  })

  // =========================================================================
  // Mandate 2: Late In-Flight Responses & SignalR Races
  // =========================================================================
  describe("2. Late In-Flight Responses & SignalR Races (Terminal State Locking)", () => {
    it("locks Succeeded state against an onslaught of late stale responses (failed, cancelled, expired, pending)", async () => {
      let resolveLatePoll1: ((v: any) => void) | null = null
      let resolveLatePoll2: ((v: any) => void) | null = null
      const latePoll1Promise = new Promise((r) => {
        resolveLatePoll1 = r
      })
      const latePoll2Promise = new Promise((r) => {
        resolveLatePoll2 = r
      })

      let pollCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        if (urlStr.includes("/receipt")) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                official_receipt_number: "OR-M4-001",
                gross_amount_centavos: 90000,
              }),
          })
        }

        pollCount++
        if (pollCount === 1) {
          // Mount initial poll
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_downgrade_shield_001",
                status: "processing",
                amount_minor: 90000,
                order: {
                  order_number: "ORD-SHIELD-001",
                  title: "Shield Test Order",
                },
              }),
          })
        }
        if (pollCount === 2) {
          return latePoll1Promise
        }
        return latePoll2Promise
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_downgrade_shield_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(screen.getByText(/Awaiting Payment Settlement/i)).toBeDefined()
      })

      // SignalR pushes Succeeded
      await act(async () => {
        if (signalROnStatusChanged) {
          signalROnStatusChanged({
            transactionId: "pay_downgrade_shield_001",
            status: "succeeded",
          })
        }
      })

      // Verified Succeeded
      await waitFor(() => {
        expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      })

      // Attack: Late Poll 1 resolves with failed status
      await act(async () => {
        resolveLatePoll1!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_downgrade_shield_001",
              status: "failed",
              failure_code: "source_failed",
              failure_message: "Stale bank decline message",
              amount_minor: 90000,
            }),
        })
      })

      // Verify Succeeded state is preserved
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
      expect(screen.queryByText(/Awaiting Payment Settlement/i)).toBeNull()
      expect(screen.queryByText(/Stale bank decline message/i)).toBeNull()

      // Attack: SignalR pushes stale failed event
      await act(async () => {
        if (signalROnStatusChanged) {
          signalROnStatusChanged({
            transactionId: "pay_downgrade_shield_001",
            status: "failed",
          })
        }
      })
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)

      // Attack: SignalR pushes expired event
      await act(async () => {
        if (signalROnStatusChanged) {
          signalROnStatusChanged({
            transactionId: "pay_downgrade_shield_001",
            status: "expired",
          })
        }
      })
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)

      // Attack: SignalR pushes cancelled event
      await act(async () => {
        if (signalROnStatusChanged) {
          signalROnStatusChanged({
            transactionId: "pay_downgrade_shield_001",
            status: "cancelled",
          })
        }
      })
      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)

      // Attack: Late Poll 2 resolves with cancelled status
      await act(async () => {
        resolveLatePoll2!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_downgrade_shield_001",
              status: "cancelled",
              failure_code: "source_cancelled",
              amount_minor: 90000,
            }),
        })
      })

      expect(screen.getAllByText(/Payment Confirmed/i).length).toBeGreaterThanOrEqual(1)
    })

    it("verifies usePayMongoRealtimeSync ignores network exception from late in-flight poll after transition to Succeeded", async () => {
      let rejectLatePoll: ((err: any) => void) | null = null
      const latePollPromise = new Promise((_, reject) => {
        rejectLatePoll = reject
      })

      let pollCount = 0
      global.fetch = vi.fn().mockImplementation(() => {
        pollCount++
        if (pollCount === 1) {
          return latePollPromise
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_err_race",
              status: "succeeded",
              is_payable: false,
            }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus }) =>
          usePayMongoRealtimeSync({
            attemptId: "att_err_race",
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: { initialStatus: "requiresaction" },
        }
      )

      expect(result.current.status).toBe("requiresaction")

      // Prop changes to succeeded
      await act(async () => {
        rerender({ initialStatus: "succeeded" })
      })
      expect(result.current.status).toBe("succeeded")

      // In-flight poll rejects with network failure
      await act(async () => {
        rejectLatePoll!(new Error("Network timeout after disconnect"))
      })

      // State remains safely succeeded
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
    })
  })

  // =========================================================================
  // Mandate 3: Query Parameter Forwarding
  // =========================================================================
  describe("3. Query Parameter Forwarding & Deep Reconcile", () => {
    it("PaymentReturnPage forwards ?status=failed correctly", async () => {
      currentSearchParams = {
        payment_id: "pay_test_fwd_01",
        status: "failed",
      }

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_test_fwd_01?status=failed")
      })
    })

    it("PaymentReturnPage forwards ?redirect_status=cancelled correctly", async () => {
      currentSearchParams = {
        payment_id: "pay_test_fwd_02",
        redirect_status: "cancelled",
      }

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_test_fwd_02?status=cancelled")
      })
    })

    it("PaymentReturnPage forwards ?state=expired correctly", async () => {
      currentSearchParams = {
        payment_id: "pay_test_fwd_03",
        state: "expired",
      }

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/pay_test_fwd_03?status=expired")
      })
    })

    it("PaymentReturnPage resolves order_id and forwards ?redirect_status=cancelled to attempt result url", async () => {
      currentSearchParams = {
        order_id: "ord_lookup_m4_001",
        redirect_status: "cancelled",
      }

      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/v1/orders/ord_lookup_m4_001")) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "ord_lookup_m4_001",
                attempts: [{ id: "att_extracted_m4_99", status: "requiresaction" }],
              }),
          })
        }
        return Promise.reject(new Error("Not found"))
      })

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(mockReplace).toHaveBeenCalledWith("/payment/result/att_extracted_m4_99?status=cancelled")
      })
    })

    it("PaymentResultPage receives forwarded status and supplies client_reported_redirect_status in background reconciliation", async () => {
      currentSearchParams = {
        status: "cancelled",
      }

      const requestedUrls: string[] = []
      let pollCount = 0

      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCount++
        requestedUrls.push(String(url))

        if (pollCount === 1) {
          // Mount local fetch
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "pay_reconcile_param_001",
                status: "requiresaction",
                amount_minor: 50000,
                order: {
                  order_number: "ORD-PARAM-001",
                  title: "Param Forwarding Order",
                },
              }),
          })
        }

        // Reconcile poll with client_reported_redirect_status
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "pay_reconcile_param_001",
              status: "failed",
              failure_code: "source_cancelled",
              failure_message: "User cancelled at checkout",
              amount_minor: 50000,
              order: {
                order_number: "ORD-PARAM-001",
                title: "Param Forwarding Order",
              },
            }),
        })
      })

      const paramsPromise = Promise.resolve({ paymentId: "pay_reconcile_param_001" })
      await act(async () => {
        render(<PaymentResultPage params={paramsPromise} />)
      })

      await waitFor(() => {
        expect(requestedUrls.some((u) => u.includes("client_reported_redirect_status=cancelled"))).toBe(true)
      })
    })

    it("PaymentReturnPage renders graceful notice when payment and order resolution finds zero matching records", async () => {
      currentSearchParams = {}

      global.fetch = vi.fn().mockImplementation((url: string) => {
        if (String(url).includes("/api/v1/orders")) {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () => Promise.resolve([]),
          })
        }
        return Promise.reject(new Error("Not found"))
      })

      render(<PaymentReturnPage />)

      await waitFor(() => {
        expect(screen.getByText(/Unable to find matching payment record for this session/i)).toBeDefined()
      })
    })
  })

  // =========================================================================
  // Mandate 4: Cross-Attempt Session Isolation
  // =========================================================================
  describe("4. Cross-Attempt Session Isolation (Rapid Multi-Hop Transitions)", () => {
    it("strictly isolates attempts during rapid multi-hop transitions (att_1 -> att_2 -> att_3)", async () => {
      let resolvePoll1: ((v: any) => void) | null = null
      let resolvePoll2: ((v: any) => void) | null = null

      const poll1Promise = new Promise((r) => {
        resolvePoll1 = r
      })
      const poll2Promise = new Promise((r) => {
        resolvePoll2 = r
      })

      let pollIndex = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        pollIndex++

        if (urlStr.includes("att_hop_1")) {
          return poll1Promise
        }
        if (urlStr.includes("att_hop_2")) {
          return poll2Promise
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_hop_3",
              status: "requiresaction",
              is_payable: true,
              checkout_url: "https://paymongo.com/hop3",
              provider_payment_id: "pay_hop_3_prov",
            }),
        })
      })

      // 1. Mount with att_hop_1
      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId, initialCheckoutUrl, initialProviderPaymentId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            initialCheckoutUrl,
            initialProviderPaymentId,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_hop_1",
            initialCheckoutUrl: "https://paymongo.com/hop1",
            initialProviderPaymentId: "pay_hop_1_prov",
          },
        }
      )

      expect(result.current.status).toBe("requiresaction")

      // 2. Rapid Hop: Transition to att_hop_2 (pending)
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_hop_2",
          initialCheckoutUrl: "https://paymongo.com/hop2",
          initialProviderPaymentId: "pay_hop_2_prov",
        })
      })

      expect(result.current.status).toBe("pending")
      expect(result.current.checkoutUrl).toBe("https://paymongo.com/hop2")
      expect(result.current.providerPaymentId).toBe("pay_hop_2_prov")

      // 3. Rapid Hop: Transition to att_hop_3 (requiresaction)
      await act(async () => {
        rerender({
          initialStatus: "requiresaction",
          attemptId: "att_hop_3",
          initialCheckoutUrl: "https://paymongo.com/hop3",
          initialProviderPaymentId: "pay_hop_3_prov",
        })
      })

      expect(result.current.status).toBe("requiresaction")
      expect(result.current.checkoutUrl).toBe("https://paymongo.com/hop3")
      expect(result.current.providerPaymentId).toBe("pay_hop_3_prov")

      // Attack: Late in-flight poll for att_hop_1 resolves late with 'succeeded'
      await act(async () => {
        resolvePoll1!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_hop_1",
              status: "succeeded",
              is_payable: false,
              checkout_url: null,
              provider_payment_id: "pay_hop_1_prov",
            }),
        })
      })

      // att_hop_3 must NOT be contaminated by att_hop_1's succeeded response
      expect(result.current.status).toBe("requiresaction")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.checkoutUrl).toBe("https://paymongo.com/hop3")

      // Attack: Late SignalR push for att_hop_1 arrives with 'succeeded'
      const signalRHandler = hookSignalRHandlers.get("PaymentStatusChanged")
      if (signalRHandler) {
        await act(async () => {
          signalRHandler({
            attemptId: "att_hop_1",
            status: "succeeded",
          })
        })
      }
      expect(result.current.status).toBe("requiresaction")

      // Attack: Late SignalR push for att_hop_2 arrives with 'failed'
      if (signalRHandler) {
        await act(async () => {
          signalRHandler({
            attemptId: "att_hop_2",
            status: "failed",
            failureCode: "source_failed",
          })
        })
      }
      expect(result.current.status).toBe("requiresaction")

      // Attack: Late in-flight poll for att_hop_2 resolves with 'cancelled'
      await act(async () => {
        resolvePoll2!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_hop_2",
              status: "cancelled",
              failure_code: "source_cancelled",
            }),
        })
      })

      // att_hop_3 must remain pristine
      expect(result.current.status).toBe("requiresaction")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.failureCode).toBeNull()
    })
  })
})

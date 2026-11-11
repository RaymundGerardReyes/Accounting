import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { renderHook, act } from "@testing-library/react"
import { usePayMongoRealtimeSync } from "./use-paymongo-realtime-sync"

type SignalRHandler = (...args: any[]) => void
const signalRHandlers = new Map<string, SignalRHandler>()

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
          signalRHandlers.set(event, handler)
        }),
        off: vi.fn().mockImplementation((event: string) => {
          signalRHandlers.delete(event)
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

describe("Empirical Challenger Stress Harness: Payment Attempt Transitions & Isolation", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    signalRHandlers.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // =========================================================================
  // Challenge 1: Multi-Hop Rapid Transitions (att_1 -> att_2 -> att_3)
  // =========================================================================
  describe("Challenge 1: Multi-hop rapid transitions & initial reconciliation execution", () => {
    it("empirically verifies att_2 and att_3 both execute initial reconciliation without dropping polls or bleeding status", async () => {
      const polledAttempts: string[] = []
      let resolveHop2: ((val: any) => void) | null = null
      let resolveHop3: ((val: any) => void) | null = null

      const hop2Promise = new Promise((r) => {
        resolveHop2 = r
      })
      const hop3Promise = new Promise((r) => {
        resolveHop3 = r
      })

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        const match = urlStr.match(/\/api\/v1\/payments\/([^/?]+)/)
        const id = match ? decodeURIComponent(match[1]) : "unknown"
        polledAttempts.push(id)

        if (id === "att_hop_1") {
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "att_hop_1",
                status: "succeeded",
                is_payable: false,
                provider_payment_id: "pay_hop_1_prov",
              }),
          })
        }
        if (id === "att_hop_2") {
          return hop2Promise
        }
        if (id === "att_hop_3") {
          return hop3Promise
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({ id, status: "pending" }),
        })
      })

      // 1. Initial attempt: att_hop_1 reaches succeeded
      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_hop_1",
          },
        }
      )

      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
      expect(result.current.providerPaymentId).toBe("pay_hop_1_prov")
      expect(polledAttempts).toContain("att_hop_1")

      // 2. Multi-hop transition to att_hop_2 (pending)
      act(() => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_hop_2",
        })
      })

      // Must be pending immediately upon switch, isTerminal false, providerPaymentId reset
      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.providerPaymentId).toBeNull()

      // The initial mount_or_reload poll for att_hop_2 MUST have fired
      expect(polledAttempts).toContain("att_hop_2")

      // Resolve att_hop_2's mount_or_reload poll response
      await act(async () => {
        resolveHop2!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_hop_2",
              status: "processing",
              is_payable: true,
              provider_payment_id: "pay_hop_2_prov",
            }),
        })
      })
      expect(result.current.status).toBe("processing")
      expect(result.current.providerPaymentId).toBe("pay_hop_2_prov")

      // 3. Multi-hop transition to att_hop_3 (requiresaction)
      act(() => {
        rerender({
          initialStatus: "requiresaction",
          attemptId: "att_hop_3",
        })
      })

      expect(result.current.status).toBe("requiresaction")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.providerPaymentId).toBeNull()

      // The initial mount_or_reload poll for att_hop_3 MUST have fired
      expect(polledAttempts).toContain("att_hop_3")

      // Resolve att_hop_3's mount_or_reload poll response
      await act(async () => {
        resolveHop3!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_hop_3",
              status: "succeeded",
              is_payable: false,
              provider_payment_id: "pay_hop_3_prov",
            }),
        })
      })
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
      expect(result.current.providerPaymentId).toBe("pay_hop_3_prov")
    })

    it("handles tri-concurrent in-flight polls resolving out-of-order across att_1 -> att_2 -> att_3 without bleed", async () => {
      let resolvePoll1: ((val: any) => void) | null = null
      let resolvePoll2: ((val: any) => void) | null = null
      let resolvePoll3: ((val: any) => void) | null = null

      const promise1 = new Promise((r) => {
        resolvePoll1 = r
      })
      const promise2 = new Promise((r) => {
        resolvePoll2 = r
      })
      const promise3 = new Promise((r) => {
        resolvePoll3 = r
      })

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        if (urlStr.includes("att_tri_1")) return promise1
        if (urlStr.includes("att_tri_2")) return promise2
        if (urlStr.includes("att_tri_3")) return promise3
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({ status: "pending" }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_tri_1",
          },
        }
      )

      // Start in-flight poll on att_tri_1
      let pollTask1: Promise<any>
      act(() => {
        pollTask1 = result.current.reconcileWithGateway("interval_3s")
      })

      // Transition to att_tri_2
      let pollTask2: Promise<any>
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_tri_2",
        })
      })
      act(() => {
        pollTask2 = result.current.reconcileWithGateway("interval_3s")
      })

      // Transition to att_tri_3
      let pollTask3: Promise<any>
      await act(async () => {
        rerender({
          initialStatus: "requiresaction",
          attemptId: "att_tri_3",
        })
      })
      act(() => {
        pollTask3 = result.current.reconcileWithGateway("interval_3s")
      })

      expect(result.current.status).toBe("requiresaction")

      // Now resolve att_tri_1 late with succeeded (must be discarded)
      await act(async () => {
        resolvePoll1!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_tri_1",
              status: "succeeded",
              is_payable: false,
              provider_payment_id: "pay_tri_1_prov",
            }),
        })
        await pollTask1
      })

      expect(result.current.status).toBe("requiresaction")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.providerPaymentId).toBeNull()

      // Now resolve att_tri_2 late with failed and failure codes (must be discarded)
      await act(async () => {
        resolvePoll2!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_tri_2",
              status: "failed",
              is_payable: false,
              failure_code: "insufficient_funds",
              failure_message: "Balance too low",
              provider_payment_id: "pay_tri_2_prov",
            }),
        })
        await pollTask2
      })

      expect(result.current.status).toBe("requiresaction")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.failureCode).toBeNull()
      expect(result.current.failureMessage).toBeNull()
      expect(result.current.providerPaymentId).toBeNull()

      // Finally, resolve att_tri_3 with processing (must be applied!)
      await act(async () => {
        resolvePoll3!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_tri_3",
              status: "processing",
              is_payable: true,
              provider_payment_id: "pay_tri_3_prov",
            }),
        })
        await pollTask3
      })

      expect(result.current.status).toBe("processing")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.providerPaymentId).toBe("pay_tri_3_prov")
      expect(result.current.failureCode).toBeNull()
    })
  })

  // =========================================================================
  // Challenge 2: Late In-Flight Responses Across Error & Fallback Scenarios
  // =========================================================================
  describe("Challenge 2: Late in-flight responses across failure modes", () => {
    it("discards late failed poll with failure codes and messages when attemptId has transitioned", async () => {
      let resolveSlowPoll: ((val: any) => void) | null = null
      const slowPollPromise = new Promise((r) => {
        resolveSlowPoll = r
      })

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        if (urlStr.includes("att_fail_001")) return slowPollPromise
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_fail_002",
              status: "pending",
              is_payable: true,
            }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_fail_001",
          },
        }
      )

      let pollTask: Promise<any>
      act(() => {
        pollTask = result.current.reconcileWithGateway("interval_3s")
      })

      // Switch to att_fail_002
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_fail_002",
        })
      })

      expect(result.current.status).toBe("pending")

      // Resolve att_fail_001 with failure codes
      await act(async () => {
        resolveSlowPoll!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_fail_001",
              status: "failed",
              failure_code: "fraudulent_attempt",
              failure_message: "Do not honor",
              is_payable: false,
            }),
        })
        await pollTask
      })

      // att_fail_002 MUST remain pending and have no failure codes
      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.failureCode).toBeNull()
      expect(result.current.failureMessage).toBeNull()
    })

    it("discards late 404 fallback fetch response when attemptId transitions during the fallback GET", async () => {
      let resolveFallbackGet: ((val: any) => void) | null = null
      const fallbackGetPromise = new Promise((r) => {
        resolveFallbackGet = r
      })

      global.fetch = vi.fn().mockImplementation((url: string, opts?: any) => {
        const urlStr = String(url)
        if (urlStr.includes("att_fb_001")) {
          if (opts?.method === "GET") {
            // Slow fallback GET
            return fallbackGetPromise
          }
          // Primary POST returns 404
          return Promise.resolve({
            ok: false,
            status: 404,
            json: () => Promise.resolve({ error: "Not found" }),
          })
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_fb_002",
              status: "pending",
              is_payable: true,
            }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_fb_001",
          },
        }
      )

      let pollTask: Promise<any>
      act(() => {
        pollTask = result.current.reconcileWithGateway("interval_3s")
      })

      // Transition to att_fb_002 while fallback GET is in flight
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_fb_002",
        })
      })
      expect(result.current.status).toBe("pending")

      // Now fallback GET for att_fb_001 resolves with succeeded
      await act(async () => {
        resolveFallbackGet!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_fb_001",
              status: "succeeded",
              is_payable: false,
            }),
        })
        await pollTask
      })

      // att_fb_002 must remain pending!
      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)
    })

    it("survives network throw / fetch rejection on superseded attempt without affecting active attempt", async () => {
      let rejectSlowPoll: ((err: any) => void) | null = null
      const slowPollPromise = new Promise((_, r) => {
        rejectSlowPoll = r
      })

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        if (urlStr.includes("att_err_001")) return slowPollPromise
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_err_002",
              status: "pending",
              is_payable: true,
            }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_err_001",
          },
        }
      )

      let pollTask: Promise<any>
      act(() => {
        pollTask = result.current.reconcileWithGateway("interval_3s")
      })

      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_err_002",
        })
      })
      expect(result.current.status).toBe("pending")

      // Reject the poll for att_err_001
      await act(async () => {
        rejectSlowPoll!(new TypeError("Failed to fetch"))
        await pollTask.catch(() => {})
      })

      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)
    })

    it("verifies in-flight lock of active attempt is NOT prematurely cleared by superseded poll completion", async () => {
      let resolvePoll1: ((val: any) => void) | null = null
      let resolvePoll2: ((val: any) => void) | null = null

      const promise1 = new Promise((r) => {
        resolvePoll1 = r
      })
      const promise2 = new Promise((r) => {
        resolvePoll2 = r
      })

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        if (urlStr.includes("att_lock_001")) return promise1
        if (urlStr.includes("att_lock_002")) return promise2
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve({ status: "pending" }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_lock_001",
          },
        }
      )

      // Start poll 1
      let pollTask1: Promise<any>
      act(() => {
        pollTask1 = result.current.reconcileWithGateway("scheduled_poll")
      })

      // Transition to att_lock_002
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_lock_002",
        })
      })

      // Start poll 2 for att_lock_002
      let pollTask2: Promise<any>
      act(() => {
        pollTask2 = result.current.reconcileWithGateway("scheduled_poll")
      })

      expect(result.current.isReconciling).toBe(true)

      // Now resolve poll 1
      await act(async () => {
        resolvePoll1!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_lock_001",
              status: "succeeded",
              is_payable: false,
            }),
        })
        await pollTask1
      })

      // att_lock_002 must STILL be reconciling because poll 2 is still running!
      expect(result.current.isReconciling).toBe(true)

      // Now resolve poll 2
      await act(async () => {
        resolvePoll2!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_lock_002",
              status: "processing",
              is_payable: true,
            }),
        })
        await pollTask2
      })

      // Now att_lock_002 is done reconciling
      expect(result.current.isReconciling).toBe(false)
      expect(result.current.status).toBe("processing")
    })
  })

  // =========================================================================
  // Challenge 3: Prop Re-render Stress on Succeeded Attempt
  // =========================================================================
  describe("Challenge 3: Prop re-render stress on succeeded attempt", () => {
    it("preserves succeeded status under 50 rapid-fire adversarial prop re-renders", async () => {
      global.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_stress_001",
              status: "succeeded",
              is_payable: false,
            }),
        })
      )

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId, initialCheckoutUrl, initialExpiresAtUtc }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            initialCheckoutUrl,
            initialExpiresAtUtc,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_stress_001",
            initialCheckoutUrl: "https://checkout.test/original",
            initialExpiresAtUtc: "2026-11-15T00:00:00Z",
          },
        }
      )

      // Succeeded via gateway
      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
      expect(result.current.checkoutUrl).toBeNull()

      const hostileStatuses = [
        "requiresaction",
        "pending",
        "processing",
        "failed",
        "expired",
        "cancelled",
        "canceled",
        "active",
        "unknown",
        "",
        null as any,
        undefined as any,
      ]

      for (let i = 0; i < 50; i++) {
        const hostileStatus = hostileStatuses[i % hostileStatuses.length]
        act(() => {
          rerender({
            initialStatus: hostileStatus,
            attemptId: "att_stress_001",
            initialCheckoutUrl: `https://checkout.test/hostile_${i}`,
            initialExpiresAtUtc: `2026-11-${10 + (i % 20)}T00:00:00Z`,
          })
        })

        expect(result.current.status).toBe("succeeded")
        expect(result.current.isTerminal).toBe(true)
        expect(result.current.isPayable).toBe(false)
        expect(result.current.checkoutUrl).toBeNull()
        expect(result.current.remainingSeconds).toBe(0)
      }
    })

    it("suppresses all gateway polls when already in terminal succeeded state", async () => {
      let fetchCount = 0
      global.fetch = vi.fn().mockImplementation(() => {
        fetchCount++
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_suppress_001",
              status: "succeeded",
              is_payable: false,
            }),
        })
      })

      const { result } = renderHook(() =>
        usePayMongoRealtimeSync({
          attemptId: "att_suppress_001",
          initialStatus: "requiresaction",
          enableDebugLogs: false,
        })
      )

      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")

      const callsAfterSucceeded = fetchCount

      // Attempt all possible trigger reasons
      const triggerReasons = [
        "scheduled_poll",
        "interval_3s",
        "window_focus",
        "tab_visibility_visible",
        "countdown_reached_zero",
        "mount_or_reload",
      ]

      for (const reason of triggerReasons) {
        await act(async () => {
          await result.current.reconcileWithGateway(reason)
        })
      }

      // ZERO additional fetch calls must be made
      expect(fetchCount).toBe(callsAfterSucceeded)
    })

    it("locks providerPaymentId, checkoutUrl, and failureCode when options are mutated on succeeded attempt", async () => {
      global.fetch = vi.fn().mockImplementation(() =>
        Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_lock_opt_001",
              status: "succeeded",
              provider_payment_id: "pay_legit_123",
              is_payable: false,
            }),
        })
      )

      const { result, rerender } = renderHook(
        (props: any) => usePayMongoRealtimeSync(props),
        {
          initialProps: {
            attemptId: "att_lock_opt_001",
            initialStatus: "requiresaction",
            initialCheckoutUrl: "https://checkout.test/orig",
            initialProviderPaymentId: "pay_initial_000",
            enableDebugLogs: false,
          },
        }
      )

      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")
      expect(result.current.providerPaymentId).toBe("pay_legit_123")
      expect(result.current.checkoutUrl).toBeNull()

      // Stale parent re-renders with adversarial option changes
      act(() => {
        rerender({
          attemptId: "att_lock_opt_001",
          initialStatus: "requiresaction",
          initialCheckoutUrl: "https://checkout.test/hostile_overwrite",
          initialProviderPaymentId: "pay_hostile_999",
          enableDebugLogs: false,
        })
      })

      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
      expect(result.current.providerPaymentId).toBe("pay_legit_123")
      expect(result.current.checkoutUrl).toBeNull()
      expect(result.current.failureCode).toBeNull()
    })
  })

  // =========================================================================
  // Challenge 4: Deep Multi-Hop Sequence & Clock Skew Resilience
  // =========================================================================
  describe("Challenge 4: Deep multi-hop sequence & clock skew resilience", () => {
    it("preserves correct state across a 5-hop rapid sequence through varied terminal states", async () => {
      const hopStatuses = [
        { id: "att_seq_1", initial: "requiresaction", resolved: "succeeded" },
        { id: "att_seq_2", initial: "pending", resolved: "expired" },
        { id: "att_seq_3", initial: "processing", resolved: "failed" },
        { id: "att_seq_4", initial: "pending", resolved: "cancelled" },
        { id: "att_seq_5", initial: "requiresaction", resolved: "processing" },
      ]

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        const match = urlStr.match(/\/api\/v1\/payments\/([^/?]+)/)
        const id = match ? decodeURIComponent(match[1]) : "unknown"
        const hop = hopStatuses.find((h) => h.id === id)
        const status = hop?.resolved ?? "pending"

        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id,
              status,
              provider_payment_id: `prov_${id}`,
              is_payable: status === "requiresaction" || status === "pending" || status === "processing",
            }),
        })
      })

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: hopStatuses[0].initial,
            attemptId: hopStatuses[0].id,
          },
        }
      )

      for (let i = 0; i < hopStatuses.length; i++) {
        const hop = hopStatuses[i]
        await act(async () => {
          rerender({
            initialStatus: hop.initial,
            attemptId: hop.id,
          })
        })

        // Poll to resolve
        await act(async () => {
          await result.current.reconcileWithGateway("mount_or_reload")
        })

        expect(result.current.status).toBe(hop.resolved)
        expect(result.current.providerPaymentId).toBe(`prov_${hop.id}`)
      }
    })

    it("discards late response with 10-hour clock skew and stale checkout url without corrupting active attempt", async () => {
      let resolveSkewedPoll: ((val: any) => void) | null = null
      const skewedPromise = new Promise((r) => {
        resolveSkewedPoll = r
      })

      global.fetch = vi.fn().mockImplementation((url: string) => {
        const urlStr = String(url)
        if (urlStr.includes("att_skew_001")) return skewedPromise
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_skew_002",
              status: "pending",
              checkout_url: "https://paymongo.test/checkout/legit_002",
              server_time_utc: new Date().toISOString(),
              is_payable: true,
            }),
        })
      })

      const futureDate = new Date(Date.now() + 600000).toISOString() // +10 minutes
      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId, initialExpiresAtUtc }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            initialExpiresAtUtc,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_skew_001",
            initialExpiresAtUtc: futureDate,
          },
        }
      )

      let pollTask: Promise<any>
      act(() => {
        pollTask = result.current.reconcileWithGateway("interval_3s")
      })

      // Switch to att_skew_002
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_skew_002",
          initialExpiresAtUtc: futureDate,
        })
      })

      // Resolve att_skew_001 with 10 hours in the past clock skew and hostile checkout url
      const skewedServerTime = new Date(Date.now() - 36000000).toISOString()
      await act(async () => {
        resolveSkewedPoll!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_skew_001",
              status: "succeeded",
              checkout_url: "https://paymongo.test/checkout/HOSTILE_CORRUPT",
              server_time_utc: skewedServerTime,
              is_payable: false,
            }),
        })
        await pollTask
      })

      // Active attempt att_skew_002 MUST remain pending
      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)
      // Telemetry clock offset must NOT be -36000000
      expect(result.current.telemetry.serverClockOffsetMs).toBe(0)
    })
  })
})

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { renderHook, act } from "@testing-library/react"
import { usePayMongoRealtimeSync } from "./use-paymongo-realtime-sync"

type SignalRHandler = (...args: any[]) => void
const signalRHandlers = new Map<string, SignalRHandler>()

let mockSignalRStart: any
let mockSignalRStop: any
let mockSignalRInvoke: any

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
      mockSignalRStart = vi.fn().mockImplementation(() => Promise.resolve())
      mockSignalRStop = vi.fn().mockImplementation(() => Promise.resolve())
      mockSignalRInvoke = vi.fn().mockImplementation(() => Promise.resolve())
      return {
        start: mockSignalRStart,
        stop: mockSignalRStop,
        invoke: mockSignalRInvoke,
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

describe("Adversarial Stress Harness: usePayMongoRealtimeSync", () => {
  let attemptStatusMap: Record<string, string> = {}

  beforeEach(() => {
    vi.clearAllMocks()
    signalRHandlers.clear()
    attemptStatusMap = {}

    global.fetch = vi.fn().mockImplementation((url: string) => {
      const urlStr = String(url)
      // Extract attemptId from /api/v1/payments/{id}/poll or /api/v1/payments/{id}
      const match = urlStr.match(/\/api\/v1\/payments\/([^/?]+)/)
      const id = match ? decodeURIComponent(match[1]) : "unknown"
      const status = attemptStatusMap[id] ?? "requiresaction"

      return Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            id,
            status,
            is_payable: status === "requiresaction" || status === "pending",
          }),
      })
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // =========================================================================
  // Challenge 1: Hook Prop Re-render Races
  // =========================================================================
  describe("Challenge 1: Hook prop re-render races after transition to succeeded", () => {
    it("preserves succeeded status when parent re-renders with initialStatus: 'requiresaction'", async () => {
      attemptStatusMap["att_adv_001"] = "succeeded"

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
            attemptId: "att_adv_001",
          },
        }
      )

      expect(result.current.status).toBe("requiresaction")

      // Trigger transition to succeeded via gateway poll
      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")

      // Stress test: Parent re-renders with initialStatus: "requiresaction"
      act(() => {
        rerender({
          initialStatus: "requiresaction",
          attemptId: "att_adv_001",
        })
      })

      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
    })

    it("preserves succeeded status across an adversarial sequence of prop updates (pending, processing, failed, expired, cancelled)", async () => {
      attemptStatusMap["att_adv_seq_001"] = "succeeded"

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "processing",
            attemptId: "att_adv_seq_001",
          },
        }
      )

      // Transition to succeeded
      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")

      const adversarialStatuses = [
        "requiresaction",
        "requires_action",
        "pending",
        "processing",
        "failed",
        "expired",
        "cancelled",
        "unknown",
      ]

      for (const hostileStatus of adversarialStatuses) {
        act(() => {
          rerender({
            initialStatus: hostileStatus,
            attemptId: "att_adv_seq_001",
          })
        })
        expect(result.current.status).toBe("succeeded")
        expect(result.current.isTerminal).toBe(true)
      }
    })

    it("locks failure codes and checkout url when prop re-renders on the same attemptId", async () => {
      attemptStatusMap["att_adv_lock_001"] = "succeeded"

      const { result, rerender } = renderHook(
        ({ initialStatus, attemptId, initialCheckoutUrl }) =>
          usePayMongoRealtimeSync({
            attemptId,
            initialStatus,
            initialCheckoutUrl,
            enableDebugLogs: false,
          }),
        {
          initialProps: {
            initialStatus: "requiresaction",
            attemptId: "att_adv_lock_001",
            initialCheckoutUrl: "https://paymongo.test/checkout/1",
          },
        }
      )

      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")
      expect(result.current.checkoutUrl).toBeNull()

      // Parent re-renders with stale props containing old checkoutUrl and requiresaction
      act(() => {
        rerender({
          initialStatus: "requiresaction",
          attemptId: "att_adv_lock_001",
          initialCheckoutUrl: "https://paymongo.test/checkout/STALE",
        })
      })

      expect(result.current.status).toBe("succeeded")
      expect(result.current.checkoutUrl).toBeNull()
      expect(result.current.failureCode).toBeNull()
    })
  })

  // =========================================================================
  // Challenge 2: AttemptId Transitions
  // =========================================================================
  describe("Challenge 2: AttemptId changing to a new attempt", () => {
    it("resets state completely to the new attempt's initial status when attemptId changes", async () => {
      attemptStatusMap["att_old_001"] = "succeeded"
      attemptStatusMap["att_new_002"] = "requiresaction"

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
            attemptId: "att_old_001",
            initialCheckoutUrl: "https://checkout.test/old",
            initialExpiresAtUtc: "2026-11-10T12:00:00Z",
          },
        }
      )

      // Old attempt reaches succeeded
      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)

      // New attempt starts with requiresaction
      await act(async () => {
        rerender({
          initialStatus: "requiresaction",
          attemptId: "att_new_002",
          initialCheckoutUrl: "https://checkout.test/new",
          initialExpiresAtUtc: "2026-11-12T12:00:00Z",
        })
      })

      expect(result.current.status).toBe("requiresaction")
      expect(result.current.isTerminal).toBe(false)
      expect(result.current.checkoutUrl).toBe("https://checkout.test/new")
      expect(result.current.failureCode).toBeNull()
      expect(result.current.failureMessage).toBeNull()
    })

    it("handles multiple rapid attempt hops (att_1 -> att_2 -> att_3) cleanly without bleed-through", async () => {
      attemptStatusMap["att_hop_1"] = "succeeded"
      attemptStatusMap["att_hop_2"] = "pending"
      attemptStatusMap["att_hop_3"] = "requiresaction"

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

      // Hop 1 -> att_hop_2 (pending)
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_hop_2",
        })
      })
      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)

      // Hop 2 -> att_hop_3 (requiresaction)
      await act(async () => {
        rerender({
          initialStatus: "requiresaction",
          attemptId: "att_hop_3",
        })
      })
      expect(result.current.status).toBe("requiresaction")
      expect(result.current.isTerminal).toBe(false)
    })

    it("discards late in-flight poll response from prior attempt when attemptId changes", async () => {
      let resolvePriorPoll: ((val: any) => void) | null = null
      const priorPollPromise = new Promise((resolve) => {
        resolvePriorPoll = resolve
      })

      let pollCount = 0
      global.fetch = vi.fn().mockImplementation((url: string) => {
        pollCount++
        if (pollCount === 1) {
          // Slow in-flight poll for att_prior_001
          return priorPollPromise
        }
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_next_002",
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
            attemptId: "att_prior_001",
          },
        }
      )

      // Start in-flight poll for att_prior_001
      let pollTask: Promise<any>
      act(() => {
        pollTask = result.current.reconcileWithGateway("interval_3s")
      })

      // Customer transitions to a new attempt session
      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_next_002",
        })
      })
      expect(result.current.status).toBe("pending")

      // The slow poll for att_prior_001 resolves late with "succeeded"
      await act(async () => {
        resolvePriorPoll!({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_prior_001",
              status: "succeeded",
              is_payable: false,
            }),
        })
        await pollTask
      })

      // Active attempt att_next_002 MUST remain pending and NOT bleed to succeeded
      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)
    })

    it("discards SignalR push for previous attemptId after attemptId transitions", async () => {
      attemptStatusMap["att_sig_001"] = "requiresaction"
      attemptStatusMap["att_sig_002"] = "pending"

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
            attemptId: "att_sig_001",
          },
        }
      )

      await act(async () => {
        rerender({
          initialStatus: "pending",
          attemptId: "att_sig_002",
        })
      })
      expect(result.current.status).toBe("pending")

      const paymentChangedHandler = signalRHandlers.get("PaymentStatusChanged")
      if (paymentChangedHandler) {
        await act(async () => {
          paymentChangedHandler({
            attemptId: "att_sig_001",
            status: "succeeded",
            updatedAt: new Date().toISOString(),
          })
        })
      }

      // att_sig_002 must NOT be corrupted by att_sig_001's SignalR push
      expect(result.current.status).toBe("pending")
      expect(result.current.isTerminal).toBe(false)
    })
  })

  // =========================================================================
  // Challenge 3: Concurrent SignalR vs Polling Races
  // =========================================================================
  describe("Challenge 3: Concurrent SignalR vs Polling Races & Terminal State Locking", () => {
    it("locks succeeded state when SignalR pushes succeeded while a slow poll returns requiresaction", async () => {
      let resolveSlowPoll: (val: any) => void = () => {}
      const slowPollPromise = new Promise((resolve) => {
        resolveSlowPoll = resolve
      })

      let pollCallCount = 0
      global.fetch = vi.fn().mockImplementation(() => {
        pollCallCount++
        if (pollCallCount === 1) {
          // Initial fast poll or mount
          return Promise.resolve({
            ok: true,
            status: 200,
            json: () =>
              Promise.resolve({
                id: "att_race_001",
                status: "requiresaction",
                is_payable: true,
              }),
          })
        }
        // In-flight slow poll
        return slowPollPromise
      })

      const { result } = renderHook(() =>
        usePayMongoRealtimeSync({
          attemptId: "att_race_001",
          initialStatus: "requiresaction",
          enableDebugLogs: false,
        })
      )

      expect(result.current.status).toBe("requiresaction")

      // Launch in-flight slow poll
      let pollTask: Promise<any>
      act(() => {
        pollTask = result.current.reconcileWithGateway("interval_3s")
      })

      // While slow poll is pending, SignalR pushes authoritative succeeded status
      const paymentChangedHandler = signalRHandlers.get("PaymentStatusChanged")
      expect(paymentChangedHandler).toBeDefined()

      await act(async () => {
        paymentChangedHandler!({
          attemptId: "att_race_001",
          status: "succeeded",
          updatedAt: new Date().toISOString(),
        })
      })

      // State is now succeeded via SignalR
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)

      // Now the slow poll finally resolves with stale "requiresaction"
      await act(async () => {
        resolveSlowPoll({
          ok: true,
          status: 200,
          json: () =>
            Promise.resolve({
              id: "att_race_001",
              status: "requiresaction",
              is_payable: true,
            }),
        })
        await pollTask
      })

      // Status must REMAIN succeeded and NOT revert to requiresaction
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
    })

    it("locks succeeded state when poll returns succeeded and delayed SignalR push arrives with processing", async () => {
      attemptStatusMap["att_race_002"] = "succeeded"

      const { result } = renderHook(() =>
        usePayMongoRealtimeSync({
          attemptId: "att_race_002",
          initialStatus: "processing",
          enableDebugLogs: false,
        })
      )

      // Poll resolves to succeeded
      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")

      // Stale/delayed SignalR push arrives with "processing"
      const paymentChangedHandler = signalRHandlers.get("PaymentStatusChanged")
      if (paymentChangedHandler) {
        await act(async () => {
          paymentChangedHandler({
            attemptId: "att_race_002",
            status: "processing",
            updatedAt: new Date().toISOString(),
          })
        })
      }

      // Status must remain succeeded
      expect(result.current.status).toBe("succeeded")
      expect(result.current.isTerminal).toBe(true)
    })

    it("does not trigger additional gateway polls on window focus when already in terminal succeeded state", async () => {
      attemptStatusMap["att_term_focus"] = "succeeded"

      let pollCount = 0
      const originalFetch = global.fetch
      global.fetch = vi.fn().mockImplementation((url) => {
        pollCount++
        return originalFetch(url)
      })

      const { result } = renderHook(() =>
        usePayMongoRealtimeSync({
          attemptId: "att_term_focus",
          initialStatus: "requiresaction",
          enableDebugLogs: false,
        })
      )

      await act(async () => {
        await result.current.reconcileWithGateway("manual_debug_button")
      })
      expect(result.current.status).toBe("succeeded")

      const callsAfterSucceeded = pollCount

      // Fire window focus multiple times
      await act(async () => {
        window.dispatchEvent(new Event("focus"))
        window.dispatchEvent(new Event("focus"))
      })

      // In terminal state, reconcileWithGateway returns early
      expect(pollCount).toBe(callsAfterSucceeded)
    })

    it("cleans up SignalR connection and polling timers without unhandled rejections or leaks on unmount", async () => {
      const { unmount } = renderHook(() =>
        usePayMongoRealtimeSync({
          attemptId: "att_unmount_001",
          initialStatus: "requiresaction",
          enableDebugLogs: false,
        })
      )

      await act(async () => {
        unmount()
      })

      expect(mockSignalRStop).toHaveBeenCalled()
    })
  })
})

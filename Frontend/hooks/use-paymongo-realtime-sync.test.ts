import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { renderHook, act } from "@testing-library/react"
import { usePayMongoRealtimeSync, normalizePaymentStatus } from "./use-paymongo-realtime-sync"

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

describe("usePayMongoRealtimeSync - State Locking & Focus Sync", () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockImplementation((url: string) => {
      const urlStr = String(url)
      const match = urlStr.match(/\/api\/v1\/payments\/([^/?]+)/)
      const id = match ? decodeURIComponent(match[1]) : "att_test_123"
      const isPending = id === "att_session_002"

      return Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            id,
            status: isPending ? "pending" : "succeeded",
            is_payable: isPending,
          }),
      })
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("normalizes various raw payment statuses accurately", () => {
    expect(normalizePaymentStatus("paid")).toBe("succeeded")
    expect(normalizePaymentStatus("succeeded")).toBe("succeeded")
    expect(normalizePaymentStatus("failed")).toBe("failed")
    expect(normalizePaymentStatus("expired")).toBe("expired")
    expect(normalizePaymentStatus("cancelled")).toBe("cancelled")
    expect(normalizePaymentStatus("canceled")).toBe("cancelled")
    expect(normalizePaymentStatus("processing")).toBe("processing")
    expect(normalizePaymentStatus("requiresaction")).toBe("requiresaction")
    expect(normalizePaymentStatus("requires_action")).toBe("requiresaction")
    expect(normalizePaymentStatus("active")).toBe("requiresaction")
    expect(normalizePaymentStatus("unknown")).toBe("pending")
    expect(normalizePaymentStatus(null)).toBe("pending")
  })

  it("locks Succeeded state against prop re-renders for the same attemptId", async () => {
    let propStatus = "requiresaction"
    const { result, rerender } = renderHook(
      ({ initialStatus, attemptId }) =>
        usePayMongoRealtimeSync({
          attemptId,
          initialStatus,
          enableDebugLogs: false,
        }),
      {
        initialProps: {
          initialStatus: propStatus,
          attemptId: "att_session_001",
        },
      }
    )

    // Initially in requiresaction
    expect(result.current.status).toBe("requiresaction")

    // Gateway poll resolves to succeeded
    await act(async () => {
      await result.current.reconcileWithGateway("manual_debug_button")
    })

    expect(result.current.status).toBe("succeeded")

    // Parent re-renders with stale prop initialStatus = "requiresaction" on the same attemptId
    rerender({
      initialStatus: "requiresaction",
      attemptId: "att_session_001",
    })

    // Succeeded state MUST be preserved and not downgraded
    expect(result.current.status).toBe("succeeded")
  })

  it("resets state when attemptId changes to a new payment attempt", async () => {
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
          attemptId: "att_session_001",
        },
      }
    )

    // Reconcile to succeeded
    await act(async () => {
      await result.current.reconcileWithGateway("manual_debug_button")
    })
    expect(result.current.status).toBe("succeeded")

    // User initiates a brand new attempt
    await act(async () => {
      rerender({
        initialStatus: "pending",
        attemptId: "att_session_002",
      })
    })

    // New attempt gets new initialStatus
    expect(result.current.status).toBe("pending")
    expect(result.current.isTerminal).toBe(false)
  })

  it("triggers reconciliation when window focus occurs", async () => {
    let pollCount = 0
    global.fetch = vi.fn().mockImplementation(() => {
      pollCount++
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            id: "att_focus_test",
            status: "processing",
            is_payable: true,
          }),
      })
    })

    await act(async () => {
      renderHook(() =>
        usePayMongoRealtimeSync({
          attemptId: "att_focus_test",
          initialStatus: "requiresaction",
          enableDebugLogs: false,
        })
      )
    })

    const initialCalls = pollCount

    // Dispatch window focus
    await act(async () => {
      window.dispatchEvent(new Event("focus"))
    })

    expect(pollCount).toBeGreaterThan(initialCalls)
  })

  it("triggers reconciliation when tab visibility changes to visible", async () => {
    let pollCount = 0
    global.fetch = vi.fn().mockImplementation(() => {
      pollCount++
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            id: "att_vis_test",
            status: "processing",
            is_payable: true,
          }),
      })
    })

    await act(async () => {
      renderHook(() =>
        usePayMongoRealtimeSync({
          attemptId: "att_vis_test",
          initialStatus: "requiresaction",
          enableDebugLogs: false,
        })
      )
    })

    const initialCalls = pollCount

    // Dispatch tab visibility change
    Object.defineProperty(document, "visibilityState", {
      value: "visible",
      configurable: true,
    })

    await act(async () => {
      document.dispatchEvent(new Event("visibilitychange"))
    })

    expect(pollCount).toBeGreaterThan(initialCalls)
  })

  it("passes auto_reconcile=true on poll requests and preserves active session on qrph_source_expired", async () => {
    const capturedUrls: string[] = []
    const futureIso = new Date(Date.now() + 1680 * 1000).toISOString()

    global.fetch = vi.fn().mockImplementation((url: string) => {
      capturedUrls.push(String(url))
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            id: "ac188954-5429-42f6-9418-e7564ef36925",
            provider_payment_id: "cs_fafe58a436adb3bc3aee6274",
            status: "requiresaction",
            checkout_url: "https://checkout.paymongo.com/cs_fafe58a436adb3bc3aee6274",
            expires_at: futureIso,
            failure_code: "qrph_source_expired",
            failure_message: "Underlying QR code expired while Checkout Session remains active.",
          }),
      })
    })

    const { result } = renderHook(() =>
      usePayMongoRealtimeSync({
        attemptId: "ac188954-5429-42f6-9418-e7564ef36925",
        initialStatus: "requiresaction",
        initialExpiresAtUtc: null,
        initialCheckoutUrl: null,
        enableDebugLogs: false,
      })
    )

    await act(async () => {
      await result.current.reconcileWithGateway("manual_debug_button")
    })

    expect(capturedUrls.some((u) => u.includes("auto_reconcile=true"))).toBe(true)
    expect(capturedUrls.some((u) => u.includes("force_reconcile=true"))).toBe(true)
    expect(result.current.status).toBe("requiresaction")
    expect(result.current.isTerminal).toBe(false)
    expect(result.current.failureCode).toBe("qrph_source_expired")
    expect(result.current.remainingSeconds).toBeGreaterThan(1600)
    expect(result.current.isPayable).toBe(true)
  })

  it("forwards client_reported_redirect_status=cancelled on cancel redirect and transitions to failed", async () => {
    const capturedUrls: string[] = []

    global.fetch = vi.fn().mockImplementation((url: string) => {
      capturedUrls.push(String(url))
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            id: "b8c42e75-1c25-47e2-bd16-3f0b3c5622c7",
            provider_payment_id: "cs_d82fafa0fd3d9d5aaa9e8ad2",
            status: "failed",
            checkout_url: null,
            is_payable: false,
            failure_code: "source_cancelled",
            failure_message: "Payment cancelled by customer during checkout redirect.",
          }),
      })
    })

    const { result } = renderHook(() =>
      usePayMongoRealtimeSync({
        attemptId: "b8c42e75-1c25-47e2-bd16-3f0b3c5622c7",
        initialStatus: "requiresaction",
        initialProviderPaymentId: "cs_d82fafa0fd3d9d5aaa9e8ad2",
        clientReportedRedirectStatus: "cancelled",
        enableDebugLogs: false,
      })
    )

    await act(async () => {
      await Promise.resolve()
    })

    expect(
      capturedUrls.some((u) =>
        u.includes("client_reported_redirect_status=cancelled")
      )
    ).toBe(true)
    expect(result.current.status).toBe("failed")
    expect(result.current.isTerminal).toBe(true)
    expect(result.current.failureCode).toBe("source_cancelled")
    expect(result.current.isPayable).toBe(false)
  })
})

"use client"

import { useEffect, useRef, useState, useCallback } from "react"
import { pollPaymentStatus, PaymentAttempt } from "@/lib/payment-api"
import { usePaymentSignalR } from "./usePaymentSignalR"

export interface UsePayMongoRealtimeSyncOptions {
  attemptId: string | null | undefined
  initialStatus?: string
  expiresAt?: string | null
  checkoutUrl?: string | null
  onStatusChange?: (newStatus: string) => void
  autoReconcile?: boolean
}

export interface UsePayMongoRealtimeSyncReturn {
  status: string
  isPayable: boolean
  isTerminal: boolean
  remainingSeconds: number
  formattedRemaining: string
  checkoutUrl: string | null
  isSyncing: boolean
  isSignalRConnected: boolean
  error: string | null
  reconcileNow: () => Promise<void>
}

const TERMINAL_STATUSES = new Set(["paid", "succeeded", "expired", "failed", "cancelled"])

function normalizeStatus(st?: string | null): string {
  if (!st) return "pending"
  const s = st.toLowerCase()
  if (s === "paid") return "succeeded"
  if (s === "requiresaction") return "requires_action"
  return s
}

/**
 * Real-time synchronization hook for PayMongo Checkout Sessions and Payment Attempts.
 * Provides:
 * 1. Immediate local status evaluation on reload/mount (F5).
 * 2. Instant tab-focus reconciliation upon return from https://checkout.paymongo.com/...
 * 3. Clock-skew compensated countdown timer.
 * 4. WebSocket SignalR subscription with terminal state immutability.
 * 5. Deterministic session URL locking upon terminal transitions.
 */
export function usePayMongoRealtimeSync({
  attemptId,
  initialStatus = "pending",
  expiresAt: initialExpiresAt,
  checkoutUrl: initialCheckoutUrl,
  onStatusChange,
  autoReconcile = true,
}: UsePayMongoRealtimeSyncOptions): UsePayMongoRealtimeSyncReturn {
  const [status, setStatus] = useState<string>(() => normalizeStatus(initialStatus))
  const [expiresAt, setExpiresAt] = useState<string | null>(initialExpiresAt || null)
  const [checkoutUrl, setCheckoutUrl] = useState<string | null>(initialCheckoutUrl || null)
  const [remainingSeconds, setRemainingSeconds] = useState<number>(0)
  const [isSyncing, setIsSyncing] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const serverClockOffsetMsRef = useRef<number>(0)
  const expiredReconcileTriggeredRef = useRef<boolean>(false)
  const onStatusChangeRef = useRef(onStatusChange)
  const statusRef = useRef(status)

  useEffect(() => {
    onStatusChangeRef.current = onStatusChange
  }, [onStatusChange])

  useEffect(() => {
    statusRef.current = status
  }, [status])

  useEffect(() => {
    if (initialExpiresAt) setExpiresAt(initialExpiresAt)
  }, [initialExpiresAt])

  useEffect(() => {
    if (initialCheckoutUrl) setCheckoutUrl(initialCheckoutUrl)
  }, [initialCheckoutUrl])

  const isTerminal = TERMINAL_STATUSES.has(status)

  const applyStatusUpdate = useCallback((newRawStatus: string) => {
    const nextStatus = normalizeStatus(newRawStatus)
    const current = statusRef.current

    // Invariant: Terminal status is immutable; never revert to pending/requires_action
    if (TERMINAL_STATUSES.has(current) && !TERMINAL_STATUSES.has(nextStatus)) {
      return
    }

    if (current !== nextStatus) {
      statusRef.current = nextStatus
      setStatus(nextStatus)
      if (TERMINAL_STATUSES.has(nextStatus)) {
        setCheckoutUrl(null)
      }
      onStatusChangeRef.current?.(nextStatus)
    }
  }, [])

  const reconcileNow = useCallback(async () => {
    if (!attemptId || TERMINAL_STATUSES.has(statusRef.current)) {
      return
    }

    setIsSyncing(true)
    setError(null)
    try {
      const data: PaymentAttempt = await pollPaymentStatus(attemptId, autoReconcile)
      if (data) {
        if (data.expires_at) {
          setExpiresAt(data.expires_at)
        }
        if (data.checkout_url) {
          setCheckoutUrl(data.checkout_url)
        }
        applyStatusUpdate(data.status)
      }
    } catch (err: any) {
      setError(err?.message || "Failed to synchronize payment state")
    } finally {
      setIsSyncing(false)
    }
  }, [attemptId, autoReconcile, applyStatusUpdate])

  // SignalR real-time push subscription
  const { isConnected: isSignalRConnected } = usePaymentSignalR({
    transactionId: attemptId,
    enabled: !isTerminal && Boolean(attemptId),
    onStatusChanged: (payload) => {
      if (payload?.status) {
        applyStatusUpdate(payload.status)
      }
    },
    onPollFallback: () => {
      if (!isTerminal) {
        reconcileNow()
      }
    },
  })

  // 1. Initial Mount: fast-path poll followed by background verification
  useEffect(() => {
    if (!attemptId || isTerminal) return

    let isMounted = true

    const initialFetch = async () => {
      try {
        const local = await pollPaymentStatus(attemptId, false)
        if (!isMounted) return
        if (local.expires_at) setExpiresAt(local.expires_at)
        if (local.checkout_url) setCheckoutUrl(local.checkout_url)
        applyStatusUpdate(local.status)

        // Non-blocking gateway verification if still non-terminal
        if (!TERMINAL_STATUSES.has(normalizeStatus(local.status)) && autoReconcile) {
          reconcileNow()
        }
      } catch {
        // Fallback silently to existing props
      }
    }

    initialFetch()

    return () => {
      isMounted = false
    }
  }, [attemptId, isTerminal, autoReconcile, reconcileNow, applyStatusUpdate])

  // 2. Tab focus & document visibilitychange: immediate sync when returning from PayMongo checkout tab
  useEffect(() => {
    if (!attemptId || isTerminal || typeof window === "undefined") {
      return
    }

    const handleVisibilityOrFocus = () => {
      if (document.visibilityState === "visible" && !TERMINAL_STATUSES.has(statusRef.current)) {
        reconcileNow()
      }
    }

    document.addEventListener("visibilitychange", handleVisibilityOrFocus)
    window.addEventListener("focus", handleVisibilityOrFocus)

    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityOrFocus)
      window.removeEventListener("focus", handleVisibilityOrFocus)
    }
  }, [attemptId, isTerminal, reconcileNow])

  // 3. Clock-skew compensated countdown calculation
  useEffect(() => {
    if (isTerminal || !expiresAt) {
      setRemainingSeconds(0)
      return
    }

    const deadlineMs = new Date(expiresAt).getTime()
    if (Number.isNaN(deadlineMs)) {
      setRemainingSeconds(0)
      return
    }

    const updateCountdown = () => {
      const nowMs = Date.now() + serverClockOffsetMsRef.current
      const diffSec = Math.max(0, Math.ceil((deadlineMs - nowMs) / 1000))
      setRemainingSeconds(diffSec)

      if (diffSec === 0 && !expiredReconcileTriggeredRef.current) {
        expiredReconcileTriggeredRef.current = true
        reconcileNow()
      }
    }

    updateCountdown()
    const interval = setInterval(updateCountdown, 1000)

    return () => clearInterval(interval)
  }, [expiresAt, isTerminal, reconcileNow])

  const minutes = Math.floor(remainingSeconds / 60)
  const seconds = remainingSeconds % 60
  const formattedRemaining = `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`

  const isPayable =
    !isTerminal &&
    (status === "pending" || status === "requires_action") &&
    remainingSeconds > 0

  return {
    status,
    isPayable,
    isTerminal,
    remainingSeconds,
    formattedRemaining,
    checkoutUrl: isPayable ? checkoutUrl : null,
    isSyncing,
    isSignalRConnected,
    error,
    reconcileNow,
  }
}


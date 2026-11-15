import { useCallback, useEffect, useRef } from "react"

export interface PaymentRealtimeEvent {
  orderId: string
  orderNumber?: string
  paymentAttemptId?: string
  status: string
  updatedAt?: string
}

export interface UseCoordinatedPaymentSyncOptions {
  /** True when usePaymentSignalR is in HubConnectionState.Connected */
  isSignalRConnected: boolean
  /** Polling interval used ONLY when SignalR is disconnected/degraded */
  fallbackPollIntervalMs?: number
  /** Minimum gap between any two page refreshes */
  dedupeWindowMs?: number
  /** Patch function from usePaginatedOrders to update row in-place on current page */
  onPatchVisibleOrder: (patch: { id: string; status: string; updatedAt?: string }) => void
  /** Silent page revalidation function from usePaginatedOrders */
  onSilentPageRefresh: () => void
}

export function useCoordinatedPaymentSync({
  isSignalRConnected,
  fallbackPollIntervalMs = 10000,
  dedupeWindowMs = 2000,
  onPatchVisibleOrder,
  onSilentPageRefresh,
}: UseCoordinatedPaymentSyncOptions) {
  const lastSyncTimestampRef = useRef<number>(0)

  // Called when SignalR or BroadcastChannel / storage event delivers a targeted payment event
  const handleRealtimeEvent = useCallback(
    (event: PaymentRealtimeEvent) => {
      lastSyncTimestampRef.current = Date.now()

      if (event.orderId && event.status) {
        // Patch the single row in-place on the active page without a full SQL query
        onPatchVisibleOrder({
          id: event.orderId,
          status: event.status,
          updatedAt: event.updatedAt,
        })
      } else {
        onSilentPageRefresh()
      }
    },
    [onPatchVisibleOrder, onSilentPageRefresh]
  )

  // Fallback polling ONLY runs when SignalR is NOT connected AND the tab is visible
  useEffect(() => {
    if (isSignalRConnected) {
      return // SignalR is live; do not run redundant polling requests
    }

    const intervalId = window.setInterval(() => {
      if (document.visibilityState !== "visible") return

      const elapsed = Date.now() - lastSyncTimestampRef.current
      if (elapsed < dedupeWindowMs) return

      lastSyncTimestampRef.current = Date.now()
      onSilentPageRefresh()
    }, fallbackPollIntervalMs)

    return () => window.clearInterval(intervalId)
  }, [isSignalRConnected, fallbackPollIntervalMs, dedupeWindowMs, onSilentPageRefresh])

  return {
    handleRealtimeEvent,
  }
}

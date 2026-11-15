import { useEffect, useRef, useState } from "react"
import { PaginationTimingMetrics } from "@/lib/payment-api"

export interface FullPageLifecycleMetrics extends PaginationTimingMetrics {
  reactRenderDurationMs: number
  totalEndToEndMs: number
}

export function usePaginationTelemetry(
  items: unknown[],
  apiMetrics: PaginationTimingMetrics | null,
  isFetching: boolean
) {
  const renderStartRef = useRef<number>(0)
  const [lifecycleMetrics, setLifecycleMetrics] =
    useState<FullPageLifecycleMetrics | null>(null)

  // Mark right before React commits the new items array
  if (!isFetching && apiMetrics && renderStartRef.current === 0) {
    renderStartRef.current = performance.now()
  }

  useEffect(() => {
    if (isFetching || !apiMetrics) {
      renderStartRef.current = 0
      return
    }

    const start = renderStartRef.current || performance.now()
    requestAnimationFrame(() => {
      const reactRenderDurationMs =
        Math.round((performance.now() - start) * 100) / 100
      const totalEndToEndMs =
        Math.round((apiMetrics.apiDurationMs + reactRenderDurationMs) * 100) / 100

      setLifecycleMetrics({
        ...apiMetrics,
        reactRenderDurationMs,
        totalEndToEndMs,
      })
      renderStartRef.current = 0
    })
  }, [items, apiMetrics, isFetching])

  return lifecycleMetrics
}

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import {
  listOrdersPaginated,
  PagedResponse,
  PaginationTimingMetrics,
} from "@/lib/payment-api"

export interface UsePaginatedOrdersOptions<TOrder extends { id: string; status?: string }> {
  initialPage?: number
  initialPageSize?: number
  initialStatus?: string
  initialCategory?: string
  searchDebounceMs?: number
  realtimeThrottleMs?: number
}

export function usePaginatedOrders<
  TOrder extends { id: string; orderNumber?: string; status?: string }
>(options: UsePaginatedOrdersOptions<TOrder> = {}) {
  const {
    initialPage = 1,
    initialPageSize = 20,
    initialStatus = "All",
    initialCategory = "All",
    searchDebounceMs = 300,
    realtimeThrottleMs = 1500,
  } = options

  // 1. Controlled UI filter & pagination state
  const [page, setPageState] = useState<number>(initialPage)
  const [pageSize, setPageSizeState] = useState<number>(initialPageSize)
  const [status, setStatusState] = useState<string>(initialStatus)
  const [category, setCategoryState] = useState<string>(initialCategory)
  const [searchInput, setSearchInputState] = useState<string>("")
  const [debouncedSearch, setDebouncedSearch] = useState<string>("")

  // 2. Server response state + Phase 7 telemetry
  const [pageData, setPageData] = useState<PagedResponse<TOrder>>({
    items: [],
    page: initialPage,
    pageSize: initialPageSize,
    totalCount: 0,
    totalPages: 1,
    hasNextPage: false,
    hasPreviousPage: false,
  })
  const [metrics, setMetrics] = useState<PaginationTimingMetrics | null>(null)
  const [isInitialLoading, setIsInitialLoading] = useState<boolean>(true)
  const [isFetchingPage, setIsFetchingPage] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  // 3. Concurrency, cancellation & deduplication refs
  const abortControllerRef = useRef<AbortController | null>(null)
  const requestSequenceRef = useRef<number>(0)
  const lastRealtimeRefreshAtRef = useRef<number>(0)
  const hasLoadedOnceRef = useRef<boolean>(false)

  // 4. Debounce search input AND reset page to 1 atomically when debounced value changes
  useEffect(() => {
    const trimmed = searchInput.trim()
    if (trimmed === debouncedSearch) return

    const timer = window.setTimeout(() => {
      setDebouncedSearch(trimmed)
      setPageState(1) // Reset to page 1 in the same render batch
    }, searchDebounceMs)

    return () => window.clearTimeout(timer)
  }, [searchInput, debouncedSearch, searchDebounceMs])

  // 5. Filter setters that atomically reset page to 1 (prevents double-fetch)
  const setStatus = useCallback((nextStatus: string) => {
    setStatusState((prev) => {
      if (prev !== nextStatus) setPageState(1)
      return nextStatus
    })
  }, [])

  const setCategory = useCallback((nextCategory: string) => {
    setCategoryState((prev) => {
      if (prev !== nextCategory) setPageState(1)
      return nextCategory
    })
  }, [])

  const setPageSize = useCallback((nextPageSize: number) => {
    const clamped = Math.min(100, Math.max(1, nextPageSize))
    setPageSizeState(clamped)
    setPageState(1)
  }, [])

  const setPage = useCallback((nextPage: number) => {
    setPageState(Math.max(1, nextPage))
  }, [])

  // 6. Core server fetch with AbortController & stale-response guard
  const fetchCurrentPage = useCallback(
    async (overrideParams?: { page?: number; silent?: boolean }) => {
      const targetPage = overrideParams?.page ?? page

      // Cancel any in-flight request before starting a new one
      if (abortControllerRef.current) {
        abortControllerRef.current.abort()
      }
      const controller = new AbortController()
      abortControllerRef.current = controller

      const currentRequestId = ++requestSequenceRef.current

      if (!hasLoadedOnceRef.current && !overrideParams?.silent) {
        setIsInitialLoading(true)
      } else if (!overrideParams?.silent) {
        setIsFetchingPage(true)
      }
      setError(null)

      try {
        const result = await listOrdersPaginated<TOrder>(
          {
            page: targetPage,
            pageSize,
            status,
            category,
            search: debouncedSearch,
          },
          { signal: controller.signal }
        )

        // Ignore stale out-of-order responses
        if (currentRequestId !== requestSequenceRef.current) return

        // If the current page is now beyond totalPages (e.g., after records were deleted/filtered),
        // clamp back to the last valid page.
        if (
          result.data.totalCount > 0 &&
          targetPage > result.data.totalPages
        ) {
          setPageState(result.data.totalPages)
          return
        }

        hasLoadedOnceRef.current = true
        setPageData(result.data)
        setMetrics(result.metrics)
      } catch (err: any) {
        if (err?.name === "AbortError") return
        if (currentRequestId !== requestSequenceRef.current) return
        setError(err instanceof Error ? err.message : "Failed to load orders.")
      } finally {
        if (currentRequestId === requestSequenceRef.current) {
          setIsInitialLoading(false)
          setIsFetchingPage(false)
        }
      }
    },
    [page, pageSize, status, category, debouncedSearch]
  )

  // Trigger fetch whenever controlled query parameters change
  useEffect(() => {
    void fetchCurrentPage()
    return () => {
      abortControllerRef.current?.abort()
    }
  }, [fetchCurrentPage])

  // 7. Phase 6: Real-time SignalR & Polling Coherency
  // Updates a visible row in-place without a network round-trip when safe,
  // or throttles background revalidation while preserving page & filters.
  const applyRealtimeOrderPatch = useCallback(
    (updatedOrder: Partial<TOrder> & { id: string }) => {
      let patchedInPlace = false

      setPageData((prev) => {
        const idx = prev.items.findIndex((item) => item.id === updatedOrder.id)
        if (idx === -1) return prev

        const merged = { ...prev.items[idx], ...updatedOrder }

        // If a status filter is active and the updated order no longer matches it,
        // we need a server refresh to pull the next matching record onto the page.
        if (status !== "All" && merged.status && merged.status !== status) {
          patchedInPlace = false
          return prev
        }

        patchedInPlace = true
        const nextItems = [...prev.items]
        nextItems[idx] = merged
        return { ...prev, items: nextItems }
      })

      // If the item wasn't on the current page or shifted out of the active status filter,
      // perform a throttled silent revalidation of the current page.
      if (!patchedInPlace) {
        const now = Date.now()
        if (now - lastRealtimeRefreshAtRef.current >= realtimeThrottleMs) {
          lastRealtimeRefreshAtRef.current = now
          void fetchCurrentPage({ silent: true })
        }
      }
    },
    [status, realtimeThrottleMs, fetchCurrentPage]
  )

  const throttledRealtimeRefresh = useCallback(() => {
    const now = Date.now()
    if (now - lastRealtimeRefreshAtRef.current < realtimeThrottleMs) {
      return
    }
    lastRealtimeRefreshAtRef.current = now
    void fetchCurrentPage({ silent: true })
  }, [realtimeThrottleMs, fetchCurrentPage])

  return {
    // Data & Metadata
    orders: pageData.items,
    pagination: pageData,
    metrics,
    // Loading & Error States
    isInitialLoading,
    isFetchingPage,
    error,
    // Controlled Filter & Page State
    page,
    pageSize,
    status,
    category,
    searchInput,
    debouncedSearch,
    // Actions
    setPage,
    setPageSize,
    setStatus,
    setCategory,
    setSearchInput: setSearchInputState,
    refreshPage: () => fetchCurrentPage({ silent: false }),
    applyRealtimeOrderPatch,
    throttledRealtimeRefresh,
  }
}

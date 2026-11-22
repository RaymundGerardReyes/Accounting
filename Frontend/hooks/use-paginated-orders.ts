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

  // 3. Concurrency, cancellation, deduplication & synchronous state mirror refs
  const abortControllerRef = useRef<AbortController | null>(null)
  const requestSequenceRef = useRef<number>(0)
  const lastRealtimeRefreshAtRef = useRef<number>(0)
  const hasLoadedOnceRef = useRef<boolean>(false)

  // Synchronous mirror refs to guarantee instant, race-free lookups during renders and events
  const pageDataRef = useRef<PagedResponse<TOrder>>(pageData)
  pageDataRef.current = pageData

  const statusRef = useRef<string>(status)
  statusRef.current = status

  const categoryRef = useRef<string>(category)
  categoryRef.current = category

  const pageSizeRef = useRef<number>(pageSize)
  pageSizeRef.current = pageSize

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

  // 5. Filter setters that atomically reset page to 1 (prevents double-fetch) using ref mirrors
  const setStatus = useCallback((nextStatus: string) => {
    if (statusRef.current !== nextStatus) {
      statusRef.current = nextStatus
      setStatusState(nextStatus)
      setPageState(1)
    }
  }, [])

  const setCategory = useCallback((nextCategory: string) => {
    if (categoryRef.current !== nextCategory) {
      categoryRef.current = nextCategory
      setCategoryState(nextCategory)
      setPageState(1)
    }
  }, [])

  const setPageSize = useCallback((nextPageSize: number) => {
    const clamped = Math.min(100, Math.max(1, nextPageSize))
    if (pageSizeRef.current !== clamped) {
      pageSizeRef.current = clamped
      setPageSizeState(clamped)
      setPageState(1)
    }
  }, [])

  const setPage = useCallback((nextPage: number) => {
    setPageState(Math.max(1, nextPage))
  }, [])

  // 6. Core server fetch with AbortController & stale-response guard
  const fetchCurrentPage = useCallback(
    async (overrideParams?: { page?: number; silent?: boolean }) => {
      const targetPage = Math.max(1, overrideParams?.page ?? page)

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

        // ONLY clamp back if this page came back EMPTY and is genuinely past totalPages
        // (e.g., rows on the last page were deleted). Never clamp away from a page with real items!
        const safeTotalPages = Math.max(1, result.data.totalPages || 1)
        if (
          result.data.items.length === 0 &&
          result.data.totalCount > 0 &&
          targetPage > safeTotalPages
        ) {
          setPageState(safeTotalPages)
          return
        }

        hasLoadedOnceRef.current = true
        pageDataRef.current = result.data
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
  // Updates a visible row in-place without a network round-trip using synchronous pageDataRef,
  // or throttles background revalidation while preserving page & filters.
  const applyRealtimeOrderPatch = useCallback(
    (updatedOrder: Partial<TOrder> & { id: string }) => {
      const currentItems = pageDataRef.current.items
      const idx = currentItems.findIndex((item) => item.id === updatedOrder.id)

      // If the item wasn't on the current page, perform throttled revalidation
      if (idx === -1) {
        const now = Date.now()
        if (now - lastRealtimeRefreshAtRef.current >= realtimeThrottleMs) {
          lastRealtimeRefreshAtRef.current = now
          void fetchCurrentPage({ silent: true })
        }
        return
      }

      const merged = { ...currentItems[idx], ...updatedOrder }

      // If a status filter is active and the updated order no longer matches it,
      // trigger background revalidation to pull in the next matching record.
      if (statusRef.current !== "All" && merged.status && merged.status !== statusRef.current) {
        const now = Date.now()
        if (now - lastRealtimeRefreshAtRef.current >= realtimeThrottleMs) {
          lastRealtimeRefreshAtRef.current = now
          void fetchCurrentPage({ silent: true })
        }
        return
      }

      // Valid match on current page: patch in place without any network fetch
      const nextItems = [...currentItems]
      nextItems[idx] = merged
      const nextData: PagedResponse<TOrder> = { ...pageDataRef.current, items: nextItems }
      pageDataRef.current = nextData
      setPageData(nextData)
    },
    [realtimeThrottleMs, fetchCurrentPage]
  )

  const throttledRealtimeRefresh = useCallback(() => {
    const now = Date.now()
    if (now - lastRealtimeRefreshAtRef.current < realtimeThrottleMs) {
      return
    }
    lastRealtimeRefreshAtRef.current = now
    void fetchCurrentPage({ silent: true })
  }, [realtimeThrottleMs, fetchCurrentPage])

  const refreshPage = useCallback(() => {
    void fetchCurrentPage({ silent: false })
  }, [fetchCurrentPage])

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
    refreshPage,
    applyRealtimeOrderPatch,
    throttledRealtimeRefresh,
  }
}

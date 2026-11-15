import { useCallback, useEffect, useRef, useState } from "react"
import { apiFetchWithMetrics, ApiRequestMetrics } from "@/lib/api-client"

export interface KeysetPagePayload<T> {
  items: T[]
  nextCursor: string | null
  hasMore: boolean
}

export interface KeysetFilterState {
  status?: string
  search?: string
  [key: string]: string | undefined
}

export interface UseKeysetPaginatedResourceOptions<TFilter extends KeysetFilterState> {
  endpoint: string
  initialLimit?: number
  initialFilters?: TFilter
  searchDebounceMs?: number
}

export function normalizeKeysetResponse<T>(
  raw: unknown,
  requestedLimit: number
): KeysetPagePayload<T> {
  if (raw && typeof raw === "object" && "items" in raw && Array.isArray((raw as any).items)) {
    const obj = raw as Record<string, unknown>
    const items = obj.items as T[]
    const nextCursor =
      typeof obj.nextCursor === "string" && obj.nextCursor.length > 0
        ? obj.nextCursor
        : null
    const hasMore =
      typeof obj.hasMore === "boolean"
        ? obj.hasMore
        : typeof obj.hasNextPage === "boolean"
          ? obj.hasNextPage
          : nextCursor !== null

    return { items, nextCursor, hasMore }
  }

  // Backward-compatible fallback if backend endpoint still returns a flat array
  const flat: T[] = Array.isArray(raw) ? raw : []
  return {
    items: flat.slice(0, requestedLimit),
    nextCursor: null,
    hasMore: false,
  }
}

export function useKeysetPaginatedResource<
  TItem,
  TFilter extends KeysetFilterState = KeysetFilterState
>({
  endpoint,
  initialLimit = 20,
  initialFilters = {} as TFilter,
  searchDebounceMs = 300,
}: UseKeysetPaginatedResourceOptions<TFilter>) {
  const [limit, setLimitState] = useState<number>(
    Math.min(100, Math.max(1, initialLimit))
  )
  const [filters, setFiltersState] = useState<TFilter>(initialFilters)
  const [searchInput, setSearchInput] = useState<string>(initialFilters.search ?? "")
  const [debouncedSearch, setDebouncedSearch] = useState<string>(initialFilters.search ?? "")

  // Cursor state: currentCursor is sent to the API; cursorHistory holds prior cursors for "Previous"
  const [currentCursor, setCurrentCursor] = useState<string | null>(null)
  const [cursorHistory, setCursorHistory] = useState<Array<string | null>>([])

  const [data, setData] = useState<KeysetPagePayload<TItem>>({
    items: [],
    nextCursor: null,
    hasMore: false,
  })
  const [metrics, setMetrics] = useState<ApiRequestMetrics | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [isFetching, setIsFetching] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const abortRef = useRef<AbortController | null>(null)
  const requestSeqRef = useRef<number>(0)
  const hasLoadedRef = useRef<boolean>(false)

  // Debounce search & reset cursor stack
  useEffect(() => {
    const trimmed = searchInput.trim()
    if (trimmed === debouncedSearch) return

    const timer = window.setTimeout(() => {
      setDebouncedSearch(trimmed)
      setCurrentCursor(null)
      setCursorHistory([])
    }, searchDebounceMs)

    return () => window.clearTimeout(timer)
  }, [searchInput, debouncedSearch, searchDebounceMs])

  const updateFilter = useCallback(<K extends keyof TFilter>(key: K, value: TFilter[K]) => {
    setFiltersState((prev) => {
      if (prev[key] === value) return prev
      setCurrentCursor(null)
      setCursorHistory([])
      return { ...prev, [key]: value }
    })
  }, [])

  const setLimit = useCallback((nextLimit: number) => {
    const clamped = Math.min(100, Math.max(1, nextLimit))
    setLimitState(clamped)
    setCurrentCursor(null)
    setCursorHistory([])
  }, [])

  const fetchKeysetPage = useCallback(
    async (options?: { silent?: boolean }) => {
      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller
      const reqId = ++requestSeqRef.current

      if (!hasLoadedRef.current && !options?.silent) {
        setIsLoading(true)
      } else if (!options?.silent) {
        setIsFetching(true)
      }
      setError(null)

      try {
        const qs = new URLSearchParams()
        qs.set("limit", String(limit))
        if (currentCursor) qs.set("cursor", currentCursor)
        if (debouncedSearch) qs.set("search", debouncedSearch)

        Object.entries(filters).forEach(([k, v]) => {
          if (k !== "search" && v && v !== "All") {
            qs.set(k, v)
          }
        });

        const res = await apiFetchWithMetrics<unknown>(`${endpoint}?${qs.toString()}`, {
          signal: controller.signal,
        })

        if (reqId !== requestSeqRef.current) return

        hasLoadedRef.current = true
        setData(normalizeKeysetResponse<TItem>(res.data, limit))
        setMetrics(res.metrics)
      } catch (err: any) {
        if (err?.name === "AbortError") return
        if (reqId !== requestSeqRef.current) return
        setError(err instanceof Error ? err.message : "Failed to load log records.")
      } finally {
        if (reqId === requestSeqRef.current) {
          setIsLoading(false)
          setIsFetching(false)
        }
      }
    },
    [endpoint, limit, currentCursor, debouncedSearch, filters]
  )

  useEffect(() => {
    void fetchKeysetPage()
    return () => abortRef.current?.abort()
  }, [fetchKeysetPage])

  const goToNextCursor = useCallback(() => {
    if (!data.hasMore || !data.nextCursor || isFetching) return
    setCursorHistory((prev) => [...prev, currentCursor])
    setCurrentCursor(data.nextCursor)
  }, [data.hasMore, data.nextCursor, isFetching, currentCursor])

  const goToPreviousCursor = useCallback(() => {
    if (cursorHistory.length === 0 || isFetching) return
    const nextHistory = [...cursorHistory]
    const previousCursor = nextHistory.pop() ?? null
    setCursorHistory(nextHistory)
    setCurrentCursor(previousCursor)
  }, [cursorHistory, isFetching])

  const resetToFirstPage = useCallback(() => {
    setCurrentCursor(null)
    setCursorHistory([])
  }, [])

  return {
    items: data.items,
    hasMore: data.hasMore,
    nextCursor: data.nextCursor,
    hasPrevious: cursorHistory.length > 0,
    pageIndex: cursorHistory.length + 1,
    limit,
    filters,
    searchInput,
    metrics,
    isLoading,
    isFetching,
    error,
    setSearchInput,
    updateFilter,
    setLimit,
    goToNextCursor,
    goToPreviousCursor,
    resetToFirstPage,
    refresh: () => fetchKeysetPage({ silent: false }),
  }
}

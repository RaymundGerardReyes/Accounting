import { useCallback, useEffect, useRef, useState } from "react"
import {
  FinanceResourceKey,
  listFinanceResourcePaginated,
} from "@/lib/finance-api"
import { PagedResponse, PaginationTimingMetrics } from "@/lib/payment-api"

export interface TabPaginationState {
  page: number
  pageSize: number
  status: string
  search: string
}

const DEFAULT_TAB_STATE: TabPaginationState = {
  page: 1,
  pageSize: 20,
  status: "All",
  search: "",
}

export function useFinanceTabPagination<TItem = any>(
  activeTab: FinanceResourceKey,
  searchDebounceMs = 300
) {
  const [tabStates, setTabStates] = useState<Record<string, TabPaginationState>>({})
  const [tabResults, setTabResults] = useState<Record<string, PagedResponse<TItem>>>({})
  const [tabMetrics, setTabMetrics] = useState<Record<string, PaginationTimingMetrics>>({})
  const [isLoading, setIsLoading] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const currentTabState = tabStates[activeTab] ?? DEFAULT_TAB_STATE
  const [searchInput, setSearchInput] = useState<string>(currentTabState.search)

  const abortRef = useRef<AbortController | null>(null)
  const seqRef = useRef<number>(0)

  // Sync local search input box when user switches tabs
  useEffect(() => {
    setSearchInput((tabStates[activeTab] ?? DEFAULT_TAB_STATE).search)
  }, [activeTab])

  // Debounce search input for the active tab and reset its page to 1
  useEffect(() => {
    const trimmed = searchInput.trim()
    if (trimmed === currentTabState.search) return

    const timer = window.setTimeout(() => {
      setTabStates((prev) => ({
        ...prev,
        [activeTab]: {
          ...(prev[activeTab] ?? DEFAULT_TAB_STATE),
          search: trimmed,
          page: 1,
        },
      }))
    }, searchDebounceMs)

    return () => window.clearTimeout(timer)
  }, [searchInput, activeTab, currentTabState.search, searchDebounceMs])

  const fetchActiveTab = useCallback(async () => {
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    const reqId = ++seqRef.current

    setIsLoading(true)
    setError(null)

    try {
      const result = await listFinanceResourcePaginated<TItem>(
        activeTab,
        {
          page: currentTabState.page,
          pageSize: currentTabState.pageSize,
          status: currentTabState.status,
          search: currentTabState.search,
        },
        { signal: controller.signal }
      )

      if (reqId !== seqRef.current) return

      setTabResults((prev) => ({ ...prev, [activeTab]: result.data }))
      setTabMetrics((prev) => ({ ...prev, [activeTab]: result.metrics }))
    } catch (err: any) {
      if (err?.name === "AbortError") return
      if (reqId !== seqRef.current) return
      setError(err instanceof Error ? err.message : `Failed to load ${activeTab}`)
    } finally {
      if (reqId === seqRef.current) {
        setIsLoading(false)
      }
    }
  }, [
    activeTab,
    currentTabState.page,
    currentTabState.pageSize,
    currentTabState.status,
    currentTabState.search,
  ])

  useEffect(() => {
    void fetchActiveTab()
    return () => abortRef.current?.abort()
  }, [fetchActiveTab])

  const setPage = useCallback(
    (nextPage: number) => {
      setTabStates((prev) => ({
        ...prev,
        [activeTab]: {
          ...(prev[activeTab] ?? DEFAULT_TAB_STATE),
          page: Math.max(1, nextPage),
        },
      }))
    },
    [activeTab]
  )

  const setPageSize = useCallback(
    (nextPageSize: number) => {
      setTabStates((prev) => ({
        ...prev,
        [activeTab]: {
          ...(prev[activeTab] ?? DEFAULT_TAB_STATE),
          pageSize: Math.min(100, Math.max(1, nextPageSize)),
          page: 1,
        },
      }))
    },
    [activeTab]
  )

  const setStatus = useCallback(
    (nextStatus: string) => {
      setTabStates((prev) => ({
        ...prev,
        [activeTab]: {
          ...(prev[activeTab] ?? DEFAULT_TAB_STATE),
          status: nextStatus,
          page: 1,
        },
      }))
    },
    [activeTab]
  )

  const activeData = tabResults[activeTab] ?? {
    items: [],
    page: currentTabState.page,
    pageSize: currentTabState.pageSize,
    totalCount: 0,
    totalPages: 1,
    hasNextPage: false,
    hasPreviousPage: false,
  }

  return {
    items: activeData.items,
    pagination: activeData,
    metrics: tabMetrics[activeTab] ?? null,
    isLoading,
    error,
    page: currentTabState.page,
    pageSize: currentTabState.pageSize,
    status: currentTabState.status,
    searchInput,
    setPage,
    setPageSize,
    setStatus,
    setSearchInput,
    refreshActiveTab: fetchActiveTab,
  }
}

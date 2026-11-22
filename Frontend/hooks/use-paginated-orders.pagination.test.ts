import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { renderHook, act, waitFor } from "@testing-library/react"
import {
  normalizePagedResponse,
  buildOrdersQueryString,
  type Order,
} from "@/lib/payment-api"
import { buildPaginationTokens } from "@/components/payment/ServerPaginationBar"
import { usePaginatedOrders } from "@/hooks/use-paginated-orders"

/**
 * Helper matching the exact concrete Order schema from your codebase.
 */
function createMockOrder(index: number, status: Order["status"] = "pending"): Order {
  const num = String(index).padStart(3, "0")
  return {
    id: `order-${num}`,
    user_id: `user-${num}`,
    order_number: `ORD-2026-${num}`,
    title: `Order Batch #${num}`,
    category: index % 2 === 0 ? "department_invoice" : "product_order",
    reference_code: `REF-${num}`,
    amount_minor: 10000 * index,
    amount: 100 * index,
    currency: "PHP",
    status,
    metadata: {
      customer_name: `Customer ${num}`,
      customer_phone: "+639171234567",
    },
    created_at: `2026-11-03T10:${String(index % 60).padStart(2, "0")}:00Z`,
    updated_at: `2026-11-03T10:${String(index % 60).padStart(2, "0")}:00Z`,
  }
}

function createOrderBatch(count: number, startIndex = 1): Order[] {
  return Array.from({ length: count }, (_, i) =>
    createMockOrder(startIndex + i, (startIndex + i) % 3 === 0 ? "paid" : "pending")
  )
}

function makeJsonResponse(payload: unknown, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" },
  })
}

function jsonOk(payload: unknown) {
  return Promise.resolve(makeJsonResponse(payload))
}

describe("Concrete Pagination Unit & Helper Contracts", () => {
  it.each([
    [
      "camelCase contract",
      {
        items: createOrderBatch(10, 11),
        page: 2,
        pageSize: 10,
        totalCount: 28,
        totalPages: 3,
        hasNextPage: true,
        hasPreviousPage: true,
      },
      2,
      10,
      { page: 2, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: true, hasPreviousPage: true, len: 10 },
    ],
    [
      "Heroku snake_case contract (original Page 2/3 bug shape)",
      {
        items: createOrderBatch(10, 11),
        page: 2,
        page_size: 10,
        total_count: 28,
        total_pages: 3,
        has_next_page: true,
        has_previous_page: true,
      },
      2,
      10,
      { page: 2, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: true, hasPreviousPage: true, len: 10 },
    ],
    [
      "PascalCase .NET contract",
      {
        Items: createOrderBatch(8, 21),
        Page: 3,
        PageSize: 10,
        TotalCount: 28,
        TotalPages: 3,
        HasNextPage: false,
        HasPreviousPage: true,
      },
      3,
      10,
      { page: 3, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: false, hasPreviousPage: true, len: 8 },
    ],
    [
      "nested pagination wrapper",
      {
        items: createOrderBatch(10, 11),
        pagination: { page: 2, page_size: 10, total_count: 28, total_pages: 3 },
      },
      2,
      10,
      { page: 2, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: true, hasPreviousPage: true, len: 10 },
    ],
    [
      "nested meta wrapper + numeric strings",
      {
        items: createOrderBatch(10, 11),
        meta: { current_page: "2", limit: "10", total: "28" },
      },
      2,
      10,
      { page: 2, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: true, hasPreviousPage: true, len: 10 },
    ],
    [
      "missing total_count on Page 2 infers minImpliedTotal=20",
      { items: createOrderBatch(10, 11) },
      2,
      10,
      { page: 2, pageSize: 10, totalCount: 20, totalPages: 2, hasNextPage: false, hasPreviousPage: true, len: 10 },
    ],
    [
      "under-reported total_pages=1 overridden by ceil(28/10)=3",
      { items: createOrderBatch(10, 11), total_count: 28, total_pages: 1 },
      2,
      10,
      { page: 2, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: true, hasPreviousPage: true, len: 10 },
    ],
    [
      "legacy raw array fallback slices Page 3 (8 items)",
      createOrderBatch(28, 1),
      3,
      10,
      { page: 3, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: false, hasPreviousPage: true, len: 8 },
    ],
    [
      "legacy { orders } wrapper fallback slices Page 2",
      { orders: createOrderBatch(28, 1) },
      2,
      10,
      { page: 2, pageSize: 10, totalCount: 28, totalPages: 3, hasNextPage: true, hasPreviousPage: true, len: 10 },
    ],
    [
      "null payload safely falls back to empty page 1 of 1",
      null,
      1,
      10,
      { page: 1, pageSize: 10, totalCount: 0, totalPages: 1, hasNextPage: false, hasPreviousPage: false, len: 0 },
    ],
  ])("normalizePagedResponse handles %s", (_label, raw, reqPage, reqSize, expected) => {
    const res = normalizePagedResponse<Order>(raw, reqPage, reqSize)
    expect(res.page).toBe(expected.page)
    expect(res.pageSize).toBe(expected.pageSize)
    expect(res.totalCount).toBe(expected.totalCount)
    expect(res.totalPages).toBe(expected.totalPages)
    expect(res.hasNextPage).toBe(expected.hasNextPage)
    expect(res.hasPreviousPage).toBe(expected.hasPreviousPage)
    expect(res.items).toHaveLength(expected.len)
  })

  it("buildOrdersQueryString clamps pageSize and trims search cleanly", () => {
    expect(
      buildOrdersQueryString({
        page: 3,
        pageSize: 250,
        status: "All",
        search: "  ORD-2026-021  ",
      })
    ).toBe("page=3&pageSize=100&search=ORD-2026-021")
  })

  it("buildPaginationTokens returns exact page buttons for 3 pages and deep ellipsis", () => {
    expect(buildPaginationTokens(2, 3)).toEqual([1, 2, 3])
    expect(buildPaginationTokens(5, 20)).toEqual([
      1,
      "ellipsis-start",
      4,
      5,
      6,
      "ellipsis-end",
      20,
    ])
  })
})

describe("Concrete Hook Integration & Snap-Back Regression: usePaginatedOrders", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("REGRESSION: Page 1 -> Page 2 -> Page 3 -> Page 2 with snake_case NEVER snaps back to Page 1", async () => {
    const all28 = createOrderBatch(28, 1)
    const requestedPages: number[] = []

    global.fetch = vi.fn().mockImplementation((input: string) => {
      const parsed = new URL(String(input), "http://localhost")
      const page = Number(parsed.searchParams.get("page") ?? 1)
      const pageSize = Number(parsed.searchParams.get("pageSize") ?? 10)
      requestedPages.push(page)

      const start = (page - 1) * pageSize
      return jsonOk({
        items: all28.slice(start, start + pageSize),
        page,
        page_size: pageSize,
        total_count: all28.length,
        total_pages: 3,
        has_next_page: page < 3,
        has_previous_page: page > 1,
      })
    }) as any

    const { result } = renderHook(() =>
      usePaginatedOrders<Order>({
        initialPage: 1,
        initialPageSize: 10,
        initialStatus: "All",
        searchDebounceMs: 50,
      })
    )

    await waitFor(() => expect(result.current.orders).toHaveLength(10))
    expect(result.current.pagination.totalCount).toBe(28)
    expect(result.current.pagination.totalPages).toBe(3)

    // Move to Page 2
    act(() => result.current.setPage(2))
    await waitFor(() => expect(result.current.orders[0]?.order_number).toBe("ORD-2026-011"))
    expect(result.current.page).toBe(2)

    // Move to Page 3
    act(() => result.current.setPage(3))
    await waitFor(() => expect(result.current.orders).toHaveLength(8))
    expect(result.current.orders[0]?.order_number).toBe("ORD-2026-021")

    // Wait >300ms to confirm ZERO delayed snap-back to Page 1
    await new Promise((r) => setTimeout(r, 350))
    expect(result.current.page).toBe(3)

    // Move back to Page 2
    act(() => result.current.setPage(2))
    await waitFor(() => expect(result.current.orders[0]?.order_number).toBe("ORD-2026-011"))

    expect(requestedPages).toEqual([1, 2, 3, 2])
  })

  it("NON-DESTRUCTIVE CLAMP: clamps to Page 2 ONLY when Page 3 returns items: []", async () => {
    const requestedPages: number[] = []
    const remaining20 = createOrderBatch(20, 1)

    global.fetch = vi.fn().mockImplementation((input: string) => {
      const page = Number(new URL(String(input), "http://localhost").searchParams.get("page") ?? 1)
      requestedPages.push(page)

      const slice = page <= 2 ? remaining20.slice((page - 1) * 10, page * 10) : []
      return jsonOk({
        items: slice,
        page,
        page_size: 10,
        total_count: 20,
        total_pages: 2,
        has_next_page: page < 2,
        has_previous_page: page > 1,
      })
    }) as any

    const { result } = renderHook(() =>
      usePaginatedOrders<Order>({ initialPage: 1, initialPageSize: 10 })
    )
    await waitFor(() => expect(result.current.orders).toHaveLength(10))

    // User requests Page 3 after all Page 3 rows were deleted -> should clamp cleanly to Page 2
    act(() => result.current.setPage(3))
    await waitFor(() => expect(result.current.page).toBe(2))
    expect(result.current.orders[0]?.order_number).toBe("ORD-2026-011")
    expect(requestedPages).toEqual([1, 3, 2])
  })

  it("REFRESH SYNC: refreshPage() reloads active Page 2 without jumping to Page 1", async () => {
    const requestedPages: number[] = []
    const all28 = createOrderBatch(28, 1)

    global.fetch = vi.fn().mockImplementation((input: string) => {
      const page = Number(new URL(String(input), "http://localhost").searchParams.get("page") ?? 1)
      requestedPages.push(page)
      return jsonOk({
        items: all28.slice((page - 1) * 10, page * 10),
        page,
        page_size: 10,
        total_count: 28,
        total_pages: 3,
      })
    }) as any

    const { result } = renderHook(() =>
      usePaginatedOrders<Order>({ initialPage: 1, initialPageSize: 10 })
    )
    await waitFor(() => expect(result.current.orders).toHaveLength(10))

    act(() => result.current.setPage(2))
    await waitFor(() => expect(result.current.page).toBe(2))

    await act(async () => {
      await result.current.refreshPage()
    })

    expect(result.current.page).toBe(2)
    expect(requestedPages).toEqual([1, 2, 2])
  })

  it("STALE RESPONSE GUARD: slow Page 2 response arriving after Page 3 is ignored", async () => {
    const all28 = createOrderBatch(28, 1)
    let resolvePage2!: (res: any) => void
    let resolvePage3!: (res: any) => void

    global.fetch = vi.fn().mockImplementation((input: string) => {
      const page = Number(new URL(String(input), "http://localhost").searchParams.get("page") ?? 1)
      if (page === 1) {
        return jsonOk({ items: all28.slice(0, 10), page: 1, page_size: 10, total_count: 28, total_pages: 3 })
      }
      if (page === 2) {
        return new Promise((r) => { resolvePage2 = r })
      }
      return new Promise((r) => { resolvePage3 = r })
    }) as any

    const { result } = renderHook(() =>
      usePaginatedOrders<Order>({ initialPage: 1, initialPageSize: 10 })
    )
    await waitFor(() => expect(result.current.orders).toHaveLength(10))

    act(() => result.current.setPage(2))
    act(() => result.current.setPage(3))

    // Page 3 settles first
    await act(async () => {
      resolvePage3(
        makeJsonResponse({
          items: all28.slice(20, 28),
          page: 3,
          page_size: 10,
          total_count: 28,
          total_pages: 3,
        })
      )
    })
    await waitFor(() => expect(result.current.orders).toHaveLength(8))

    // Stale Page 2 settles late -> must NOT overwrite Page 3
    await act(async () => {
      resolvePage2(
        makeJsonResponse({
          items: all28.slice(10, 20),
          page: 2,
          page_size: 10,
          total_count: 28,
          total_pages: 3,
        })
      )
    })
    expect(result.current.page).toBe(3)
    expect(result.current.orders[0]?.order_number).toBe("ORD-2026-021")
  })
})

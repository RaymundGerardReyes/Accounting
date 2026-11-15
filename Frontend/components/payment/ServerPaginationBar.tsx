import React, { useMemo } from "react"
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"

export interface ServerPaginationBarProps {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
  isFetching?: boolean
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
  pageSizeOptions?: number[]
}

export type PageToken = number | "ellipsis-start" | "ellipsis-end"

export function buildPaginationTokens(currentPage: number, totalPages: number): PageToken[] {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, i) => i + 1)
  }

  const tokens: PageToken[] = [1]
  const leftSibling = Math.max(2, currentPage - 1)
  const rightSibling = Math.min(totalPages - 1, currentPage + 1)

  if (leftSibling > 2) {
    tokens.push("ellipsis-start")
  } else if (leftSibling === 2) {
    tokens.push(2)
  }

  for (let p = leftSibling; p <= rightSibling; p++) {
    if (p !== 1 && p !== totalPages && !tokens.includes(p)) {
      tokens.push(p)
    }
  }

  if (rightSibling < totalPages - 1) {
    tokens.push("ellipsis-end")
  } else if (rightSibling === totalPages - 1 && !tokens.includes(totalPages - 1)) {
    tokens.push(totalPages - 1)
  }

  tokens.push(totalPages)
  return tokens
}

export function ServerPaginationBar({
  page,
  pageSize,
  totalCount,
  totalPages,
  hasNextPage,
  hasPreviousPage,
  isFetching = false,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = [10, 20, 50, 100],
}: ServerPaginationBarProps) {
  const tokens = useMemo(() => buildPaginationTokens(page, totalPages), [page, totalPages])

  const firstRecordIndex = totalCount === 0 ? 0 : (page - 1) * pageSize + 1
  const lastRecordIndex = Math.min(totalCount, page * pageSize)

  return (
    <div
      className="flex flex-col gap-3 border-t pt-4 sm:flex-row sm:items-center sm:justify-between"
      data-testid="server-pagination-bar"
    >
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <span>
          Showing <strong className="font-medium text-foreground">{firstRecordIndex}</strong>–
          <strong className="font-medium text-foreground">{lastRecordIndex}</strong> of{" "}
          <strong className="font-medium text-foreground">{totalCount}</strong> records
        </span>

        {onPageSizeChange && (
          <div className="flex items-center gap-2">
            <span className="hidden md:inline">Rows per page:</span>
            <Select
              value={String(pageSize)}
              onValueChange={(val) => onPageSizeChange(Number(val))}
              disabled={isFetching}
            >
              <SelectTrigger className="h-8 w-[72px]" aria-label="Rows per page">
                <SelectValue placeholder={String(pageSize)} />
              </SelectTrigger>
              <SelectContent>
                {pageSizeOptions.map((opt) => (
                  <SelectItem key={opt} value={String(opt)}>
                    {opt}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}
      </div>

      <Pagination className="mx-0 w-auto justify-end">
        <PaginationContent>
          <PaginationItem>
            <PaginationPrevious
              href="#"
              onClick={(e) => {
                e.preventDefault()
                if (hasPreviousPage && !isFetching) onPageChange(page - 1)
              }}
              aria-disabled={!hasPreviousPage || isFetching}
              className={!hasPreviousPage || isFetching ? "pointer-events-none opacity-50" : "cursor-pointer"}
            />
          </PaginationItem>

          {tokens.map((token) =>
            typeof token === "number" ? (
              <PaginationItem key={token}>
                <PaginationLink
                  href="#"
                  isActive={token === page}
                  onClick={(e) => {
                    e.preventDefault()
                    if (token !== page && !isFetching) onPageChange(token)
                  }}
                  className="cursor-pointer"
                >
                  {token}
                </PaginationLink>
              </PaginationItem>
            ) : (
              <PaginationItem key={token}>
                <PaginationEllipsis />
              </PaginationItem>
            )
          )}

          <PaginationItem>
            <PaginationNext
              href="#"
              onClick={(e) => {
                e.preventDefault()
                if (hasNextPage && !isFetching) onPageChange(page + 1)
              }}
              aria-disabled={!hasNextPage || isFetching}
              className={!hasNextPage || isFetching ? "pointer-events-none opacity-50" : "cursor-pointer"}
            />
          </PaginationItem>
        </PaginationContent>
      </Pagination>
    </div>
  )
}

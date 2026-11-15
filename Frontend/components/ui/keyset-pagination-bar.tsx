import React from "react"
import {
  Pagination,
  PaginationContent,
  PaginationItem,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination"
import { Button } from "@/components/ui/button"

export interface KeysetPaginationBarProps {
  pageIndex: number
  itemCount: number
  limit: number
  hasPrevious: boolean
  hasMore: boolean
  isFetching?: boolean
  onPrevious: () => void
  onNext: () => void
  onResetFirst?: () => void
}

export function KeysetPaginationBar({
  pageIndex,
  itemCount,
  limit,
  hasPrevious,
  hasMore,
  isFetching = false,
  onPrevious,
  onNext,
  onResetFirst,
}: KeysetPaginationBarProps) {
  return (
    <div
      className="flex flex-col gap-3 border-t pt-4 sm:flex-row sm:items-center sm:justify-between"
      data-testid="keyset-pagination-bar"
    >
      <div className="flex items-center gap-3 text-sm text-muted-foreground">
        <span>
          Batch <strong className="font-medium text-foreground">{pageIndex}</strong>{" "}
          ({itemCount} of {limit} max rows)
        </span>
        {hasPrevious && onResetFirst && (
          <Button
            variant="ghost"
            size="sm"
            onClick={onResetFirst}
            disabled={isFetching}
            className="h-7 px-2 text-xs"
          >
            Jump to Newest
          </Button>
        )}
      </div>

      <Pagination className="mx-0 w-auto justify-end">
        <PaginationContent>
          <PaginationItem>
            <PaginationPrevious
              href="#"
              onClick={(e) => {
                e.preventDefault()
                if (hasPrevious && !isFetching) onPrevious()
              }}
              aria-disabled={!hasPrevious || isFetching}
              className={!hasPrevious || isFetching ? "pointer-events-none opacity-50" : "cursor-pointer"}
            />
          </PaginationItem>
          <PaginationItem>
            <PaginationNext
              href="#"
              onClick={(e) => {
                e.preventDefault()
                if (hasMore && !isFetching) onNext()
              }}
              aria-disabled={!hasMore || isFetching}
              className={!hasMore || isFetching ? "pointer-events-none opacity-50" : "cursor-pointer"}
            />
          </PaginationItem>
        </PaginationContent>
      </Pagination>
    </div>
  )
}

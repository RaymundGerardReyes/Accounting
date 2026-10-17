"use client"

import * as React from "react"
import { cn } from "@/lib/utils"
import { Badge } from "@/components/ui/badge"
import {
  CheckCircle2,
  AlertTriangle,
  XCircle,
  Clock,
  FileEdit,
  ShieldAlert,
  TrendingUp,
  TrendingDown,
  Minus,
} from "lucide-react"

// ============================================================================
// 1. MONETARY AMOUNT DISPLAY (Supports Major & Minor Units Safely)
// ============================================================================

export interface CurrencyDisplayProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Pass either `amount` (major units, e.g. 1250.50) OR `amountMinor` (cents, e.g. 125050) */
  amount?: number | null
  amountMinor?: number | null
  currency?: string
  locale?: string
  /** Show explicit + or () for variance/accounting tables */
  accountingFormat?: boolean
  /** Fallback text when null or undefined */
  fallback?: string
  size?: "sm" | "md" | "lg" | "xl"
}

export function CurrencyDisplay({
  amount,
  amountMinor,
  currency = "USD",
  locale,
  accountingFormat = false,
  fallback = "—",
  size = "md",
  className,
  ...props
}: CurrencyDisplayProps) {
  // Safely resolve minor or major units without mutating precision
  const resolvedValue = React.useMemo(() => {
    if (typeof amountMinor === "number" && !Number.isNaN(amountMinor)) {
      return amountMinor / 100
    }
    if (typeof amountMinor === "string" && (amountMinor as string).trim() !== "" && !Number.isNaN(Number(amountMinor))) {
      return Number(amountMinor) / 100
    }
    if (typeof amount === "number" && !Number.isNaN(amount)) {
      return amount
    }
    if (typeof amount === "string" && (amount as string).trim() !== "" && !Number.isNaN(Number(amount))) {
      return Number(amount)
    }
    return null
  }, [amount, amountMinor])

  if (resolvedValue === null) {
    return (
      <span
        className={cn("font-mono tabular-nums text-muted-foreground", className)}
        {...props}
      >
        {fallback}
      </span>
    )
  }

  const resolvedLocale = locale || (currency === "PHP" ? "en-PH" : "en-US")
  const isNegative = resolvedValue < 0
  const absValue = Math.abs(resolvedValue)

  const formatted = new Intl.NumberFormat(resolvedLocale, {
    style: "currency",
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(absValue)

  const displayString = accountingFormat
    ? isNegative
      ? `(${formatted})`
      : formatted
    : isNegative
      ? `-${formatted}`
      : formatted

  const sizeClasses = {
    sm: "text-xs",
    md: "text-sm",
    lg: "text-lg font-semibold",
    xl: "text-2xl font-bold tracking-tight",
  }

  return (
    <span
      className={cn(
        "font-mono tabular-nums tracking-tight whitespace-nowrap",
        sizeClasses[size],
        isNegative && accountingFormat ? "text-secondary font-medium" : "text-foreground",
        className
      )}
      {...props}
    >
      {displayString}
    </span>
  )
}

// ============================================================================
// 2. SEMANTIC FINANCIAL STATUS BADGE (Never relies on color alone)
// ============================================================================

export type FinancialStatusType =
  | "active"
  | "approved"
  | "favorable"
  | "succeeded"
  | "paid"
  | "reconciled"
  | "closed"
  | "released"
  | "executed"
  | "pending"
  | "pending_approval"
  | "processing"
  | "submitted"
  | "under_review"
  | "liquidating"
  | "open"
  | "partial"
  | "in_progress"
  | "verified"
  | "soft_warn"
  | "exception_eligible"
  | "draft"
  | "neutral"
  | "retired"
  | "rejected"
  | "unfavorable"
  | "hard_block"
  | "failed"
  | "overdue"
  | "cancelled"
  | "expired"
  | string

export interface StatusBadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  status: FinancialStatusType
  label?: string
}

export function FinancialStatusBadge({
  status,
  label,
  className,
  ...props
}: StatusBadgeProps) {
  const normalized = status?.toLowerCase().trim() || "draft"
  const displayLabel =
    label ||
    normalized
      .replace(/_/g, " ")
      .replace(/\b\w/g, (char) => char.toUpperCase())

  // Uses strictly the existing Black/Red/Neutral palette with distinct borders & icons
  const getConfig = (s: string) => {
    switch (s) {
      case "active":
      case "approved":
      case "succeeded":
      case "paid":
      case "reconciled":
      case "closed":
      case "released":
      case "executed":
        return {
          icon: CheckCircle2,
          styles: "bg-primary text-primary-foreground border-primary",
        }
      case "favorable":
        return {
          icon: TrendingUp,
          styles: "bg-card text-foreground border-primary font-medium",
        }
      case "pending":
      case "pending_approval":
      case "processing":
      case "submitted":
      case "under_review":
      case "liquidating":
      case "open":
      case "partial":
      case "in_progress":
      case "verified":
        return {
          icon: Clock,
          styles: "bg-muted text-foreground border-border border-dashed",
        }
      case "soft_warn":
      case "exception_eligible":
        return {
          icon: AlertTriangle,
          styles: "bg-secondary/10 text-secondary border-secondary/40",
        }
      case "unfavorable":
        return {
          icon: TrendingDown,
          styles: "bg-secondary/10 text-secondary border-secondary/40 font-medium",
        }
      case "hard_block":
      case "rejected":
      case "failed":
      case "overdue":
      case "cancelled":
      case "expired":
        return {
          icon: s === "hard_block" ? ShieldAlert : XCircle,
          styles: "bg-secondary text-secondary-foreground border-secondary",
        }
      case "neutral":
        return {
          icon: Minus,
          styles: "bg-muted text-muted-foreground border-border",
        }
      case "draft":
      case "retired":
      default:
        return {
          icon: FileEdit,
          styles: "bg-card text-muted-foreground border-border",
        }
    }
  }

  const { icon: Icon, styles } = getConfig(normalized)

  return (
    <Badge
      variant="outline"
      className={cn(
        "inline-flex items-center gap-1.5 px-2.5 py-0.5 text-xs font-medium rounded-md transition-colors",
        styles,
        className
      )}
      {...props}
    >
      <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
      <span>{displayLabel}</span>
    </Badge>
  )
}

// ============================================================================
// 3. CONSISTENT ENTERPRISE PAGE HEADER
// ============================================================================

export interface PageHeaderProps {
  category?: string
  title: string
  description?: string
  primaryAction?: React.ReactNode
  secondaryActions?: React.ReactNode
  className?: string
}

export function PageHeader({
  category,
  title,
  description,
  primaryAction,
  secondaryActions,
  className,
}: PageHeaderProps) {
  return (
    <div className={cn("flex flex-col gap-4 pb-6 mb-6 border-b border-border sm:flex-row sm:items-center sm:justify-between", className)}>
      <div className="space-y-1">
        {category && (
          <p className="text-xs font-semibold uppercase tracking-wider text-secondary">
            {category}
          </p>
        )}
        <h1 className="text-2xl font-bold tracking-tight text-foreground sm:text-3xl">
          {title}
        </h1>
        {description && (
          <p className="text-sm text-muted-foreground max-w-2xl">
            {description}
          </p>
        )}
      </div>
      {(primaryAction || secondaryActions) && (
        <div className="flex flex-wrap items-center gap-2 shrink-0">
          {secondaryActions}
          {primaryAction}
        </div>
      )}
    </div>
  )
}


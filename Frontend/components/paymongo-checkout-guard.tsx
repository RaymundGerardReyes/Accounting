"use client"

import React from "react"
import { useRouter } from "next/navigation"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import {
  Clock,
  AlertTriangle,
  CheckCircle2,
  XCircle,
  RefreshCw,
  ExternalLink,
  Radio,
  ArrowRight,
} from "lucide-react"
import { formatPhpFromMinor, isApprovedGatewayOrigin } from "@/lib/payment-api"
import { usePayMongoRealtimeSync } from "@/hooks/use-paymongo-realtime-sync"

export interface PayMongoCheckoutGuardProps {
  attemptId: string
  orderId: string
  orderNumber?: string
  amountMinor: number
  initialStatus?: string
  expiresAt?: string | null
  checkoutUrl?: string | null
  onRetry?: () => void
  className?: string
}

export function PayMongoCheckoutGuard({
  attemptId,
  orderId,
  orderNumber,
  amountMinor,
  initialStatus = "pending",
  expiresAt,
  checkoutUrl,
  onRetry,
  className = "",
}: PayMongoCheckoutGuardProps) {
  const router = useRouter()

  const {
    status,
    isPayable,
    isTerminal,
    remainingSeconds,
    formattedRemaining,
    checkoutUrl: activeCheckoutUrl,
    isSyncing,
    isSignalRConnected,
    reconcileNow,
  } = usePayMongoRealtimeSync({
    attemptId,
    initialStatus,
    expiresAt,
    checkoutUrl,
    autoReconcile: true,
  })

  const isUrgent = remainingSeconds > 0 && remainingSeconds <= 180

  const handleOpenCheckout = () => {
    if (!activeCheckoutUrl) return
    if (!isApprovedGatewayOrigin(activeCheckoutUrl)) {
      console.error("Untrusted gateway URL origin:", activeCheckoutUrl)
      return
    }
    window.location.assign(activeCheckoutUrl)
  }

  // Succeeded state
  if (status === "succeeded" || status === "paid") {
    return (
      <div className={`p-4 rounded-xl border border-emerald-500/30 bg-emerald-500/10 text-emerald-800 dark:text-emerald-300 space-y-3 ${className}`}>
        <div className="flex items-center gap-2">
          <CheckCircle2 className="w-5 h-5 flex-shrink-0 text-emerald-600 dark:text-emerald-400" />
          <div>
            <h4 className="font-semibold text-sm">Payment Confirmed</h4>
            <p className="text-xs opacity-90">Transaction has been successfully settled and verified.</p>
          </div>
        </div>
        <Button
          type="button"
          onClick={() => router.push(`/payment/result/${attemptId}`)}
          className="w-full bg-emerald-600 text-white hover:bg-emerald-700 text-xs font-semibold gap-1.5"
        >
          <span>View Official Receipt &amp; Summary</span>
          <ArrowRight className="w-3.5 h-3.5" />
        </Button>
      </div>
    )
  }

  // Expired state
  if (status === "expired") {
    return (
      <div className={`p-4 rounded-xl border border-amber-500/30 bg-amber-500/10 text-amber-900 dark:text-amber-300 space-y-3 ${className}`}>
        <div className="flex items-center gap-2">
          <AlertTriangle className="w-5 h-5 flex-shrink-0 text-amber-600 dark:text-amber-400" />
          <div>
            <h4 className="font-semibold text-sm">Checkout Session Expired</h4>
            <p className="text-xs opacity-90">
              The 30-minute gateway authorization window has expired. The payment link is locked.
            </p>
          </div>
        </div>
        <Button
          type="button"
          onClick={onRetry || (() => router.push(`/checkout?order_id=${orderId}`))}
          className="w-full bg-amber-600 text-white hover:bg-amber-700 text-xs font-semibold"
        >
          Generate New Checkout Session
        </Button>
      </div>
    )
  }

  // Failed state
  if (status === "failed" || status === "cancelled") {
    return (
      <div className={`p-4 rounded-xl border border-rose-500/30 bg-rose-500/10 text-rose-900 dark:text-rose-300 space-y-3 ${className}`}>
        <div className="flex items-center gap-2">
          <XCircle className="w-5 h-5 flex-shrink-0 text-rose-600 dark:text-rose-400" />
          <div>
            <h4 className="font-semibold text-sm">Payment Unsuccessful</h4>
            <p className="text-xs opacity-90">The payment attempt was declined or cancelled.</p>
          </div>
        </div>
        <Button
          type="button"
          onClick={onRetry || (() => router.push(`/checkout?order_id=${orderId}`))}
          className="w-full bg-rose-600 text-white hover:bg-rose-700 text-xs font-semibold"
        >
          Try Checkout Again
        </Button>
      </div>
    )
  }

  // Active / Payable Countdown View
  return (
    <div className={`p-4 rounded-xl border border-border bg-card shadow-sm space-y-3 ${className}`}>
      <div className="flex items-center justify-between text-xs text-muted-foreground">
        <span className="flex items-center gap-1.5 font-medium">
          <Clock className="w-4 h-4 text-accent" />
          <span>PayMongo Session Timer</span>
        </span>
        <div className="flex items-center gap-2">
          <Badge
            variant="outline"
            className={`text-[10px] px-2 py-0.5 flex items-center gap-1 ${
              isSignalRConnected
                ? "border-emerald-500/40 text-emerald-600 dark:text-emerald-400"
                : "border-amber-500/40 text-amber-600 dark:text-amber-400"
            }`}
          >
            <Radio className={`w-3 h-3 ${isSignalRConnected ? "animate-pulse" : ""}`} />
            <span>{isSignalRConnected ? "SignalR Live" : "Polling"}</span>
          </Badge>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => reconcileNow()}
            disabled={isSyncing}
            className="h-6 w-6 p-0 text-muted-foreground hover:text-foreground"
            title="Refresh payment status"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? "animate-spin text-accent" : ""}`} />
          </Button>
        </div>
      </div>

      <div className="flex items-baseline justify-between">
        <div
          className={`font-mono text-3xl font-extrabold tracking-tight ${
            isUrgent ? "text-rose-500 animate-pulse" : "text-foreground"
          }`}
        >
          {formattedRemaining}
        </div>
        <span className="text-xs text-muted-foreground">
          {formatPhpFromMinor(amountMinor)}
        </span>
      </div>

      {/* Progress Bar */}
      <div className="w-full bg-muted h-1.5 rounded-full overflow-hidden">
        <div
          className={`h-full transition-all duration-1000 rounded-full ${
            isUrgent ? "bg-rose-500" : "bg-accent"
          }`}
          style={{
            width: `${Math.min(100, Math.max(0, (remainingSeconds / 1800) * 100))}%`,
          }}
        />
      </div>

      {/* Pay CTA Button */}
      <Button
        type="button"
        disabled={!isPayable || !activeCheckoutUrl}
        onClick={handleOpenCheckout}
        className="w-full py-5 text-sm font-semibold bg-accent text-accent-foreground hover:bg-accent/90 gap-2"
      >
        <ExternalLink className="w-4 h-4" />
        <span>Complete Payment on PayMongo</span>
      </Button>

      {orderNumber && (
        <p className="text-[11px] text-center text-muted-foreground font-mono">
          Order: {orderNumber}
        </p>
      )}
    </div>
  )
}


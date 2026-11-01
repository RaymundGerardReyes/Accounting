"use client"

import React, { useEffect, useState, useRef } from "react"
import { Clock, AlertTriangle, CheckCircle2, XCircle, RefreshCw, Radio } from "lucide-react"
import { Badge } from "@/components/ui/badge"

interface PaymentCountdownProps {
  expiresAt?: string | null
  status: string
  onExpired: () => void
  isSignalRConnected?: boolean
  className?: string
}

export function PaymentCountdown({
  expiresAt,
  status,
  onExpired,
  isSignalRConnected = false,
  className = "",
}: PaymentCountdownProps) {
  const [remainingSeconds, setRemainingSeconds] = useState<number>(0)
  const expiredTriggeredRef = useRef(false)
  const onExpiredRef = useRef(onExpired)

  useEffect(() => {
    onExpiredRef.current = onExpired
  }, [onExpired])

  const normalizedStatus = (status || "").toLowerCase()
  const isPending =
    normalizedStatus === "pending" ||
    normalizedStatus === "requires_action" ||
    normalizedStatus === "awaiting_payment"

  // Reset trigger ref if new future expiration timestamp arrives
  useEffect(() => {
    expiredTriggeredRef.current = false
  }, [expiresAt])

  useEffect(() => {
    if (!isPending || !expiresAt) {
      setRemainingSeconds(0)
      return
    }

    const deadline = new Date(expiresAt).getTime()
    if (isNaN(deadline)) {
      setRemainingSeconds(0)
      return
    }

    const calculateRemaining = () => {
      const now = Date.now()
      const diffSec = Math.max(0, Math.ceil((deadline - now) / 1000))
      setRemainingSeconds(diffSec)

      if (diffSec === 0 && !expiredTriggeredRef.current) {
        expiredTriggeredRef.current = true
        // One-time reconciliation trigger on zero
        onExpiredRef.current?.()
      }
    }

    // Initial calculation
    calculateRemaining()

    const timer = setInterval(calculateRemaining, 1000)
    return () => clearInterval(timer)
  }, [expiresAt, isPending])

  // Paid / Succeeded state
  if (normalizedStatus === "paid" || normalizedStatus === "succeeded") {
    return (
      <div className={`flex items-center gap-2 p-3 rounded-lg border border-emerald-500/20 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 ${className}`}>
        <CheckCircle2 className="w-5 h-5 flex-shrink-0" />
        <div className="text-sm">
          <p className="font-semibold">Payment Confirmed</p>
          <p className="text-xs opacity-90">Session settled successfully on server.</p>
        </div>
      </div>
    )
  }

  // Expired state
  if (normalizedStatus === "expired") {
    return (
      <div className={`flex items-center gap-2 p-3 rounded-lg border border-amber-500/20 bg-amber-500/10 text-amber-700 dark:text-amber-400 ${className}`}>
        <AlertTriangle className="w-5 h-5 flex-shrink-0" />
        <div className="text-sm">
          <p className="font-semibold">Payment Window Expired</p>
          <p className="text-xs opacity-90">
            This checkout session has expired. Please initiate a new payment attempt.
          </p>
        </div>
      </div>
    )
  }

  // Failed state
  if (normalizedStatus === "failed" || normalizedStatus === "cancelled") {
    return (
      <div className={`flex items-center gap-2 p-3 rounded-lg border border-rose-500/20 bg-rose-500/10 text-rose-700 dark:text-rose-400 ${className}`}>
        <XCircle className="w-5 h-5 flex-shrink-0" />
        <div className="text-sm">
          <p className="font-semibold">Payment Unsuccessful</p>
          <p className="text-xs opacity-90">Transaction was not completed. You may retry checkout.</p>
        </div>
      </div>
    )
  }

  // Processing state
  if (normalizedStatus === "processing") {
    return (
      <div className={`flex items-center gap-2 p-3 rounded-lg border border-blue-500/20 bg-blue-500/10 text-blue-700 dark:text-blue-400 ${className}`}>
        <RefreshCw className="w-5 h-5 animate-spin flex-shrink-0" />
        <div className="text-sm">
          <p className="font-semibold">Processing Settlement</p>
          <p className="text-xs opacity-90">Verifying authorization with PayMongo...</p>
        </div>
      </div>
    )
  }

  // Active Pending countdown
  const minutes = Math.floor(remainingSeconds / 60)
  const seconds = remainingSeconds % 60
  const isUrgent = remainingSeconds > 0 && remainingSeconds <= 180 // under 3 minutes

  return (
    <div className={`p-4 rounded-xl border border-border bg-card/60 backdrop-blur-sm space-y-2 ${className}`}>
      <div className="flex items-center justify-between text-xs text-muted-foreground">
        <span className="flex items-center gap-1.5 font-medium">
          <Clock className="w-4 h-4 text-accent" />
          Payment Time Remaining
        </span>
        <Badge
          variant="outline"
          className={`text-[10px] px-2 py-0.5 flex items-center gap-1 ${
            isSignalRConnected
              ? "border-emerald-500/40 text-emerald-600 dark:text-emerald-400"
              : "border-amber-500/40 text-amber-600 dark:text-amber-400"
          }`}
        >
          <Radio className={`w-3 h-3 ${isSignalRConnected ? "animate-pulse" : ""}`} />
          {isSignalRConnected ? "SignalR Live" : "Polling Sync"}
        </Badge>
      </div>

      <div className="flex items-baseline justify-between">
        <div
          className={`font-mono text-3xl font-extrabold tracking-tight ${
            isUrgent ? "text-rose-500 animate-pulse" : "text-foreground"
          }`}
        >
          {String(minutes).padStart(2, "0")}:{String(seconds).padStart(2, "0")}
        </div>
        <span className="text-xs text-muted-foreground text-right">
          {remainingSeconds === 0
            ? "Reconciling state..."
            : "Complete authorization before timer ends"}
        </span>
      </div>

      <div className="w-full bg-muted/60 h-1.5 rounded-full overflow-hidden">
        <div
          className={`h-full transition-all duration-1000 rounded-full ${
            isUrgent ? "bg-rose-500" : "bg-accent"
          }`}
          style={{
            width: expiresAt
              ? `${Math.min(100, Math.max(0, (remainingSeconds / 1800) * 100))}%`
              : "100%",
          }}
        />
      </div>
    </div>
  )
}
export default PaymentCountdown

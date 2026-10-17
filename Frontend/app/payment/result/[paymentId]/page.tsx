"use client"

import React, { useEffect, useState, use } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardDescription, CardHeader, CardTitle, CardFooter } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import {
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Clock,
  RefreshCw,
  ArrowLeft,
  Receipt,
  ShieldCheck,
  Loader2,
} from "lucide-react"
import {
  pollPaymentStatus,
  reconcilePayment,
  PaymentAttempt,
  formatPhpFromMinor,
} from "@/lib/payment-api"

interface PageProps {
  params: Promise<{ paymentId: string }>
}

export default function PaymentResultPage({ params }: PageProps) {
  const resolvedParams = use(params)
  const paymentId = resolvedParams.paymentId
  const router = useRouter()

  const [payment, setPayment] = useState<PaymentAttempt | null>(null)
  const [loading, setLoading] = useState(true)
  const [isReconciling, setIsReconciling] = useState(false)
  const [pollCount, setPollCount] = useState(0)
  const [error, setError] = useState<string | null>(null)

  const isTerminal = (status?: string) => {
    return status === "succeeded" || status === "failed" || status === "cancelled" || status === "expired"
  }

  // Polling loop
  useEffect(() => {
    let timer: NodeJS.Timeout
    let active = true

    const checkStatus = async () => {
      try {
        const data = await pollPaymentStatus(paymentId)
        if (!active) return

        setPayment(data)
        setLoading(false)

        // Continue polling if non-terminal and under limit (30 tries = 60s)
        if (!isTerminal(data.status) && pollCount < 30) {
          timer = setTimeout(() => {
            setPollCount((prev) => prev + 1)
          }, 2000)
        }
      } catch (err: any) {
        if (!active) return
        setError(err.message || "Failed to poll payment status")
        setLoading(false)
      }
    }

    checkStatus()

    return () => {
      active = false
      clearTimeout(timer)
    }
  }, [paymentId, pollCount])

  const handleManualReconcile = async () => {
    setIsReconciling(true)
    try {
      await reconcilePayment(paymentId)
      const updated = await pollPaymentStatus(paymentId)
      setPayment(updated)
    } catch (err: any) {
      setError(err.message || "Manual reconciliation error")
    } finally {
      setIsReconciling(false)
    }
  }

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <div className="flex flex-col items-center gap-3">
          <Loader2 className="w-8 h-8 animate-spin text-accent" />
          <p className="text-sm text-muted-foreground">Retrieving payment status...</p>
        </div>
      </div>
    )
  }

  if (error || !payment) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <Card className="max-w-md w-full border-border">
          <CardHeader>
            <div className="flex items-center gap-2 text-primary">
              <XCircle className="w-5 h-5" />
              <CardTitle>Payment Error</CardTitle>
            </div>
            <CardDescription>{error || "Unable to find payment attempt."}</CardDescription>
          </CardHeader>
          <CardFooter>
            <Button variant="outline" className="w-full" onClick={() => router.push("/")}>
              <ArrowLeft className="w-4 h-4 mr-2" />
              Return to Dashboard
            </Button>
          </CardFooter>
        </Card>
      </div>
    )
  }

  const { status, amount_minor, order, provider_payment_id, paid_at, failure_message } = payment

  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4 md:p-8">
      <Card className="max-w-xl w-full border-border shadow-lg">
        {/* Status Header */}
        <CardHeader className="text-center pb-4">
          <div className="flex justify-center mb-3">
            {status === "succeeded" && (
              <div className="p-3 rounded-full bg-accent/10 text-accent">
                <CheckCircle2 className="w-12 h-12" />
              </div>
            )}
            {status === "failed" && (
              <div className="p-3 rounded-full bg-red-500/10 text-red-600">
                <XCircle className="w-12 h-12" />
              </div>
            )}
            {(status === "expired" || status === "cancelled") && (
              <div className="p-3 rounded-full bg-amber-500/10 text-amber-600">
                <AlertTriangle className="w-12 h-12" />
              </div>
            )}
            {(status === "pending" || status === "requires_action" || status === "processing" || status === "unknown") && (
              <div className="p-3 rounded-full bg-blue-500/10 text-blue-600">
                <Clock className="w-12 h-12 animate-pulse" />
              </div>
            )}
          </div>

          <CardTitle className="text-2xl font-bold">
            {status === "succeeded" && "Payment Successful!"}
            {status === "failed" && "Payment Failed"}
            {status === "expired" && "Payment Session Expired"}
            {status === "cancelled" && "Payment Cancelled"}
            {(status === "pending" || status === "requires_action" || status === "processing") &&
              "Awaiting Payment Settlement..."}
            {status === "unknown" && "Settlement Status Uncertain"}
          </CardTitle>

          <CardDescription className="text-sm mt-1">
            {status === "succeeded" && "Your transaction has been verified and fulfilled in the Acme Ledger."}
            {status === "failed" && (failure_message || "The payment transaction could not be completed.")}
            {status === "expired" && "The checkout window closed before completion. Please initiate a new session."}
            {status === "cancelled" && "The payment was cancelled by user request."}
            {(status === "pending" || status === "requires_action" || status === "processing") &&
              "We are waiting for PayMongo's authoritative signed webhook confirmation."}
            {status === "unknown" &&
              "A network timeout occurred. Please trigger reconciliation to synchronize with PayMongo."}
          </CardDescription>
        </CardHeader>

        {/* Transaction Summary */}
        <CardContent className="space-y-4">
          <div className="rounded-lg border border-border p-4 space-y-3 bg-muted/30">
            <div className="flex justify-between items-center text-sm border-b border-border pb-2">
              <span className="text-muted-foreground">Amount Paid</span>
              <span className="font-bold text-accent text-lg">{formatPhpFromMinor(amount_minor)}</span>
            </div>

            {order && (
              <div className="flex justify-between text-sm">
                <span className="text-muted-foreground">Order Title</span>
                <span className="font-medium text-foreground text-right">{order.title}</span>
              </div>
            )}

            {order && (
              <div className="flex justify-between text-sm">
                <span className="text-muted-foreground">Order Reference</span>
                <span className="font-mono text-xs">{order.order_number}</span>
              </div>
            )}

            <div className="flex justify-between text-sm">
              <span className="text-muted-foreground">Gateway Provider</span>
              <span className="font-medium">PayMongo Philippines</span>
            </div>

            {provider_payment_id && (
              <div className="flex justify-between text-sm">
                <span className="text-muted-foreground">Session / Payment ID</span>
                <span className="font-mono text-xs">{provider_payment_id}</span>
              </div>
            )}

            <div className="flex justify-between text-sm">
              <span className="text-muted-foreground">Settlement Time</span>
              <span className="text-xs">
                {paid_at ? new Date(paid_at).toLocaleString() : "Pending confirmation"}
              </span>
            </div>
          </div>

          {/* Pending or Unknown Guidance */}
          {!isTerminal(status) && (
            <div className="p-3 rounded-lg bg-blue-500/10 border border-blue-500/20 text-xs text-blue-700 dark:text-blue-400 space-y-2">
              <div className="flex items-center gap-2 font-medium">
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
                <span>Polling PayMongo Live Webhooks (Attempt {pollCount}/30)</span>
              </div>
              <p>
                Transactions paid via GCash, Maya, or QR Ph usually settle within 2 to 5 seconds. You can also trigger an immediate server-side reconciliation check below.
              </p>
            </div>
          )}
        </CardContent>

        <CardFooter className="flex flex-col sm:flex-row gap-3 pt-2">
          {!isTerminal(status) && (
            <Button
              variant="outline"
              onClick={handleManualReconcile}
              disabled={isReconciling}
              className="w-full sm:flex-1 gap-2"
            >
              <RefreshCw className={`w-4 h-4 ${isReconciling ? "animate-spin" : ""}`} />
              <span>{isReconciling ? "Reconciling..." : "Reconcile with Gateway"}</span>
            </Button>
          )}

          {(status === "failed" || status === "expired" || status === "cancelled") && order && (
            <Button
              className="w-full sm:flex-1 bg-accent text-accent-foreground hover:bg-accent/90"
              onClick={() => router.push(`/checkout?order_id=${order.id}`)}
            >
              {status === "expired" ? "Start New Checkout" : "Try Again"}
            </Button>
          )}

          <Button
            variant="default"
            className="w-full sm:flex-1 bg-secondary text-secondary-foreground hover:bg-secondary/80"
            onClick={() => router.push("/")}
          >
            <ArrowLeft className="w-4 h-4 mr-2" />
            Back to Dashboard
          </Button>
        </CardFooter>
      </Card>
    </div>
  )
}


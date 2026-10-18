"use client"

import React, { useState, useEffect, use, Suspense } from "react"
import { useSearchParams, useRouter } from "next/navigation"
import { Card, CardContent, CardDescription, CardHeader, CardTitle, CardFooter } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import {
  QrCode,
  Smartphone,
  CreditCard,
  Building2,
  CheckCircle2,
  XCircle,
  Clock,
  Loader2,
} from "lucide-react"
import { fetchOrder, pollPaymentStatus, Order, formatPhpFromMinor, simulateWebhook } from "@/lib/payment-api"

interface SandboxProps {
  params: Promise<{ attemptId: string }>
}

function SandboxContent({ params }: SandboxProps) {
  const resolvedParams = use(params)
  const targetId = resolvedParams.attemptId
  const searchParams = useSearchParams()
  const queryOrderId = searchParams?.get("order_id") || null
  const sessionId = searchParams?.get("session_id") || "cs_sim_demo"
  const router = useRouter()

  const searchMethod = searchParams?.get("method") || null
  const initialMethod = (
    searchMethod === "card" || searchMethod === "card_visa" ? "card_visa"
    : searchMethod === "gcash" ? "gcash"
    : searchMethod === "maya" || searchMethod === "paymaya" ? "maya"
    : searchMethod === "dob" || searchMethod === "dob_bpi" || searchMethod === "bpi" ? "dob_bpi"
    : "qrph"
  )

  const [order, setOrder] = useState<Order | null>(null)
  const [attemptId, setAttemptId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [processing, setProcessing] = useState(false)
  const [method, setMethod] = useState<"qrph" | "card_visa" | "gcash" | "maya" | "dob_bpi">(initialMethod)
  const [webhookTiming, setWebhookTiming] = useState<"before_redirect" | "after_redirect">("before_redirect")
  const [statusMessage, setStatusMessage] = useState<string | null>(null)

  useEffect(() => {
    const loadTarget = async () => {
      try {
        // 1. If queryOrderId is present, load that order first
        if (queryOrderId) {
          try {
            const ord = await fetchOrder(queryOrderId)
            setOrder(ord)
            const att = ord.latest_attempt || ord.attempts?.[0]
            if (att) setAttemptId(att.id)
            else setAttemptId(targetId)
            return
          } catch {}
        }

        // 2. Try fetching as order
        try {
          const ord = await fetchOrder(targetId)
          setOrder(ord)
          const att = ord.latest_attempt || ord.attempts?.[0]
          if (att) setAttemptId(att.id)
          else setAttemptId(targetId)
          return
        } catch {}

        // 3. Try fetching as payment attempt
        const paymentData = await pollPaymentStatus(targetId)
        if (paymentData) {
          setAttemptId(paymentData.id)
          if (paymentData.order) {
            setOrder(paymentData.order)
          } else if (paymentData.order_id) {
            const ord = await fetchOrder(paymentData.order_id)
            setOrder(ord)
          }
        }
      } catch (err) {
        console.error("Failed to load sandbox target:", err)
      } finally {
        setLoading(false)
      }
    }
    loadTarget()
  }, [targetId, queryOrderId])

  const handleSimulatePayment = async (resultType: "paid" | "failed" | "expired") => {
    if (!order) return
    setProcessing(true)
    setStatusMessage("Generating HMAC-signed PayMongo webhook...")

    const resolvedAttemptId = attemptId || order.latest_attempt?.id || (order as any).attempts?.[0]?.id || order.id
    const eventType =
      resultType === "paid"
        ? "checkout_session.payment.paid"
        : resultType === "failed"
        ? "payment.failed"
        : "checkout_session.expired"

    try {
      if (webhookTiming === "before_redirect") {
        // Send webhook first
        await simulateWebhook(resolvedAttemptId, eventType, false, method)
        setStatusMessage("Signed webhook processed! Redirecting to return URL...")
        setTimeout(() => {
          router.push(`/payment/return?order_id=${order.id}&payment_id=${resolvedAttemptId}`)
        }, 800)
      } else {
        // Redirect first, send webhook after 3 seconds in the background to test real-time polling
        setStatusMessage("Redirecting browser first (testing real-time polling sync)...")
        setTimeout(async () => {
          try {
            await simulateWebhook(resolvedAttemptId, eventType, false, method)
          } catch (e) {
            console.error("Delayed webhook simulation failed:", e)
          }
        }, 3000)
        setTimeout(() => {
          router.push(`/payment/return?order_id=${order.id}&payment_id=${resolvedAttemptId}`)
        }, 800)
      }
    } catch (err: any) {
      console.error("Sandbox simulation error:", err)
      setStatusMessage(`Error: ${err.message}`)
      setProcessing(false)
    }
  }

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <Loader2 className="w-8 h-8 animate-spin text-accent" />
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-background text-foreground flex items-center justify-center p-4">
      <Card className="max-w-xl w-full border-border bg-card shadow-lg">
        <CardHeader className="border-b border-border pb-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <span className="w-2.5 h-2.5 rounded-full bg-accent animate-pulse" />
              <span className="text-xs font-mono text-accent uppercase tracking-wider font-semibold">
                PayMongo Local Sandbox Simulator
              </span>
            </div>
            <span className="text-[10px] bg-muted text-muted-foreground px-2 py-0.5 rounded font-mono">
              Session: {sessionId.slice(0, 14)}...
            </span>
          </div>
          <CardTitle className="text-xl text-foreground font-bold mt-2 tracking-tight">
            Simulate PayMongo Hosted Checkout
          </CardTitle>
          <CardDescription className="text-muted-foreground">
            {order ? `${order.title} (${order.order_number})` : "Simulated Order"}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-6 pt-6">
          {/* Amount banner */}
          <div className="p-4 rounded-lg bg-muted/40 border border-border flex justify-between items-center">
            <div>
              <p className="text-xs text-muted-foreground">Total Payable Amount</p>
              <p className="text-2xl font-bold text-foreground font-mono tabular-nums">
                {order ? formatPhpFromMinor(order.amount_minor) : "₱0.00"}
              </p>
            </div>
            <span className="text-xs bg-muted text-foreground border border-border px-2.5 py-1 rounded font-mono">
              PHP Currency
            </span>
          </div>

          {/* Simulated Payment Channel Picker */}
          <div>
            <label id="payment-channel-label" className="text-xs font-semibold text-foreground block mb-2 uppercase tracking-wide">
              1. Choose Simulated Payment Channel
            </label>
            <div role="radiogroup" aria-labelledby="payment-channel-label" className="grid grid-cols-1 sm:grid-cols-2 gap-2">
              {[
                { id: "qrph", name: "Scan GQRPh code to pay (QR Ph)", icon: QrCode },
                { id: "card_visa", name: "Card (VISA 3D Secure)", icon: CreditCard },
                { id: "gcash", name: "E-Wallet (GCash)", icon: Smartphone },
                { id: "maya", name: "E-Wallet (Maya)", icon: Smartphone },
                { id: "dob_bpi", name: "Online Banking (BPI)", icon: Building2 },
              ].map((c) => {
                const Icon = c.icon
                const isSelected = method === c.id
                return (
                  <button
                    key={c.id}
                    type="button"
                    role="radio"
                    aria-checked={isSelected}
                    onClick={() => setMethod(c.id as any)}
                    className={`flex items-center gap-2 p-3 rounded-lg border text-left text-xs transition-colors focus:outline-none focus:ring-2 focus:ring-accent ${
                      isSelected
                        ? "border-primary bg-primary text-primary-foreground font-semibold"
                        : "border-border bg-card text-muted-foreground hover:bg-muted/40 hover:text-foreground"
                    }`}
                  >
                    <Icon className="w-4 h-4 shrink-0" aria-hidden="true" />
                    <span>{c.name}</span>
                  </button>
                )
              })}
            </div>
          </div>

          {/* Simulated Race Condition / Timing */}
          <div>
            <label id="timing-label" className="text-xs font-semibold text-foreground block mb-2 uppercase tracking-wide">
              2. Test Race Condition / Timing
            </label>
            <div role="radiogroup" aria-labelledby="timing-label" className="grid grid-cols-2 gap-2 text-xs">
              <button
                type="button"
                role="radio"
                aria-checked={webhookTiming === "before_redirect"}
                onClick={() => setWebhookTiming("before_redirect")}
                className={`p-3 rounded-lg border text-left transition-colors focus:outline-none focus:ring-2 focus:ring-accent ${
                  webhookTiming === "before_redirect"
                    ? "border-primary bg-primary text-primary-foreground font-semibold"
                    : "border-border bg-card text-muted-foreground hover:bg-muted/40 hover:text-foreground"
                }`}
              >
                <div className="font-medium mb-1">Webhook First (Normal)</div>
                <div className="text-[11px] opacity-80">Webhook arrives before redirect</div>
              </button>
              <button
                type="button"
                role="radio"
                aria-checked={webhookTiming === "after_redirect"}
                onClick={() => setWebhookTiming("after_redirect")}
                className={`p-3 rounded-lg border text-left transition-colors focus:outline-none focus:ring-2 focus:ring-accent ${
                  webhookTiming === "after_redirect"
                    ? "border-primary bg-primary text-primary-foreground font-semibold"
                    : "border-border bg-card text-muted-foreground hover:bg-muted/40 hover:text-foreground"
                }`}
              >
                <div className="font-medium mb-1">Redirect First (Real-Time Polling)</div>
                <div className="text-[11px] opacity-80">Redirect arrives before webhook</div>
              </button>
            </div>
          </div>

          {statusMessage && (
            <div className="p-3 rounded-lg bg-muted/40 border border-border text-xs text-foreground flex items-center gap-2 font-mono">
              <Loader2 className="w-4 h-4 animate-spin shrink-0 text-accent" />
              <span>{statusMessage}</span>
            </div>
          )}
        </CardContent>

        <CardFooter className="flex flex-col gap-2 border-t border-border pt-4">
          <Button
            type="button"
            disabled={processing}
            onClick={() => handleSimulatePayment("paid")}
            className="w-full bg-primary hover:bg-primary/90 text-primary-foreground font-semibold py-5 gap-2"
          >
            <CheckCircle2 className="w-4 h-4" />
            <span>Authorize &amp; Complete Payment (Succeed)</span>
          </Button>

          <div className="grid grid-cols-2 gap-2 w-full">
            <Button
              type="button"
              variant="outline"
              disabled={processing}
              onClick={() => handleSimulatePayment("failed")}
              className="border-destructive/30 bg-destructive/10 text-destructive hover:bg-destructive/20 text-xs gap-1.5"
            >
              <XCircle className="w-3.5 h-3.5" />
              <span>Simulate Decline</span>
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={processing}
              onClick={() => handleSimulatePayment("expired")}
              className="border-border bg-muted/30 text-foreground hover:bg-muted text-xs gap-1.5"
            >
              <Clock className="w-3.5 h-3.5" />
              <span>Simulate Expiry</span>
            </Button>
          </div>
        </CardFooter>
      </Card>
    </div>
  )
}

export default function SandboxPage({ params }: SandboxProps) {
  return (
    <Suspense fallback={<div className="p-8 text-center text-sm">Loading Simulator...</div>}>
      <SandboxContent params={params} />
    </Suspense>
  )
}

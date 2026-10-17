"use client"

import React, { useState, useEffect, use } from "react"
import { useSearchParams, useRouter } from "next/navigation"
import { Card, CardContent, CardDescription, CardHeader, CardTitle, CardFooter } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import {
  QrCode,
  Smartphone,
  CreditCard,
  Building2,
  ShieldAlert,
  CheckCircle2,
  XCircle,
  Clock,
  ArrowRight,
  Loader2,
} from "lucide-react"
import { fetchOrder, pollPaymentStatus, Order, formatPhpFromMinor, simulateWebhook } from "@/lib/payment-api"

interface SandboxProps {
  params: Promise<{ attemptId: string }>
}

export default function SandboxPage({ params }: SandboxProps) {
  const resolvedParams = use(params)
  const targetId = resolvedParams.attemptId
  const searchParams = useSearchParams()
  const queryOrderId = searchParams.get("order_id")
  const sessionId = searchParams.get("session_id") || "cs_sim_demo"
  const router = useRouter()

  const [order, setOrder] = useState<Order | null>(null)
  const [attemptId, setAttemptId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [processing, setProcessing] = useState(false)
  const [method, setMethod] = useState<"qrph" | "gcash" | "maya" | "card">("qrph")
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
        await simulateWebhook(resolvedAttemptId, eventType)
        setStatusMessage("Signed webhook processed! Redirecting to return URL...")
        setTimeout(() => {
          router.push(`/payment/return?order_id=${order.id}&payment_id=${resolvedAttemptId}`)
        }, 800)
      } else {
        // Redirect first, send webhook after 3 seconds in the background to test polling
        setStatusMessage("Redirecting browser first (testing polling race condition)...")
        setTimeout(async () => {
          try {
            await simulateWebhook(resolvedAttemptId, eventType)
          } catch (e) {
            console.error("Delayed webhook error:", e)
          }
        }, 3000)

        setTimeout(() => {
          router.push(`/payment/return?order_id=${order.id}&payment_id=${resolvedAttemptId}`)
        }, 500)
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
    <div className="min-h-screen bg-slate-950 text-slate-100 flex items-center justify-center p-4">
      <Card className="max-w-xl w-full border-slate-800 bg-slate-900 shadow-2xl">
        <CardHeader className="border-b border-slate-800 pb-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
              <span className="text-xs font-mono text-emerald-400 uppercase tracking-wider font-semibold">
                PayMongo Local Sandbox Simulator
              </span>
            </div>
            <span className="text-[10px] bg-slate-800 text-slate-400 px-2 py-0.5 rounded font-mono">
              Session: {sessionId.slice(0, 14)}...
            </span>
          </div>
          <CardTitle className="text-xl text-white mt-2">
            Simulate PayMongo Hosted Checkout
          </CardTitle>
          <CardDescription className="text-slate-400">
            {order ? `${order.title} (${order.order_number})` : "Simulated Order"}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-6 pt-6">
          {/* Amount banner */}
          <div className="p-4 rounded-lg bg-slate-800/80 border border-slate-700/60 flex justify-between items-center">
            <div>
              <p className="text-xs text-slate-400">Total Payable Amount</p>
              <p className="text-2xl font-bold text-emerald-400">
                {order ? formatPhpFromMinor(order.amount_minor) : "₱0.00"}
              </p>
            </div>
            <span className="text-xs bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 px-2.5 py-1 rounded">
              PHP Currency
            </span>
          </div>

          {/* Simulated Payment Channel Picker */}
          <div>
            <label id="payment-channel-label" className="text-xs font-semibold text-slate-300 block mb-2 uppercase tracking-wide">
              1. Choose Simulated Payment Channel
            </label>
            <div role="radiogroup" aria-labelledby="payment-channel-label" className="grid grid-cols-2 gap-2">
              {[
                { id: "qrph", name: "QR Ph National QR", icon: QrCode },
                { id: "gcash", name: "GCash Wallet", icon: Smartphone },
                { id: "maya", name: "Maya Wallet", icon: Smartphone },
                { id: "card", name: "Credit / Debit Card", icon: CreditCard },
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
                    className={`flex items-center gap-2 p-3 rounded-lg border text-left text-xs transition-colors focus:outline-none focus:ring-2 focus:ring-emerald-500 ${
                      isSelected
                        ? "border-emerald-500 bg-emerald-500/10 text-emerald-300 font-semibold"
                        : "border-slate-800 bg-slate-800/40 text-slate-300 hover:bg-slate-800"
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
            <label id="timing-label" className="text-xs font-semibold text-slate-300 block mb-2 uppercase tracking-wide">
              2. Test Race Condition / Timing
            </label>
            <div role="radiogroup" aria-labelledby="timing-label" className="grid grid-cols-2 gap-2 text-xs">
              <button
                type="button"
                role="radio"
                aria-checked={webhookTiming === "before_redirect"}
                onClick={() => setWebhookTiming("before_redirect")}
                className={`p-3 rounded-lg border text-left transition-colors focus:outline-none focus:ring-2 focus:ring-emerald-500 ${
                  webhookTiming === "before_redirect"
                    ? "border-emerald-500 bg-emerald-500/10 text-emerald-300 font-semibold"
                    : "border-slate-800 bg-slate-800/40 text-slate-400 hover:bg-slate-800"
                }`}
              >
                <div className="font-medium text-white mb-1">Webhook First (Normal)</div>
                <div className="text-[11px] text-slate-400">Webhook arrives before redirect</div>
              </button>
              <button
                type="button"
                role="radio"
                aria-checked={webhookTiming === "after_redirect"}
                onClick={() => setWebhookTiming("after_redirect")}
                className={`p-3 rounded-lg border text-left transition-colors focus:outline-none focus:ring-2 focus:ring-emerald-500 ${
                  webhookTiming === "after_redirect"
                    ? "border-emerald-500 bg-emerald-500/10 text-emerald-300 font-semibold"
                    : "border-slate-800 bg-slate-800/40 text-slate-400 hover:bg-slate-800"
                }`}
              >
                <div className="font-medium text-white mb-1">Redirect First (Polling)</div>
                <div className="text-[11px] text-slate-400">Redirect arrives 3s before webhook</div>
              </button>
            </div>
          </div>

          {statusMessage && (
            <div className="p-3 rounded-lg bg-emerald-950 border border-emerald-800 text-xs text-emerald-300 flex items-center gap-2">
              <Loader2 className="w-4 h-4 animate-spin shrink-0" />
              <span>{statusMessage}</span>
            </div>
          )}
        </CardContent>

        <CardFooter className="flex flex-col gap-2 border-t border-slate-800 pt-4">
          <Button
            type="button"
            disabled={processing}
            onClick={() => handleSimulatePayment("paid")}
            className="w-full bg-emerald-600 hover:bg-emerald-500 text-white font-semibold py-5 gap-2"
          >
            <CheckCircle2 className="w-4 h-4" />
            <span>Authorize & Complete Payment (Succeed)</span>
          </Button>

          <div className="grid grid-cols-2 gap-2 w-full">
            <Button
              type="button"
              variant="outline"
              disabled={processing}
              onClick={() => handleSimulatePayment("failed")}
              className="border-slate-700 bg-transparent text-red-400 hover:bg-red-950/30 hover:border-red-600 text-xs gap-1.5"
            >
              <XCircle className="w-3.5 h-3.5" />
              <span>Simulate Decline</span>
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={processing}
              onClick={() => handleSimulatePayment("expired")}
              className="border-slate-700 bg-transparent text-amber-400 hover:bg-amber-950/30 hover:border-amber-600 text-xs gap-1.5"
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


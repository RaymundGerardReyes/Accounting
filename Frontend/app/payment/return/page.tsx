"use client"

import React, { useEffect, useState, Suspense } from "react"
import { useSearchParams, useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Loader2, ShieldCheck, AlertCircle } from "lucide-react"
import { fetchOrder, fetchOrders } from "@/lib/payment-api"

function ReturnContent() {
  const searchParams = useSearchParams()
  const router = useRouter()
  const orderId = searchParams?.get("order_id") || searchParams?.get("orderId") || null
  const paymentId = searchParams?.get("payment_id") || searchParams?.get("paymentId") || null

  const [message, setMessage] = useState("Connecting to PayMongo Orchestrator...")
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const resolvePayment = async () => {
      try {
        if (paymentId) {
          setMessage("Verifying payment settlement status...")
          router.replace(`/payment/result/${paymentId}`)
          return
        }

        if (orderId) {
          setMessage("Retrieving order payment attempt...")
          const order = await fetchOrder(orderId)
          if (order.attempts && order.attempts.length > 0) {
            const latestAttempt = order.attempts[0]
            router.replace(`/payment/result/${latestAttempt.id}`)
            return
          }
        }

        // If neither, fallback to the latest order's attempt
        const orders = await fetchOrders()
        if (orders.length > 0 && orders[0].latest_attempt) {
          router.replace(`/payment/result/${orders[0].latest_attempt.id}`)
          return
        }

        setError("Unable to find matching payment record for this session.")
      } catch (err: any) {
        setError(err.message || "Failed to resolve payment attempt")
      }
    }

    resolvePayment()
  }, [orderId, paymentId, router])

  if (error) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <Card className="max-w-md w-full border-border">
          <CardHeader>
            <div className="flex items-center gap-2 text-primary">
              <AlertCircle className="w-5 h-5" />
              <CardTitle>Verification Notice</CardTitle>
            </div>
            <CardDescription>{error}</CardDescription>
          </CardHeader>
          <CardContent>
            <button
              onClick={() => router.push("/")}
              className="w-full py-2 px-4 rounded-md bg-secondary text-secondary-foreground text-sm font-medium hover:bg-secondary/80 transition-colors"
            >
              Return to Dashboard
            </button>
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-6">
      <Card className="max-w-md w-full border-border shadow-md text-center">
        <CardHeader className="pb-2">
          <div className="flex justify-center mb-3">
            <div className="p-3 rounded-full bg-accent/10 text-accent">
              <Loader2 className="w-8 h-8 animate-spin" />
            </div>
          </div>
          <CardTitle className="text-xl">Verifying Payment</CardTitle>
          <CardDescription className="text-sm">{message}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4 pt-2">
          <div className="p-3 rounded-lg bg-muted/50 border border-border text-xs text-muted-foreground flex items-center gap-2 text-left">
            <ShieldCheck className="w-4 h-4 text-accent shrink-0" />
            <span>
              Security Rule: Authoritative status is confirmed only via PayMongo signed webhook or reconciliation.
            </span>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

export default function PaymentReturnPage() {
  return (
    <Suspense fallback={<div className="p-8 text-center text-sm">Verifying payment...</div>}>
      <ReturnContent />
    </Suspense>
  )
}


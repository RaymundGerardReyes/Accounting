"use client"

import React, { useState } from "react"
import { Button } from "@/components/ui/button"
import { CreditCard, Loader2, AlertCircle } from "lucide-react"
import { createPaymentAttempt, isApprovedGatewayOrigin, formatPhpFromMinor } from "@/lib/payment-api"

interface CheckoutButtonProps {
  orderId: string
  orderNumber?: string
  amountMinor: number
  paymentMethod?: string
  className?: string
  children?: React.ReactNode
  onInitiated?: (checkoutUrl: string) => void
}

export function CheckoutButton({
  orderId,
  orderNumber,
  amountMinor,
  paymentMethod = "hosted_checkout",
  className = "",
  children,
  onInitiated,
}: CheckoutButtonProps) {
  const [loading, setLoading] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const handleCheckout = async () => {
    setLoading(true)
    setErrorMessage(null)

    try {
      // 1. Generate client idempotency key to prevent double submits
      const idempotencyKey = `idemp_${orderId}_${Date.now()}`

      // 2. Initiate payment attempt with backend orchestrator
      const attempt = await createPaymentAttempt(orderId, {
        idempotency_key: idempotencyKey,
        payment_method: paymentMethod,
        success_url: `${window.location.origin}/payment/return?order_id=${orderId}`,
        cancel_url: `${window.location.origin}/checkout?order_id=${orderId}&cancelled=true`,
      })

      if (!attempt.checkout_url) {
        throw new Error("No checkout URL returned from payment provider.")
      }

      // 3. Security Check: Validate origin before redirecting
      if (!isApprovedGatewayOrigin(attempt.checkout_url)) {
        throw new Error(`Untrusted checkout gateway URL: ${attempt.checkout_url}`)
      }

      if (onInitiated) {
        onInitiated(attempt.checkout_url)
      }

      // 4. Redirect browser to PayMongo hosted checkout (or local sandbox)
      window.location.assign(attempt.checkout_url)
    } catch (err: any) {
      console.error("Payment initiation error:", err)
      setErrorMessage(err.message || "Failed to start payment checkout.")
      setLoading(false)
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <Button
        type="button"
        onClick={handleCheckout}
        disabled={loading}
        className={`bg-accent text-accent-foreground hover:bg-accent/90 transition-all font-semibold gap-2 ${className}`}
        aria-label={`Pay ${formatPhpFromMinor(amountMinor)} for order ${orderNumber || orderId}`}
        aria-busy={loading}
      >
        {loading ? (
          <>
            <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" />
            <span>Connecting to PayMongo...</span>
          </>
        ) : (
          children || (
            <>
              <CreditCard className="w-4 h-4" aria-hidden="true" />
              <span>Pay with PayMongo {formatPhpFromMinor(amountMinor)}</span>
            </>
          )
        )}
      </Button>

      {errorMessage && (
        <div
          role="alert"
          className="flex items-center gap-2 p-2.5 text-xs rounded-md bg-red-500/10 text-red-600 border border-red-500/20"
        >
          <AlertCircle className="w-4 h-4 shrink-0" aria-hidden="true" />
          <span>{errorMessage}</span>
        </div>
      )}
    </div>
  )
}


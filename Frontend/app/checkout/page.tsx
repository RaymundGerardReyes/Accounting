"use client"

import React, { useEffect, useState, Suspense } from "react"
import { useSearchParams, useRouter } from "next/navigation"
import { Card, CardContent, CardDescription, CardHeader, CardTitle, CardFooter } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import {
  CreditCard,
  QrCode,
  Smartphone,
  Building2,
  ArrowLeft,
  CheckCircle2,
  ShieldCheck,
  AlertCircle,
  Loader2,
} from "lucide-react"
import { fetchOrder, Order, formatPhpFromMinor } from "@/lib/payment-api"
import { CheckoutButton } from "@/components/checkout-button"

function CheckoutContent() {
  const searchParams = useSearchParams()
  const router = useRouter()
  const orderId = searchParams.get("order_id") || searchParams.get("orderId")
  const isCancelled = searchParams.get("cancelled") === "true"

  const [order, setOrder] = useState<Order | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selectedChannel, setSelectedChannel] = useState<string>("hosted_checkout")

  useEffect(() => {
    if (!orderId) {
      setError("No order ID provided. Please select an order from the Payments & Checkout hub.")
      setLoading(false)
      return
    }

    const loadOrder = async () => {
      try {
        const data = await fetchOrder(orderId)
        setOrder(data)
      } catch (err: any) {
        setError(err.message || "Failed to load order details")
      } finally {
        setLoading(false)
      }
    }

    loadOrder()
  }, [orderId])

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <div className="flex flex-col items-center gap-3">
          <Loader2 className="w-8 h-8 animate-spin text-accent" />
          <p className="text-sm text-muted-foreground">Loading checkout order...</p>
        </div>
      </div>
    )
  }

  if (error || !order) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <Card className="max-w-md w-full border-border">
          <CardHeader>
            <div className="flex items-center gap-2 text-primary">
              <AlertCircle className="w-5 h-5" />
              <CardTitle>Order Not Found</CardTitle>
            </div>
            <CardDescription>{error || "Unable to retrieve order details."}</CardDescription>
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

  const paymentChannels = [
    {
      id: "hosted_checkout",
      name: "All-in-One PayMongo Checkout",
      desc: "QR Ph, GCash, Maya, BPI/UB Online Banking, & Cards",
      icon: ShieldCheck,
      recommended: true,
    },
    {
      id: "qrph",
      name: "QR Ph (National QR Code)",
      desc: "Scan and pay using any PH bank or e-wallet app",
      icon: QrCode,
    },
    {
      id: "gcash_maya",
      name: "E-Wallets (GCash & Maya)",
      desc: "Instant mobile wallet authorization",
      icon: Smartphone,
    },
    {
      id: "card",
      name: "Credit / Debit Card",
      desc: "Visa, Mastercard, JCB with 3D Secure verification",
      icon: CreditCard,
    },
    {
      id: "dob",
      name: "Direct Online Banking",
      desc: "BPI, UnionBank direct bank debit",
      icon: Building2,
    },
  ]

  return (
    <div className="min-h-screen bg-background text-foreground flex flex-col justify-between p-4 md:p-8">
      <div className="max-w-3xl mx-auto w-full space-y-6">
        {/* Navigation & Brand */}
        <div className="flex items-center justify-between border-b border-border pb-4">
          <Button variant="ghost" size="sm" onClick={() => router.push("/")} className="gap-2">
            <ArrowLeft className="w-4 h-4" />
            <span>Back to Accounting</span>
          </Button>
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <ShieldCheck className="w-4 h-4 text-accent" />
            <span>PCI-DSS Certified 256-bit SSL</span>
          </div>
        </div>

        {isCancelled && (
          <div className="p-4 rounded-lg bg-accent/10 border border-accent/20 text-accent flex items-center gap-3">
            <AlertCircle className="w-5 h-5 shrink-0" />
            <p className="text-sm">
              Your previous checkout attempt was cancelled. You can review your details and try again whenever you are ready.
            </p>
          </div>
        )}

        <div className="grid grid-cols-1 md:grid-cols-5 gap-6">
          {/* Order Details Column */}
          <Card className="md:col-span-3 border-border">
            <CardHeader>
              <div className="flex items-center justify-between">
                <span className="text-xs uppercase tracking-wider font-semibold text-accent">
                  {order.category === "product_order" ? "Product Order" : "Department Settlement"}
                </span>
                <span className="text-xs text-muted-foreground font-mono">{order.order_number}</span>
              </div>
              <CardTitle className="text-xl mt-1">{order.title}</CardTitle>
              <CardDescription>
                Reference: {order.reference_code} • Created {new Date(order.created_at).toLocaleDateString()}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2 border-t border-b border-border py-4">
                <div className="flex justify-between text-sm">
                  <span className="text-muted-foreground">Subtotal</span>
                  <span>{formatPhpFromMinor(order.amount_minor)}</span>
                </div>
                <div className="flex justify-between text-sm">
                  <span className="text-muted-foreground">Gateway Processing Fee (PayMongo)</span>
                  <span className="text-accent font-medium">Free / Covered</span>
                </div>
                <div className="flex justify-between text-base font-bold pt-2 border-t border-border">
                  <span>Total Due</span>
                  <span className="text-accent text-lg">{formatPhpFromMinor(order.amount_minor)}</span>
                </div>
              </div>

              {/* Channel Selector */}
              <div>
                <h3 className="text-sm font-semibold mb-3">Select Payment Method Channel</h3>
                <div className="space-y-2" role="radiogroup" aria-label="Payment method channels">
                  {paymentChannels.map((ch) => {
                    const Icon = ch.icon
                    const isSelected = selectedChannel === ch.id
                    return (
                      <div
                        key={ch.id}
                        onClick={() => setSelectedChannel(ch.id)}
                        role="radio"
                        aria-checked={isSelected}
                        tabIndex={0}
                        onKeyDown={(e) => {
                          if (e.key === " " || e.key === "Enter") {
                            setSelectedChannel(ch.id)
                          }
                        }}
                        className={`flex items-center justify-between p-3 rounded-lg border cursor-pointer transition-all ${
                          isSelected
                            ? "border-accent bg-accent/5 ring-1 ring-accent"
                            : "border-border hover:bg-muted/50"
                        }`}
                      >
                        <div className="flex items-center gap-3">
                          <Icon className={`w-5 h-5 ${isSelected ? "text-accent" : "text-muted-foreground"}`} />
                          <div>
                            <div className="flex items-center gap-2">
                              <p className="text-sm font-medium text-foreground">{ch.name}</p>
                              {ch.recommended && (
                                <span className="text-[10px] bg-accent/10 text-accent font-semibold px-2 py-0.5 rounded">
                                  Recommended
                                </span>
                              )}
                            </div>
                            <p className="text-xs text-muted-foreground">{ch.desc}</p>
                          </div>
                        </div>
                        {isSelected && <CheckCircle2 className="w-4 h-4 text-accent" />}
                      </div>
                    )
                  })}
                </div>
              </div>
            </CardContent>
            <CardFooter className="pt-2">
              <CheckoutButton
                orderId={order.id}
                orderNumber={order.order_number}
                amountMinor={order.amount_minor}
                paymentMethod={selectedChannel}
                className="w-full py-6 text-base"
              />
            </CardFooter>
          </Card>

          {/* Security & Invariant Guarantee Sidebar */}
          <div className="md:col-span-2 space-y-4">
            <Card className="border-border bg-card/60">
              <CardHeader className="pb-3">
                <CardTitle className="text-sm font-semibold flex items-center gap-2">
                  <ShieldCheck className="w-4 h-4 text-accent" />
                  Live Orchestration Invariants
                </CardTitle>
              </CardHeader>
              <CardContent className="text-xs text-muted-foreground space-y-3">
                <div className="flex gap-2">
                  <span className="font-bold text-accent">1.</span>
                  <span>Authoritative server-side price calculation in centavos (PHP minor currency units).</span>
                </div>
                <div className="flex gap-2">
                  <span className="font-bold text-accent">2.</span>
                  <span>Unique idempotency key prevents duplicate charges or checkout sessions.</span>
                </div>
                <div className="flex gap-2">
                  <span className="font-bold text-accent">3.</span>
                  <span>Order is only marked paid when a signed HMAC-SHA256 webhook arrives from PayMongo.</span>
                </div>
              </CardContent>
            </Card>

            <div className="p-4 rounded-lg border border-border text-xs text-muted-foreground space-y-1">
              <p className="font-medium text-foreground">Need help or invoicing assistance?</p>
              <p>Contact your Acme Finance Officer at billing@acme.com</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export default function CheckoutPage() {
  return (
    <Suspense fallback={<div className="p-8 text-center text-sm">Loading Checkout...</div>}>
      <CheckoutContent />
    </Suspense>
  )
}


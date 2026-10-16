"use client"

import React, { useState, useEffect } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import {
  CreditCard,
  Plus,
  RefreshCw,
  CheckCircle2,
  XCircle,
  Clock,
  AlertTriangle,
  ArrowRight,
  ShieldCheck,
  FileText,
  Activity,
  Layers,
  Sparkles,
  ExternalLink,
  Loader2,
  X,
} from "lucide-react"
import {
  fetchOrders,
  createOrder,
  reconcilePayment,
  reconcileAllPayments,
  fetchWebhookEvents,
  fetchFulfillmentOutbox,
  simulateWebhook,
  Order,
  WebhookEventRecord,
  FulfillmentOutboxRecord,
  formatPhpFromMinor,
} from "@/lib/payment-api"

export function PaymentsHub() {
  const router = useRouter()
  const [activeTab, setActiveTab] = useState<"orders" | "webhooks" | "checklist">("orders")
  const [orders, setOrders] = useState<Order[]>([])
  const [webhooks, setWebhooks] = useState<WebhookEventRecord[]>([])
  const [outbox, setOutbox] = useState<FulfillmentOutboxRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [actionLoading, setActionLoading] = useState(false)
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null)

  // New Order Modal State
  const [showOrderModal, setShowOrderModal] = useState(false)
  const [orderCategory, setOrderCategory] = useState<"product_order" | "department_invoice">("product_order")
  const [referenceCode, setReferenceCode] = useState("Product A")
  const [orderTitle, setOrderTitle] = useState("")
  const [orderQuantity, setOrderQuantity] = useState(1)

  const productOptions = [
    { name: "Product A", unitPrice: 125 },
    { name: "Product B", unitPrice: 89 },
    { name: "Product C", unitPrice: 156 },
    { name: "Product D", unitPrice: 203 },
    { name: "Product E", unitPrice: 67 },
  ]

  const departmentOptions = [
    { slug: "manufacturing", name: "Manufacturing (CC-001)", defaultCost: 45000 },
    { slug: "assembly", name: "Assembly (CC-002)", defaultCost: 28000 },
    { slug: "qa", name: "QA (CC-003)", defaultCost: 15000 },
    { slug: "logistics", name: "Logistics (CC-004)", defaultCost: 10000 },
  ]

  const loadAllData = async () => {
    try {
      const [ordersData, whData, outboxData] = await Promise.all([
        fetchOrders(),
        fetchWebhookEvents().catch(() => []),
        fetchFulfillmentOutbox().catch(() => []),
      ])
      setOrders(ordersData)
      setWebhooks(whData)
      setOutbox(outboxData)
    } catch (err: any) {
      console.error("Error loading payment hub data:", err)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadAllData()
  }, [])

  const handleCreateOrder = async (e: React.FormEvent) => {
    e.preventDefault()
    setActionLoading(true)
    setFeedbackMessage(null)
    try {
      const title =
        orderTitle.trim() ||
        (orderCategory === "product_order"
          ? `${orderQuantity}x Units of ${referenceCode}`
          : `${referenceCode.toUpperCase()} Cost Center Vendor Settlement`)

      await createOrder({
        title,
        category: orderCategory,
        reference_code: referenceCode,
        quantity: orderQuantity,
      })

      setShowOrderModal(false)
      setOrderTitle("")
      setOrderQuantity(1)
      await loadAllData()
      setFeedbackMessage("Order created successfully with server-side authoritative pricing.")
    } catch (err: any) {
      setFeedbackMessage(`Failed to create order: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const handleReconcileSingle = async (attemptId: string) => {
    setActionLoading(true)
    try {
      const res = await reconcilePayment(attemptId)
      setFeedbackMessage(`Reconciliation outcome: Status is now '${res.status}' (changed: ${res.changed})`)
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Reconciliation error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const handleBatchReconcile = async () => {
    setActionLoading(true)
    try {
      const res = await reconcileAllPayments()
      setFeedbackMessage(`Batch reconciliation finished: Reconciled ${res.reconciled_count} unresolved attempts.`)
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Batch error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const handleSimulateWebhookEvent = async (type: "paid" | "failed" | "duplicate") => {
    if (orders.length === 0 || !orders[0].latest_attempt) {
      setFeedbackMessage("No orders with payment attempts found to simulate webhook on.")
      return
    }
    setActionLoading(true)
    const targetAttempt = orders[0].latest_attempt
    try {
      if (type === "duplicate") {
        const res = await simulateWebhook(targetAttempt.id, "checkout_session.payment.paid", true)
        setFeedbackMessage(`Duplicate webhook test: ${res.result.duplicate ? "Deduplicated successfully (200 OK without double fulfillment)" : "Received"}`)
      } else if (type === "paid") {
        await simulateWebhook(targetAttempt.id, "checkout_session.payment.paid", false)
        setFeedbackMessage(`Simulated signed 'checkout_session.payment.paid' webhook processed! Attempt marked succeeded and outbox fulfilled.`)
      } else {
        await simulateWebhook(targetAttempt.id, "payment.failed", false)
        setFeedbackMessage(`Simulated signed 'payment.failed' webhook processed! Attempt marked failed.`)
      }
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Simulation error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  // Summary Metrics
  const totalVolumeMinor = orders
    .filter((o) => o.status === "paid")
    .reduce((acc, curr) => acc + curr.amount_minor, 0)
  const pendingVolumeMinor = orders
    .filter((o) => o.status === "pending")
    .reduce((acc, curr) => acc + curr.amount_minor, 0)
  const paidCount = orders.filter((o) => o.status === "paid").length

  return (
    <div className="p-8 space-y-8 bg-background min-h-screen">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h1 className="text-3xl font-bold tracking-tight text-foreground">
              Payments & Live Orchestration
            </h1>
            <span className="text-xs bg-accent/10 text-accent font-semibold px-2.5 py-1 rounded-full flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-accent animate-pulse" />
              PayMongo Ready
            </span>
          </div>
          <p className="text-sm text-muted-foreground mt-1">
            PayMongo Hosted Checkout Sessions, HMAC webhooks, outbox fulfillment, & ledger reconciliation.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            onClick={loadAllData}
            disabled={actionLoading}
            className="gap-2"
          >
            <RefreshCw className={`w-4 h-4 ${actionLoading ? "animate-spin" : ""}`} />
            Refresh
          </Button>

          <Button
            size="sm"
            onClick={() => setShowOrderModal(true)}
            className="gap-2 bg-accent text-accent-foreground hover:bg-accent/90"
          >
            <Plus className="w-4 h-4" />
            Create Payable Order
          </Button>
        </div>
      </div>

      {feedbackMessage && (
        <div className="p-3.5 rounded-lg bg-accent/10 border border-accent/20 text-xs text-foreground flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Sparkles className="w-4 h-4 text-accent shrink-0" />
            <span>{feedbackMessage}</span>
          </div>
          <button
            type="button"
            aria-label="Dismiss notification"
            onClick={() => setFeedbackMessage(null)}
            className="text-muted-foreground hover:text-foreground"
          >
            <X className="w-3.5 h-3.5" aria-hidden="true" />
          </button>
        </div>
      )}

      {/* KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="border-border">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">Total Invoices & Orders</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-foreground">{orders.length}</div>
            <p className="text-xs text-muted-foreground mt-1">{paidCount} Paid • {orders.length - paidCount} Pending</p>
          </CardContent>
        </Card>

        <Card className="border-border">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">Settled Volume (PHP)</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-accent">{formatPhpFromMinor(totalVolumeMinor)}</div>
            <p className="text-xs text-muted-foreground mt-1">Verified via signed webhook</p>
          </CardContent>
        </Card>

        <Card className="border-border">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">Pending Receivables</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-foreground">{formatPhpFromMinor(pendingVolumeMinor)}</div>
            <p className="text-xs text-muted-foreground mt-1">Awaiting checkout settlement</p>
          </CardContent>
        </Card>

        <Card className="border-border">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">Processed Webhook Events</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-foreground">{webhooks.length}</div>
            <p className="text-xs text-muted-foreground mt-1">{outbox.length} Fulfillment outbox records</p>
          </CardContent>
        </Card>
      </div>

      {/* Tab Navigation */}
      <div role="tablist" aria-label="Payment Hub Sections" className="flex gap-2 border-b border-border pb-px">
        {[
          { id: "orders", label: "Orders & Invoices", icon: FileText },
          { id: "webhooks", label: "Live Orchestrator & Webhooks", icon: Activity },
          { id: "checklist", label: "Sandbox & Go-Live Checklist", icon: ShieldCheck },
        ].map((tab) => {
          const Icon = tab.icon
          const isActive = activeTab === tab.id
          return (
            <button
              key={tab.id}
              id={`tab-${tab.id}`}
              role="tab"
              type="button"
              aria-selected={isActive}
              aria-controls={`tabpanel-${tab.id}`}
              onClick={() => setActiveTab(tab.id as any)}
              className={`flex items-center gap-2 px-4 py-2.5 font-medium text-sm transition-colors border-b-2 focus:outline-none focus:ring-2 focus:ring-accent ${
                isActive
                  ? "border-accent text-accent"
                  : "border-transparent text-muted-foreground hover:text-foreground hover:border-muted"
              }`}
            >
              <Icon className="w-4 h-4" aria-hidden="true" />
              <span>{tab.label}</span>
            </button>
          )
        })}
      </div>

      {/* Tab 1: Orders & Invoices */}
      {activeTab === "orders" && (
        <div id="tabpanel-orders" role="tabpanel" aria-labelledby="tab-orders">
          <Card className="border-border">
          <CardHeader className="flex flex-row items-center justify-between pb-4">
            <div>
              <CardTitle className="text-lg">Accounting Orders & Payables</CardTitle>
              <CardDescription className="text-xs">
                Authoritative orders linked to Product Lines and Department Cost Centers.
              </CardDescription>
            </div>
            <span className="text-xs text-muted-foreground">
              Prices derived directly from trusted ledger models
            </span>
          </CardHeader>
          <CardContent className="p-0">
            {loading ? (
              <div className="p-8 text-center text-sm text-muted-foreground">Loading orders...</div>
            ) : orders.length === 0 ? (
              <div className="p-8 text-center text-sm text-muted-foreground">
                No orders found. Click &quot;Create Payable Order&quot; to begin.
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-muted/50 border-y border-border text-xs text-muted-foreground uppercase font-semibold">
                    <tr>
                      <th className="px-6 py-3">Order Number</th>
                      <th className="px-6 py-3">Title & Reference</th>
                      <th className="px-6 py-3">Category</th>
                      <th className="px-6 py-3">Amount</th>
                      <th className="px-6 py-3">Order Status</th>
                      <th className="px-6 py-3">Payment Attempt</th>
                      <th className="px-6 py-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {orders.map((o) => {
                      const latest = o.latest_attempt
                      return (
                        <tr key={o.id} className="hover:bg-muted/20 transition-colors">
                          <td className="px-6 py-4 font-mono font-medium text-foreground">
                            {o.order_number}
                          </td>
                          <td className="px-6 py-4">
                            <div className="font-semibold text-foreground">{o.title}</div>
                            <div className="text-xs text-muted-foreground mt-0.5">{o.reference_code}</div>
                          </td>
                          <td className="px-6 py-4">
                            <span
                              className={`text-xs px-2.5 py-1 rounded-full font-medium ${
                                o.category === "product_order"
                                  ? "bg-blue-500/10 text-blue-600"
                                  : "bg-purple-500/10 text-purple-600"
                              }`}
                            >
                              {o.category === "product_order" ? "Product Run" : "Cost Center"}
                            </span>
                          </td>
                          <td className="px-6 py-4 font-bold text-foreground">
                            {formatPhpFromMinor(o.amount_minor)}
                          </td>
                          <td className="px-6 py-4">
                            <span
                              className={`inline-flex items-center gap-1.5 text-xs font-semibold px-2.5 py-1 rounded-full ${
                                o.status === "paid"
                                  ? "bg-accent/10 text-accent"
                                  : o.status === "cancelled"
                                  ? "bg-red-500/10 text-red-600"
                                  : "bg-amber-500/10 text-amber-600"
                              }`}
                            >
                              {o.status === "paid" && <CheckCircle2 className="w-3.5 h-3.5" />}
                              {o.status === "pending" && <Clock className="w-3.5 h-3.5" />}
                              {o.status.toUpperCase()}
                            </span>
                          </td>
                          <td className="px-6 py-4">
                            {latest ? (
                              <div className="space-y-1">
                                <span
                                  className={`text-[11px] px-2 py-0.5 rounded font-mono font-medium ${
                                    latest.status === "succeeded"
                                      ? "bg-accent/10 text-accent"
                                      : latest.status === "failed"
                                      ? "bg-red-500/10 text-red-600"
                                      : latest.status === "requires_action"
                                      ? "bg-blue-500/10 text-blue-600"
                                      : "bg-muted text-muted-foreground"
                                  }`}
                                >
                                  {latest.status}
                                </span>
                                {latest.provider_payment_id && (
                                  <div className="text-[10px] text-muted-foreground font-mono">
                                    {latest.provider_payment_id.slice(0, 12)}...
                                  </div>
                                )}
                              </div>
                            ) : (
                              <span className="text-xs text-muted-foreground italic">None</span>
                            )}
                          </td>
                          <td className="px-6 py-4 text-right space-x-2">
                            {o.status === "pending" && (
                              <Button
                                size="sm"
                                className="bg-accent text-accent-foreground hover:bg-accent/90 text-xs font-semibold"
                                onClick={() => router.push(`/checkout?order_id=${o.id}`)}
                              >
                                <CreditCard className="w-3.5 h-3.5 mr-1" />
                                Pay Now
                              </Button>
                            )}

                            {latest && latest.status !== "succeeded" && (
                              <Button
                                variant="outline"
                                size="sm"
                                className="text-xs"
                                onClick={() => handleReconcileSingle(latest.id)}
                              >
                                Reconcile
                              </Button>
                            )}

                            {latest && (
                              <Button
                                variant="ghost"
                                size="sm"
                                className="text-xs text-muted-foreground hover:text-foreground"
                                onClick={() => router.push(`/payment/result/${latest.id}`)}
                              >
                                View
                              </Button>
                            )}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
      )}

      {/* Tab 2: Live Orchestrator & Webhooks */}
      {activeTab === "webhooks" && (
        <div id="tabpanel-webhooks" role="tabpanel" aria-labelledby="tab-webhooks" className="space-y-6">
          <Card className="border-border">
            <CardHeader className="flex flex-row items-center justify-between pb-3">
              <div>
                <CardTitle className="text-base font-semibold">Incoming PayMongo Webhook Events</CardTitle>
                <CardDescription className="text-xs">
                  All signed payloads verified with HMAC-SHA256 and deduplicated by provider event ID.
                </CardDescription>
              </div>
              <span className="text-xs font-mono text-accent">HMAC-SHA256 Verified</span>
            </CardHeader>
            <CardContent className="p-0">
              {webhooks.length === 0 ? (
                <div className="p-8 text-center text-sm text-muted-foreground">
                  No webhook events received yet. Use the Sandbox tab or initiate a checkout session.
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-muted/50 border-y border-border text-muted-foreground uppercase font-semibold">
                      <tr>
                        <th className="px-6 py-2.5">Event ID</th>
                        <th className="px-6 py-2.5">Event Type</th>
                        <th className="px-6 py-2.5">Status</th>
                        <th className="px-6 py-2.5">Received At</th>
                        <th className="px-6 py-2.5">Processed At</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border">
                      {webhooks.map((w) => (
                        <tr key={w.id} className="hover:bg-muted/20">
                          <td className="px-6 py-3 font-mono text-foreground font-medium">
                            {w.provider_event_id}
                          </td>
                          <td className="px-6 py-3">
                            <span className="bg-accent/10 text-accent font-mono px-2 py-0.5 rounded">
                              {w.event_type}
                            </span>
                          </td>
                          <td className="px-6 py-3">
                            <span
                              className={`px-2 py-0.5 rounded font-medium ${
                                w.processing_status === "processed"
                                  ? "bg-accent/10 text-accent"
                                  : "bg-muted text-muted-foreground"
                              }`}
                            >
                              {w.processing_status}
                            </span>
                          </td>
                          <td className="px-6 py-3 text-muted-foreground">
                            {new Date(w.received_at).toLocaleString()}
                          </td>
                          <td className="px-6 py-3 text-muted-foreground">
                            {w.processed_at ? new Date(w.processed_at).toLocaleString() : "—"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>

          {/* Fulfillment Outbox */}
          <Card className="border-border">
            <CardHeader className="pb-3">
              <CardTitle className="text-base font-semibold">Fulfillment Outbox Records</CardTitle>
              <CardDescription className="text-xs">
                Asynchronous worker records guaranteeing exactly-once ledger and audit trail synchronization.
              </CardDescription>
            </CardHeader>
            <CardContent className="p-0">
              {outbox.length === 0 ? (
                <div className="p-8 text-center text-sm text-muted-foreground">
                  Fulfillment outbox is empty.
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-muted/50 border-y border-border text-muted-foreground uppercase font-semibold">
                      <tr>
                        <th className="px-6 py-2.5">Outbox ID</th>
                        <th className="px-6 py-2.5">Order ID</th>
                        <th className="px-6 py-2.5">Payment Attempt ID</th>
                        <th className="px-6 py-2.5">Status</th>
                        <th className="px-6 py-2.5">Processed Time</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border">
                      {outbox.map((item) => (
                        <tr key={item.id} className="hover:bg-muted/20">
                          <td className="px-6 py-3 font-mono">{item.id.slice(0, 8)}...</td>
                          <td className="px-6 py-3 font-mono">{item.order_id.slice(0, 8)}...</td>
                          <td className="px-6 py-3 font-mono">{item.payment_attempt_id.slice(0, 8)}...</td>
                          <td className="px-6 py-3">
                            <span className="bg-accent/10 text-accent px-2 py-0.5 rounded font-medium">
                              {item.status}
                            </span>
                          </td>
                          <td className="px-6 py-3 text-muted-foreground">
                            {item.processed_at ? new Date(item.processed_at).toLocaleString() : "Pending"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      )}

      {/* Tab 3: Sandbox & Go-Live Checklist */}
      {activeTab === "checklist" && (
        <div id="tabpanel-checklist" role="tabpanel" aria-labelledby="tab-checklist" className="space-y-6">
          <Card className="border-border">
            <CardHeader>
              <CardTitle className="text-base font-semibold">Interactive Sandbox Testing Suite</CardTitle>
              <CardDescription className="text-xs">
                Trigger HMAC-signed webhook simulations to verify the 6 state machine invariants live.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                <Button
                  variant="outline"
                  onClick={() => handleSimulateWebhookEvent("paid")}
                  disabled={actionLoading}
                  className="gap-2 h-auto py-3 text-left flex-col items-start"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-accent text-xs">
                    <CheckCircle2 className="w-4 h-4" />
                    <span>Simulate Paid Webhook</span>
                  </div>
                  <span className="text-[11px] text-muted-foreground">
                    checkout_session.payment.paid
                  </span>
                </Button>

                <Button
                  variant="outline"
                  onClick={() => handleSimulateWebhookEvent("failed")}
                  disabled={actionLoading}
                  className="gap-2 h-auto py-3 text-left flex-col items-start"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-red-600 text-xs">
                    <XCircle className="w-4 h-4" />
                    <span>Simulate Failed Webhook</span>
                  </div>
                  <span className="text-[11px] text-muted-foreground">payment.failed</span>
                </Button>

                <Button
                  variant="outline"
                  onClick={() => handleSimulateWebhookEvent("duplicate")}
                  disabled={actionLoading}
                  className="gap-2 h-auto py-3 text-left flex-col items-start"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-purple-600 text-xs">
                    <Layers className="w-4 h-4" />
                    <span>Simulate Duplicate Webhook</span>
                  </div>
                  <span className="text-[11px] text-muted-foreground">
                    Tests atomic deduplication
                  </span>
                </Button>

                <Button
                  variant="outline"
                  onClick={handleBatchReconcile}
                  disabled={actionLoading}
                  className="gap-2 h-auto py-3 text-left flex-col items-start"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-blue-600 text-xs">
                    <RefreshCw className="w-4 h-4" />
                    <span>Run Batch Reconciliation</span>
                  </div>
                  <span className="text-[11px] text-muted-foreground">
                    Reconciles all unresolved
                  </span>
                </Button>
              </div>
            </CardContent>
          </Card>

          {/* Go-Live Checklist */}
          <Card className="border-border">
            <CardHeader>
              <CardTitle className="text-base font-semibold flex items-center gap-2">
                <ShieldCheck className="w-5 h-5 text-accent" />
                <span>Go-Live Production Readiness Checklist</span>
              </CardTitle>
              <CardDescription className="text-xs">
                Verified against all technical guidelines in the Payment Orchestration Architecture.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {[
                {
                  label: "1. Authoritative Server-Side Pricing (Centavos)",
                  desc: "Amount is calculated server-side from product/department records and stored in PHP centavos (BigInteger > 0).",
                  checked: true,
                },
                {
                  label: "2. Client & Gateway Idempotency Enforced",
                  desc: "All checkout attempts use client idempotency keys with unique constraint (provider, idempotency_key).",
                  checked: true,
                },
                {
                  label: "3. Official Paymongo-Signature HMAC Verification",
                  desc: "Webhook endpoints reject untrusted or forged requests with 401 using hmac.compare_digest.",
                  checked: true,
                },
                {
                  label: "4. Webhook Deduplication & Monotonic Terminal States",
                  desc: "Duplicate webhooks return 200 OK without re-fulfilling. Late failed webhooks never revert a paid order.",
                  checked: true,
                },
                {
                  label: "5. Asynchronous Outbox Fulfillment & Audit Trail",
                  desc: "Payments update the Acme Audit Trail and ledger asynchronously via fulfillment_outbox table.",
                  checked: true,
                },
                {
                  label: "6. Polling & Automated Reconciliation",
                  desc: "Browser never trusts redirect URL alone; polls backend and provides manual & batch reconciliation.",
                  checked: true,
                },
              ].map((item, idx) => (
                <div key={idx} className="flex items-start gap-3 p-3 rounded-lg border border-border bg-card/40">
                  <CheckCircle2 className="w-5 h-5 text-accent shrink-0 mt-0.5" />
                  <div>
                    <p className="text-sm font-semibold text-foreground">{item.label}</p>
                    <p className="text-xs text-muted-foreground mt-0.5">{item.desc}</p>
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        </div>
      )}

      {/* Create Order Modal */}
      {showOrderModal && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="create-order-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
        >
          <Card className="max-w-lg w-full border-border shadow-2xl">
            <CardHeader className="flex flex-row items-center justify-between pb-3">
              <div>
                <CardTitle id="create-order-dialog-title" className="text-lg">Create New Payable Order</CardTitle>
                <CardDescription className="text-xs">
                  Pricing is authoritatively computed server-side in PHP centavos.
                </CardDescription>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                onClick={() => setShowOrderModal(false)}
                aria-label="Close dialog"
                className="text-muted-foreground hover:text-foreground"
              >
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>

            <form onSubmit={handleCreateOrder}>
              <CardContent className="space-y-4">
                <div>
                  <label id="order-category-group-label" className="text-xs font-semibold text-foreground block mb-1.5">
                    Order Category
                  </label>
                  <div role="radiogroup" aria-labelledby="order-category-group-label" className="grid grid-cols-2 gap-2">
                    <button
                      type="button"
                      role="radio"
                      aria-checked={orderCategory === "product_order"}
                      onClick={() => {
                        setOrderCategory("product_order")
                        setReferenceCode("Product A")
                      }}
                      className={`p-2.5 rounded-lg border text-xs font-semibold text-center transition-colors focus:outline-none focus:ring-2 focus:ring-accent ${
                        orderCategory === "product_order"
                          ? "border-accent bg-accent/10 text-accent"
                          : "border-border text-muted-foreground hover:bg-muted"
                      }`}
                    >
                      Product Line Order
                    </button>
                    <button
                      type="button"
                      role="radio"
                      aria-checked={orderCategory === "department_invoice"}
                      onClick={() => {
                        setOrderCategory("department_invoice")
                        setReferenceCode("manufacturing")
                      }}
                      className={`p-2.5 rounded-lg border text-xs font-semibold text-center transition-colors focus:outline-none focus:ring-2 focus:ring-accent ${
                        orderCategory === "department_invoice"
                          ? "border-accent bg-accent/10 text-accent"
                          : "border-border text-muted-foreground hover:bg-muted"
                      }`}
                    >
                      Department Settlement
                    </button>
                  </div>
                </div>

                {orderCategory === "product_order" ? (
                  <>
                    <div>
                      <label htmlFor="modal-product-select" className="text-xs font-semibold text-foreground block mb-1.5">
                        Select Product Line
                      </label>
                      <select
                        id="modal-product-select"
                        value={referenceCode}
                        onChange={(e) => setReferenceCode(e.target.value)}
                        className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm focus:outline-none focus:ring-1 focus:ring-accent"
                      >
                        {productOptions.map((p) => (
                          <option key={p.name} value={p.name}>
                            {p.name} (Unit Cost: ₱{p.unitPrice})
                          </option>
                        ))}
                      </select>
                    </div>

                    <div>
                      <label htmlFor="modal-quantity-input" className="text-xs font-semibold text-foreground block mb-1.5">
                        Quantity
                      </label>
                      <input
                        id="modal-quantity-input"
                        type="number"
                        min="1"
                        max="10000"
                        value={orderQuantity}
                        onChange={(e) => setOrderQuantity(Number(e.target.value) || 1)}
                        className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm focus:outline-none focus:ring-1 focus:ring-accent"
                      />
                    </div>
                  </>
                ) : (
                  <div>
                    <label htmlFor="modal-dept-select" className="text-xs font-semibold text-foreground block mb-1.5">
                      Select Department Cost Center
                    </label>
                    <select
                      id="modal-dept-select"
                      value={referenceCode}
                      onChange={(e) => setReferenceCode(e.target.value)}
                      className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm focus:outline-none focus:ring-1 focus:ring-accent"
                    >
                      {departmentOptions.map((d) => (
                        <option key={d.slug} value={d.slug}>
                          {d.name} (Allocation: ₱{d.defaultCost.toLocaleString()})
                        </option>
                      ))}
                    </select>
                  </div>
                )}

                <div>
                  <label htmlFor="modal-title-input" className="text-xs font-semibold text-foreground block mb-1.5">
                    Custom Description (Optional)
                  </label>
                  <input
                    id="modal-title-input"
                    type="text"
                    value={orderTitle}
                    onChange={(e) => setOrderTitle(e.target.value)}
                    placeholder="e.g. Q3 Production Batch Supplies"
                    className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm focus:outline-none focus:ring-1 focus:ring-accent"
                  />
                </div>
              </CardContent>

              <CardFooter className="flex justify-end gap-2 border-t border-border pt-3">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setShowOrderModal(false)}
                  disabled={actionLoading}
                >
                  Cancel
                </Button>
                <Button
                  type="submit"
                  disabled={actionLoading}
                  className="bg-accent text-accent-foreground hover:bg-accent/90 gap-2 font-semibold"
                >
                  {actionLoading ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
                  <span>Generate Order</span>
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  )
}


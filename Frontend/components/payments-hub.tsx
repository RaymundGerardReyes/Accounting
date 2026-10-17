"use client"

import React, { useState, useEffect, useMemo } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import {
  CreditCard,
  Plus,
  RefreshCw,
  CheckCircle2,
  XCircle,
  Clock,
  ShieldCheck,
  FileText,
  Activity,
  Layers,
  Sparkles,
  Loader2,
  Search,
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
import { CurrencyDisplay, FinancialStatusBadge } from "@/components/ui/financial-primitives"

export function PaymentsHub() {
  const router = useRouter()
  const [activeTab, setActiveTab] = useState<"orders" | "webhooks" | "checklist">("orders")
  const [orders, setOrders] = useState<Order[]>([])
  const [webhooks, setWebhooks] = useState<WebhookEventRecord[]>([])
  const [outbox, setOutbox] = useState<FulfillmentOutboxRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [actionLoading, setActionLoading] = useState(false)
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null)

  // Search & Status Filter State for Orders Table
  const [searchQuery, setSearchQuery] = useState("")
  const [statusFilter, setStatusFilter] = useState<"all" | "pending" | "paid" | "cancelled">("all")

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

  // Filtered Orders Calculation
  const filteredOrders = useMemo(() => {
    return orders.filter((order) => {
      const matchesStatus = statusFilter === "all" || order.status === statusFilter
      const query = searchQuery.trim().toLowerCase()
      const matchesSearch =
        !query ||
        order.order_number.toLowerCase().includes(query) ||
        order.title.toLowerCase().includes(query) ||
        order.reference_code.toLowerCase().includes(query)
      return matchesStatus && matchesSearch
    })
  }, [orders, statusFilter, searchQuery])

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
      setFeedbackMessage("Order created with authoritative server-side PHP centavo pricing.")
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
      setFeedbackMessage(`Reconciliation complete: Status is now '${res.status}' (changed: ${res.changed})`)
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
      setFeedbackMessage("No orders with payment attempts found to simulate a webhook on.")
      return
    }
    setActionLoading(true)
    const targetAttempt = orders[0].latest_attempt
    try {
      if (type === "duplicate") {
        const res = await simulateWebhook(targetAttempt.id, "checkout_session.payment.paid", true)
        setFeedbackMessage(
          `Duplicate webhook test: ${
            res.result.duplicate ? "Deduplicated cleanly (200 OK without double fulfillment)" : "Received"
          }`
        )
      } else if (type === "paid") {
        await simulateWebhook(targetAttempt.id, "checkout_session.payment.paid", false)
        setFeedbackMessage(
          "Simulated signed 'checkout_session.payment.paid' webhook processed. Order marked paid."
        )
      } else {
        await simulateWebhook(targetAttempt.id, "payment.failed", false)
        setFeedbackMessage("Simulated signed 'payment.failed' webhook processed. Attempt marked failed.")
      }
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Simulation error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const totalVolumeMinor = orders
    .filter((o) => o.status === "paid")
    .reduce((acc, curr) => acc + curr.amount_minor, 0)

  const pendingVolumeMinor = orders
    .filter((o) => o.status === "pending")
    .reduce((acc, curr) => acc + curr.amount_minor, 0)

  const paidCount = orders.filter((o) => o.status === "paid").length

  return (
    <div className="p-8 space-y-8 max-w-7xl mx-auto">
      {/* Minimalist Header */}
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-4 border-b border-border pb-6">
        <div>
          <div className="flex items-center gap-2.5">
            <p className="text-xs font-mono uppercase tracking-widest text-muted-foreground">
              Payment Gateway Engine
            </p>
            <span className="inline-flex items-center gap-1.5 text-[10px] font-mono uppercase px-2 py-0.5 rounded-full bg-accent/10 text-accent border border-accent/20 font-semibold">
              <span className="w-1.5 h-1.5 rounded-full bg-accent animate-pulse" />
              PayMongo Ready
            </span>
          </div>
          <h1 className="text-3xl font-bold tracking-tight text-foreground mt-1">
            Payments & Live Orchestration
          </h1>
        </div>
        <div className="flex items-center gap-2.5">
          <Button
            variant="outline"
            size="sm"
            onClick={loadAllData}
            disabled={actionLoading}
            className="gap-2 border-border hover:bg-muted text-foreground"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${actionLoading ? "animate-spin" : ""}`} />
            <span>Refresh</span>
          </Button>
          <Button
            size="sm"
            onClick={() => setShowOrderModal(true)}
            className="gap-2 bg-primary text-primary-foreground hover:bg-primary/90"
          >
            <Plus className="w-3.5 h-3.5" />
            <span>Create Payable Order</span>
          </Button>
        </div>
      </div>

      {/* Feedback Banner */}
      {feedbackMessage && (
        <div className="p-3.5 rounded-lg bg-card border border-accent/40 text-xs text-foreground flex items-center justify-between shadow-2xs">
          <div className="flex items-center gap-2.5">
            <Sparkles className="w-4 h-4 text-accent shrink-0" />
            <span className="font-medium">{feedbackMessage}</span>
          </div>
          <button
            type="button"
            aria-label="Dismiss notification"
            onClick={() => setFeedbackMessage(null)}
            className="text-muted-foreground hover:text-foreground p-1"
          >
            <X className="w-3.5 h-3.5" aria-hidden="true" />
          </button>
        </div>
      )}

      {/* KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Invoices & Orders
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              {orders.length}
            </div>
            <p className="text-xs text-muted-foreground mt-1 font-mono">
              {paidCount} Paid · {orders.length - paidCount} Pending
            </p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Settled Volume (PHP)
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-foreground">
              <CurrencyDisplay amountMinor={totalVolumeMinor} currency="PHP" size="xl" className="font-bold text-foreground" />
            </div>
            <p className="text-xs text-muted-foreground mt-1">Verified via signed webhook</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Pending Receivables
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-secondary">
              <CurrencyDisplay amountMinor={pendingVolumeMinor} currency="PHP" size="xl" className="font-bold text-secondary" />
            </div>
            <p className="text-xs text-muted-foreground mt-1">Awaiting checkout settlement</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Webhook Events
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              {webhooks.length}
            </div>
            <p className="text-xs text-muted-foreground mt-1 font-mono">
              {outbox.length} Outbox records
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Minimalist Tab Navigation */}
      <div role="tablist" aria-label="Payment Hub Sections" className="flex gap-6 border-b border-border">
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
              className={`flex items-center gap-2 pb-3 font-medium text-xs uppercase tracking-wider transition-colors border-b-2 -mb-px focus:outline-none ${
                isActive
                  ? "border-accent text-foreground font-semibold"
                  : "border-transparent text-muted-foreground hover:text-foreground"
              }`}
            >
              <Icon className={`w-3.5 h-3.5 ${isActive ? "text-accent" : ""}`} aria-hidden="true" />
              <span>{tab.label}</span>
            </button>
          )
        })}
      </div>

      {/* Tab 1: Orders & Invoices with Search & Filter Bar */}
      {activeTab === "orders" && (
        <div id="tabpanel-orders" role="tabpanel" aria-labelledby="tab-orders" className="space-y-4">
          {/* Search & Status Filter Bar */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 bg-card p-3 rounded-lg border border-border/90">
            <div className="relative flex-1 max-w-sm">
              <Search className="w-4 h-4 text-muted-foreground absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search order #, title, or reference..."
                aria-label="Search orders"
                className="w-full pl-9 pr-8 py-1.5 text-xs border border-border rounded-md bg-background text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery("")}
                  aria-label="Clear search"
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>

            <div className="flex items-center gap-1.5">
              {(["all", "pending", "paid", "cancelled"] as const).map((status) => {
                const active = statusFilter === status
                return (
                  <button
                    key={status}
                    type="button"
                    onClick={() => setStatusFilter(status)}
                    className={`px-3 py-1.5 rounded-md text-xs font-mono uppercase transition-all ${
                      active
                        ? "bg-primary text-primary-foreground font-semibold shadow-2xs"
                        : "bg-muted/50 text-muted-foreground hover:text-foreground hover:bg-muted"
                    }`}
                  >
                    {status}
                  </button>
                )
              })}
            </div>
          </div>

          <Card className="shadow-none border-border/90 overflow-hidden">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <div>
                <CardTitle className="text-base font-semibold">Accounting Orders & Payables</CardTitle>
                <CardDescription className="text-xs">
                  Showing {filteredOrders.length} of {orders.length} authoritative ledger orders
                </CardDescription>
              </div>
              <span className="text-[11px] font-mono text-muted-foreground">
                Server-side centavo ledger
              </span>
            </CardHeader>
            <CardContent className="p-0">
              {loading ? (
                <div className="p-8 text-center text-sm text-muted-foreground">Loading orders...</div>
              ) : filteredOrders.length === 0 ? (
                <div className="p-8 text-center text-sm text-muted-foreground">
                  No orders match your current filter. Try clearing your search query or status filter.
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="bg-muted/40 border-b border-border text-[11px] text-muted-foreground uppercase font-mono">
                      <tr>
                        <th className="px-6 py-3">Order No.</th>
                        <th className="px-6 py-3">Title & Reference</th>
                        <th className="px-6 py-3">Category</th>
                        <th className="px-6 py-3">Amount</th>
                        <th className="px-6 py-3">Order Status</th>
                        <th className="px-6 py-3">Latest Attempt</th>
                        <th className="px-6 py-3 text-right">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/70">
                      {filteredOrders.map((o) => {
                        const latest = o.latest_attempt
                        return (
                          <tr key={o.id} className="hover:bg-muted/30 transition-colors">
                            <td className="px-6 py-4 font-mono text-xs font-medium text-foreground">
                              {o.order_number}
                            </td>
                            <td className="px-6 py-4">
                              <div className="font-semibold text-foreground text-sm">{o.title}</div>
                              <div className="text-xs text-muted-foreground font-mono mt-0.5">
                                {o.reference_code}
                              </div>
                            </td>
                            <td className="px-6 py-4">
                              <span className="text-[11px] font-mono px-2.5 py-1 rounded-md bg-muted text-foreground font-medium">
                                {o.category === "product_order" ? "Product Run" : "Cost Center"}
                              </span>
                            </td>
                            <td className="px-6 py-4">
                              <CurrencyDisplay amountMinor={o.amount_minor} currency="PHP" size="sm" className="font-bold text-foreground" />
                            </td>
                            <td className="px-6 py-4">
                              <FinancialStatusBadge status={o.status} />
                            </td>
                            <td className="px-6 py-4">
                              {latest ? (
                                <div className="space-y-1">
                                  <span
                                    className={`text-[11px] px-2 py-0.5 rounded font-mono font-medium ${
                                      latest.status === "succeeded"
                                        ? "bg-primary/10 text-foreground"
                                        : latest.status === "failed"
                                        ? "bg-accent/10 text-accent"
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
                                <span className="text-xs text-muted-foreground font-mono">—</span>
                              )}
                            </td>
                            <td className="px-6 py-4 text-right space-x-2">
                              {o.status === "pending" && (
                                <Button
                                  size="sm"
                                  className="bg-accent text-accent-foreground hover:bg-accent/90 text-xs font-semibold h-8"
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
                                  className="text-xs h-8"
                                  onClick={() => handleReconcileSingle(latest.id)}
                                >
                                  Reconcile
                                </Button>
                              )}
                              {latest && (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="text-xs text-muted-foreground hover:text-foreground h-8"
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
          <Card className="shadow-none border-border/90 overflow-hidden">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <div>
                <CardTitle className="text-base font-semibold">Incoming PayMongo Webhook Events</CardTitle>
                <CardDescription className="text-xs">
                  Signed payloads verified with HMAC-SHA256 and deduplicated by provider event ID
                </CardDescription>
              </div>
              <span className="text-[11px] font-mono px-2.5 py-1 rounded-full bg-primary text-primary-foreground">
                HMAC-SHA256 Active
              </span>
            </CardHeader>
            <CardContent className="p-0">
              {webhooks.length === 0 ? (
                <div className="p-8 text-center text-sm text-muted-foreground">
                  No webhook events received yet. Use the Sandbox tab or initiate a checkout session.
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs">
                    <thead className="bg-muted/40 border-b border-border text-muted-foreground uppercase font-mono">
                      <tr>
                        <th className="px-6 py-3">Event ID</th>
                        <th className="px-6 py-3">Event Type</th>
                        <th className="px-6 py-3">Status</th>
                        <th className="px-6 py-3">Received At</th>
                        <th className="px-6 py-3">Processed At</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/70">
                      {webhooks.map((w) => (
                        <tr key={w.id} className="hover:bg-muted/30">
                          <td className="px-6 py-3.5 font-mono text-foreground font-medium">
                            {w.provider_event_id}
                          </td>
                          <td className="px-6 py-3.5">
                            <span className="bg-muted text-foreground font-mono px-2 py-0.5 rounded">
                              {w.event_type}
                            </span>
                          </td>
                          <td className="px-6 py-3.5">
                            <span
                              className={`px-2 py-0.5 rounded font-mono font-medium ${
                                w.processing_status === "processed"
                                  ? "bg-primary text-primary-foreground"
                                  : "bg-muted text-muted-foreground"
                              }`}
                            >
                              {w.processing_status}
                            </span>
                          </td>
                          <td className="px-6 py-3.5 text-muted-foreground font-mono">
                            {new Date(w.received_at).toLocaleString()}
                          </td>
                          <td className="px-6 py-3.5 text-muted-foreground font-mono">
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

          <Card className="shadow-none border-border/90 overflow-hidden">
            <CardHeader className="border-b border-border/60 pb-4">
              <CardTitle className="text-base font-semibold">Fulfillment Outbox Records</CardTitle>
              <CardDescription className="text-xs">
                Asynchronous worker records guaranteeing exactly-once ledger synchronization
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
                    <thead className="bg-muted/40 border-b border-border text-muted-foreground uppercase font-mono">
                      <tr>
                        <th className="px-6 py-3">Outbox ID</th>
                        <th className="px-6 py-3">Order ID</th>
                        <th className="px-6 py-3">Attempt ID</th>
                        <th className="px-6 py-3">Status</th>
                        <th className="px-6 py-3">Processed Time</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border/70">
                      {outbox.map((item) => (
                        <tr key={item.id} className="hover:bg-muted/30">
                          <td className="px-6 py-3.5 font-mono">{item.id.slice(0, 8)}...</td>
                          <td className="px-6 py-3.5 font-mono">{item.order_id.slice(0, 8)}...</td>
                          <td className="px-6 py-3.5 font-mono">{item.payment_attempt_id.slice(0, 8)}...</td>
                          <td className="px-6 py-3.5">
                            <span className="bg-primary text-primary-foreground px-2 py-0.5 rounded font-mono font-medium">
                              {item.status}
                            </span>
                          </td>
                          <td className="px-6 py-3.5 text-muted-foreground font-mono">
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
          <Card className="shadow-none border-border/90">
            <CardHeader>
              <CardTitle className="text-base font-semibold">Interactive Sandbox Testing Suite</CardTitle>
              <CardDescription className="text-xs">
                Trigger HMAC-signed webhook simulations to verify payment state machine invariants live
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                <Button
                  variant="outline"
                  onClick={() => handleSimulateWebhookEvent("paid")}
                  disabled={actionLoading}
                  className="gap-1.5 h-auto py-3.5 px-4 text-left flex-col items-start border-border hover:border-foreground"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-foreground text-xs">
                    <CheckCircle2 className="w-4 h-4 text-foreground" />
                    <span>Simulate Paid Webhook</span>
                  </div>
                  <span className="text-[11px] font-mono text-muted-foreground">
                    checkout_session.payment.paid
                  </span>
                </Button>

                <Button
                  variant="outline"
                  onClick={() => handleSimulateWebhookEvent("failed")}
                  disabled={actionLoading}
                  className="gap-1.5 h-auto py-3.5 px-4 text-left flex-col items-start border-border hover:border-accent"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-accent text-xs">
                    <XCircle className="w-4 h-4 text-accent" />
                    <span>Simulate Failed Webhook</span>
                  </div>
                  <span className="text-[11px] font-mono text-muted-foreground">payment.failed</span>
                </Button>

                <Button
                  variant="outline"
                  onClick={() => handleSimulateWebhookEvent("duplicate")}
                  disabled={actionLoading}
                  className="gap-1.5 h-auto py-3.5 px-4 text-left flex-col items-start border-border hover:border-foreground"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-foreground text-xs">
                    <Layers className="w-4 h-4 text-foreground" />
                    <span>Simulate Duplicate</span>
                  </div>
                  <span className="text-[11px] font-mono text-muted-foreground">
                    Tests atomic deduplication
                  </span>
                </Button>

                <Button
                  variant="outline"
                  onClick={handleBatchReconcile}
                  disabled={actionLoading}
                  className="gap-1.5 h-auto py-3.5 px-4 text-left flex-col items-start border-border hover:border-foreground"
                >
                  <div className="flex items-center gap-1.5 font-semibold text-foreground text-xs">
                    <RefreshCw className="w-4 h-4 text-accent" />
                    <span>Batch Reconcile</span>
                  </div>
                  <span className="text-[11px] font-mono text-muted-foreground">
                    Syncs unresolved attempts
                  </span>
                </Button>
              </div>
            </CardContent>
          </Card>

          <Card className="shadow-none border-border/90">
            <CardHeader>
              <CardTitle className="text-base font-semibold flex items-center gap-2">
                <ShieldCheck className="w-4 h-4 text-accent" />
                <span>Go-Live Production Readiness Checklist</span>
              </CardTitle>
              <CardDescription className="text-xs">
                Verified against all technical guidelines in the Payment Orchestration Architecture
              </CardDescription>
            </CardHeader>
            <CardContent className="grid grid-cols-1 md:grid-cols-2 gap-3">
              {[
                {
                  label: "1. Authoritative Server-Side Pricing (Centavos)",
                  desc: "Amount is calculated server-side from product/department records and stored in PHP centavos.",
                },
                {
                  label: "2. Client & Gateway Idempotency Enforced",
                  desc: "All checkout attempts use client idempotency keys with unique constraint (provider, idempotency_key).",
                },
                {
                  label: "3. Official Paymongo-Signature HMAC Verification",
                  desc: "Webhook endpoints reject untrusted or forged requests with 401 using hmac.compare_digest.",
                },
                {
                  label: "4. Webhook Deduplication & Monotonic States",
                  desc: "Duplicate webhooks return 200 OK without re-fulfilling. Late failed webhooks never revert a paid order.",
                },
                {
                  label: "5. Asynchronous Outbox Fulfillment & Audit",
                  desc: "Payments update the Acme Audit Trail and ledger asynchronously via fulfillment_outbox table.",
                },
                {
                  label: "6. Polling & Automated Reconciliation",
                  desc: "Browser never trusts redirect URL alone; polls backend and provides manual & batch reconciliation.",
                },
              ].map((item, idx) => (
                <div key={idx} className="flex items-start gap-3 p-3.5 rounded-lg border border-border bg-muted/20">
                  <CheckCircle2 className="w-4 h-4 text-foreground shrink-0 mt-0.5" />
                  <div>
                    <p className="text-xs font-semibold text-foreground">{item.label}</p>
                    <p className="text-xs text-muted-foreground mt-1 leading-relaxed">{item.desc}</p>
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
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
        >
          <Card className="max-w-lg w-full border-border shadow-xl">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <div>
                <CardTitle id="create-order-dialog-title" className="text-base font-semibold">
                  Create New Payable Order
                </CardTitle>
                <CardDescription className="text-xs">
                  Pricing is authoritatively computed server-side in PHP centavos (₱)
                </CardDescription>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                onClick={() => setShowOrderModal(false)}
                aria-label="Close dialog"
                className="text-muted-foreground hover:text-foreground"
              >
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <form onSubmit={handleCreateOrder}>
              <CardContent className="space-y-4 pt-4">
                <div>
                  <label id="order-category-group-label" className="text-xs font-semibold text-foreground block mb-2">
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
                      className={`p-2.5 rounded-md border text-xs font-semibold text-center transition-all ${
                        orderCategory === "product_order"
                          ? "border-primary bg-primary text-primary-foreground"
                          : "border-border text-muted-foreground hover:text-foreground hover:bg-muted/50"
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
                      className={`p-2.5 rounded-md border text-xs font-semibold text-center transition-all ${
                        orderCategory === "department_invoice"
                          ? "border-primary bg-primary text-primary-foreground"
                          : "border-border text-muted-foreground hover:text-foreground hover:bg-muted/50"
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
                        className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm focus:outline-none focus:ring-2 focus:ring-ring"
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
                        className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm font-mono focus:outline-none focus:ring-2 focus:ring-ring"
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
                      className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm focus:outline-none focus:ring-2 focus:ring-ring"
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
                    placeholder="e.g., Q4 Production Batch Supplies"
                    className="w-full px-3 py-2 border border-border rounded-md bg-card text-foreground text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 border-t border-border/60 pt-4 mt-2">
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


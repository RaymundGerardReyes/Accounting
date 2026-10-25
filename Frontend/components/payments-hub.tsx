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
  Copy,
  Check,
  ExternalLink,
  Play,
  RotateCcw,
  Receipt,
  Download,
  AlertTriangle,
} from "lucide-react"
import {
  fetchOrders,
  createOrder,
  reconcilePayment,
  reconcileAllPayments,
  fetchWebhookEvents,
  fetchFulfillmentOutbox,
  processFulfillmentOutbox,
  simulateWebhook,
  simulatePaymentAttemptWebhook,
  getReceiptPdfUrl,
  expirePayment,
  failPayment,
  createAndExpireTestPayment,
  Order,
  WebhookEventRecord,
  FulfillmentOutboxRecord,
  formatPhpFromMinor,
} from "@/lib/payment-api"
import { CurrencyDisplay, FinancialStatusBadge } from "@/components/ui/financial-primitives"
import { HoverCard, HoverCardTrigger, HoverCardContent } from "@/components/ui/hover-card"

// ============================================================================
// Subcomponents for Modular Performance & Error Isolation
// ============================================================================

interface OrderMobileCardProps {
  order: Order
  copiedKey: string | null
  onCopy: (text: string, key: string) => void
  onReconcile: (attemptId: string) => void
  onView: (attemptId: string) => void
  onPay: (orderId: string) => void
  onExpire?: (attemptId: string) => void
}

function OrderMobileCard({ order, copiedKey, onCopy, onReconcile, onView, onPay, onExpire }: OrderMobileCardProps) {
  const latest = order.latest_attempt
  return (
    <div className="p-4 space-y-3 bg-card hover:bg-muted/10 transition-colors">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2">
          <span className="font-mono text-xs font-semibold text-foreground">
            {order.order_number}
          </span>
          <span className="text-[11px] font-mono px-2.5 py-1 rounded-md bg-muted text-foreground font-medium whitespace-nowrap inline-flex items-center shrink-0">
            {order.category === "product_order" ? "Product Run" : "Cost Center"}
          </span>
          {order.metadata?.source === "google_forms" && (
            <span
              className="text-[10px] font-mono px-2 py-0.5 rounded bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 font-medium inline-flex items-center gap-1 shrink-0"
              title={`Ingested via Google Forms (Response: ${order.metadata.google_form_response_id || "N/A"})`}
            >
              <FileText className="w-3 h-3" />
              Google Form
            </span>
          )}
        </div>
        <FinancialStatusBadge status={order.status} />
      </div>

      <div>
        <div className="font-semibold text-foreground text-sm">{order.title}</div>
        <div className="text-xs text-muted-foreground font-mono mt-0.5 flex items-center gap-1.5 flex-wrap">
          <span>{order.reference_code}</span>
          {(order.metadata?.customer_name || order.metadata?.customer_phone) && (
            <span className="text-[11px] text-muted-foreground font-sans">
              &middot; {order.metadata.customer_name || "Customer"}{order.metadata.customer_phone ? ` (${order.metadata.customer_phone})` : ""}
            </span>
          )}
        </div>
      </div>

      <div className="flex items-center justify-between py-2 px-3 rounded-md bg-muted/40 border border-border/60">
        <span className="text-xs text-muted-foreground font-medium">Order Total:</span>
        <CurrencyDisplay
          amountMinor={order.amount_minor}
          currency="PHP"
          size="sm"
          className="font-bold text-foreground text-sm"
        />
      </div>

      {latest ? (
        <div className="p-3 rounded-md bg-muted/30 border border-border/70 space-y-2.5">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-medium text-muted-foreground flex items-center gap-1.5 font-mono">
              <Activity className="w-3.5 h-3.5 text-accent" />
              Latest Attempt
            </span>
            <span
              className={`text-[10px] px-2 py-0.5 rounded font-mono font-medium uppercase ${
                latest.status === "succeeded"
                  ? "bg-primary/10 text-foreground border border-primary/20"
                  : latest.status === "failed"
                  ? "bg-accent/10 text-accent border border-accent/20"
                  : latest.status === "requires_action"
                  ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20"
                  : latest.status === "expired" || latest.status === "cancelled"
                  ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20"
                  : "bg-muted text-muted-foreground border border-border"
              }`}
            >
              {latest.status}
            </span>
          </div>

          <div className="space-y-1.5 text-xs font-mono">
            <div>
              <div className="text-[10px] text-muted-foreground uppercase tracking-wider">Attempt ID</div>
              <div className="flex items-center justify-between gap-1.5 mt-0.5 bg-background px-2 py-1 rounded border border-border/60 text-[11px]">
                <span className="truncate select-all">{latest.id}</span>
                <button
                  type="button"
                  onClick={() => onCopy(latest.id, `mob-att-${latest.id}`)}
                  className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
                  title="Copy Attempt ID"
                  aria-label="Copy Attempt ID"
                >
                  {copiedKey === `mob-att-${latest.id}` ? (
                    <Check className="w-3.5 h-3.5 text-primary" />
                  ) : (
                    <Copy className="w-3.5 h-3.5" />
                  )}
                </button>
              </div>
            </div>

            {latest.provider_payment_id && (
              <div>
                <div className="text-[10px] text-muted-foreground uppercase tracking-wider">Provider Ref / Session ID</div>
                <div className="flex items-center justify-between gap-1.5 mt-0.5 bg-background px-2 py-1 rounded border border-border/60 text-[11px]">
                  <span className="truncate select-all">{latest.provider_payment_id}</span>
                  <button
                    type="button"
                    onClick={() => onCopy(latest.provider_payment_id || "", `mob-prov-${latest.provider_payment_id}`)}
                    className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
                    title="Copy Provider Payment ID"
                    aria-label="Copy Provider Payment ID"
                  >
                    {copiedKey === `mob-prov-${latest.provider_payment_id}` ? (
                      <Check className="w-3.5 h-3.5 text-primary" />
                    ) : (
                      <Copy className="w-3.5 h-3.5" />
                    )}
                  </button>
                </div>
              </div>
            )}

            <div className="grid grid-cols-2 gap-2 pt-1 border-t border-border/40 text-[11px]">
              <div>
                <span className="text-[10px] text-muted-foreground block">Method:</span>
                <span className="text-foreground uppercase">{latest.payment_method || "PayMongo Checkout"}</span>
              </div>
              <div>
                <span className="text-[10px] text-muted-foreground block">Created:</span>
                <span className="text-muted-foreground">{new Date(latest.created_at).toLocaleString()}</span>
              </div>
            </div>
          </div>

          <Button
            size="sm"
            variant="secondary"
            className="w-full text-xs font-semibold h-8 mt-2 flex items-center justify-center gap-1.5"
            onClick={() => onView(latest.id)}
          >
            <ExternalLink className="w-3.5 h-3.5" />
            <span>Inspect Full Attempt Details</span>
          </Button>
        </div>
      ) : (
        <div className="text-xs text-muted-foreground font-mono py-1">
          No payment attempt recorded yet
        </div>
      )}

      <div className="flex items-center gap-2 pt-2 border-t border-border/50 flex-wrap">
        {order.status === "pending" && (
          <Button
            size="sm"
            className="bg-accent text-accent-foreground hover:bg-accent/90 text-xs font-semibold h-8 flex-1 min-w-[100px] whitespace-nowrap"
            onClick={() => onPay(order.id)}
          >
            <CreditCard className="w-3.5 h-3.5 mr-1 shrink-0" />
            {latest && (latest.status === "failed" || latest.status === "expired" || latest.status === "cancelled")
              ? "Retry Payment"
              : "Pay Now"}
          </Button>
        )}
        {latest && latest.status !== "succeeded" && (
          <Button
            variant="outline"
            size="sm"
            className="text-xs h-8 flex-1 min-w-[90px] whitespace-nowrap"
            onClick={() => onReconcile(latest.id)}
          >
            Reconcile
          </Button>
        )}
        {latest && (latest.status === "pending" || latest.status === "requires_action") && onExpire && (
          <Button
            variant="outline"
            size="sm"
            className="text-xs h-8 flex-1 min-w-[90px] whitespace-nowrap text-amber-600 dark:text-amber-400 border-amber-500/30 hover:bg-amber-500/10 gap-1"
            title="Expire and invalidate this test checkout session"
            onClick={() => onExpire(latest.id)}
          >
            <Clock className="w-3.5 h-3.5" />
            <span>Expire</span>
          </Button>
        )}
        {(order.status === "paid" || latest?.status === "succeeded") && (
          <Button
            variant="outline"
            size="sm"
            className="text-xs h-8 flex-1 min-w-[90px] whitespace-nowrap text-emerald-600 border-emerald-600/30 hover:bg-emerald-500/10 gap-1"
            onClick={() => onView(latest?.id || order.id)}
          >
            <Receipt className="w-3.5 h-3.5" />
            <span>Receipt</span>
          </Button>
        )}
        {latest && (
          <Button
            variant="ghost"
            size="sm"
            className="text-xs text-muted-foreground hover:text-foreground h-8 flex-1 min-w-[80px] whitespace-nowrap"
            onClick={() => onView(latest.id)}
          >
            View
          </Button>
        )}
      </div>
    </div>
  )
}

interface OrderDesktopRowProps {
  order: Order
  copiedKey: string | null
  onCopy: (text: string, key: string) => void
  onReconcile: (attemptId: string) => void
  onView: (attemptId: string) => void
  onPay: (orderId: string) => void
  onExpire?: (attemptId: string) => void
}

function OrderDesktopRow({ order, copiedKey, onCopy, onReconcile, onView, onPay, onExpire }: OrderDesktopRowProps) {
  const latest = order.latest_attempt
  return (
    <tr className="hover:bg-muted/30 transition-colors">
      <td className="px-6 py-4 font-mono text-xs font-medium text-foreground whitespace-nowrap">
        {order.order_number}
      </td>
      <td className="px-6 py-4">
        <div className="font-semibold text-foreground text-sm line-clamp-1">{order.title}</div>
        <div className="text-xs text-muted-foreground font-mono mt-0.5">
          {order.reference_code}
        </div>
      </td>
      <td className="px-6 py-4 whitespace-nowrap">
        <div className="flex items-center gap-1.5 flex-wrap">
          <span className="text-[11px] font-mono px-2.5 py-1 rounded-md bg-muted text-foreground font-medium whitespace-nowrap inline-flex items-center shrink-0">
            {order.category === "product_order" ? "Product Run" : "Cost Center"}
          </span>
          {order.metadata?.source === "google_forms" && (
            <span
              className="text-[10px] font-mono px-2 py-0.5 rounded bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 font-medium inline-flex items-center gap-1 shrink-0"
              title={`Ingested via Google Forms (Response: ${order.metadata.google_form_response_id || "N/A"})`}
            >
              <FileText className="w-3 h-3" />
              Google Form
            </span>
          )}
        </div>
      </td>
      <td className="px-6 py-4 whitespace-nowrap">
        <CurrencyDisplay amountMinor={order.amount_minor} currency="PHP" size="sm" className="font-bold text-foreground" />
      </td>
      <td className="px-6 py-4 whitespace-nowrap">
        <FinancialStatusBadge status={order.status} />
      </td>
      <td className="px-6 py-4 whitespace-nowrap">
        {latest ? (
          <HoverCard openDelay={150} closeDelay={200}>
            <HoverCardTrigger asChild>
              <button
                type="button"
                className="space-y-1 cursor-pointer group text-left focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded p-1 -m-1 transition-colors"
              >
                <div className="flex items-center gap-1.5">
                  <span
                    className={`text-[11px] px-2 py-0.5 rounded font-mono font-medium transition-colors ${
                      latest.status === "succeeded"
                        ? "bg-primary/10 text-foreground group-hover:bg-primary/20"
                        : latest.status === "failed"
                        ? "bg-accent/10 text-accent group-hover:bg-accent/20"
                        : latest.status === "requires_action"
                        ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20 group-hover:bg-amber-500/20"
                        : "bg-muted text-muted-foreground group-hover:bg-muted/80"
                    }`}
                  >
                    {latest.status}
                  </span>
                  <span className="text-[10px] text-muted-foreground/70 font-mono hidden group-hover:inline transition-opacity">
                    (details)
                  </span>
                </div>
                {latest.provider_payment_id ? (
                  <div className="text-[10px] text-muted-foreground font-mono group-hover:text-foreground transition-colors">
                    {latest.provider_payment_id.slice(0, 12)}...
                  </div>
                ) : (
                  <div className="text-[10px] text-muted-foreground font-mono group-hover:text-foreground transition-colors">
                    {latest.id.slice(0, 12)}...
                  </div>
                )}
              </button>
            </HoverCardTrigger>
            <HoverCardContent
              className="w-80 p-4 space-y-3 bg-popover text-popover-foreground shadow-xl border border-border rounded-lg"
              align="start"
            >
              <div className="flex items-center justify-between border-b border-border/60 pb-2">
                <div className="flex items-center gap-1.5 font-semibold text-xs text-foreground">
                  <Activity className="w-3.5 h-3.5 text-accent" />
                  <span>Latest Payment Attempt</span>
                </div>
                <span
                  className={`text-[10px] font-mono px-2 py-0.5 rounded font-semibold uppercase ${
                    latest.status === "succeeded"
                      ? "bg-primary/10 text-foreground border border-primary/20"
                      : latest.status === "failed"
                      ? "bg-accent/10 text-accent border border-accent/20"
                      : latest.status === "requires_action"
                      ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20"
                      : latest.status === "expired" || latest.status === "cancelled"
                      ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20"
                      : "bg-muted text-muted-foreground border border-border"
                  }`}
                >
                  {latest.status}
                </span>
              </div>

              <div className="space-y-2 text-xs">
                <div>
                  <div className="text-[10px] font-medium text-muted-foreground uppercase font-mono tracking-wider">Attempt ID</div>
                  <div className="flex items-center justify-between gap-2 mt-0.5 bg-muted/50 px-2 py-1 rounded border border-border/60 font-mono text-[11px] text-foreground">
                    <span className="truncate select-all" title={latest.id}>{latest.id}</span>
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation()
                        onCopy(latest.id, `desk-att-${latest.id}`)
                      }}
                      className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
                      title="Copy Attempt ID"
                      aria-label="Copy Attempt ID"
                    >
                      {copiedKey === `desk-att-${latest.id}` ? (
                        <Check className="w-3.5 h-3.5 text-primary" />
                      ) : (
                        <Copy className="w-3.5 h-3.5" />
                      )}
                    </button>
                  </div>
                </div>

                {latest.provider_payment_id && (
                  <div>
                    <div className="text-[10px] font-medium text-muted-foreground uppercase font-mono tracking-wider">Provider Session / Payment ID</div>
                    <div className="flex items-center justify-between gap-2 mt-0.5 bg-muted/50 px-2 py-1 rounded border border-border/60 font-mono text-[11px] text-foreground">
                      <span className="truncate select-all" title={latest.provider_payment_id}>{latest.provider_payment_id}</span>
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation()
                          onCopy(latest.provider_payment_id || "", `desk-prov-${latest.provider_payment_id}`)
                        }}
                        className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
                        title="Copy Provider Payment ID"
                        aria-label="Copy Provider Payment ID"
                      >
                        {copiedKey === `desk-prov-${latest.provider_payment_id}` ? (
                          <Check className="w-3.5 h-3.5 text-primary" />
                        ) : (
                          <Copy className="w-3.5 h-3.5" />
                        )}
                      </button>
                    </div>
                  </div>
                )}

                <div className="grid grid-cols-2 gap-2 pt-1 border-t border-border/40">
                  <div>
                    <div className="text-[10px] text-muted-foreground font-mono">Method</div>
                    <div className="font-mono font-medium text-foreground text-[11px] uppercase truncate">
                      {latest.payment_method || "PayMongo Checkout"}
                    </div>
                  </div>
                  <div>
                    <div className="text-[10px] text-muted-foreground font-mono">Amount</div>
                    <div className="font-mono font-bold text-foreground text-[11px]">
                      {formatPhpFromMinor(latest.amount_minor)}
                    </div>
                  </div>
                </div>

                <div className="pt-1 border-t border-border/40">
                  <div className="text-[10px] text-muted-foreground font-mono">Created Timestamp</div>
                  <div className="text-[11px] font-mono text-muted-foreground mt-0.5 flex items-center gap-1">
                    <Clock className="w-3 h-3 text-muted-foreground shrink-0" />
                    <span>{new Date(latest.created_at).toLocaleString()}</span>
                  </div>
                </div>
              </div>

              <Button
                size="sm"
                variant="secondary"
                className="w-full text-xs font-semibold h-8 mt-2 flex items-center justify-center gap-1.5"
                onClick={() => onView(latest.id)}
              >
                <ExternalLink className="w-3.5 h-3.5" />
                <span>Inspect Full Attempt Details</span>
              </Button>
            </HoverCardContent>
          </HoverCard>
        ) : (
          <span className="text-xs text-muted-foreground font-mono">—</span>
        )}
      </td>
      <td className="px-6 py-4 text-right whitespace-nowrap">
        <div className="inline-flex items-center justify-end gap-1.5 flex-nowrap">
          {order.status === "pending" && (
            <Button
              size="sm"
              className="bg-accent text-accent-foreground hover:bg-accent/90 text-xs font-semibold h-8 shrink-0 whitespace-nowrap"
              onClick={() => onPay(order.id)}
            >
              <CreditCard className="w-3.5 h-3.5 mr-1 shrink-0" />
              {latest && (latest.status === "failed" || latest.status === "expired" || latest.status === "cancelled")
                ? "Retry Payment"
                : "Pay Now"}
            </Button>
          )}
          {latest && latest.status !== "succeeded" && (
            <Button
              variant="outline"
              size="sm"
              className="text-xs h-8 shrink-0 whitespace-nowrap"
              onClick={() => onReconcile(latest.id)}
            >
              Reconcile
            </Button>
          )}
          {latest && (latest.status === "pending" || latest.status === "requires_action") && onExpire && (
            <Button
              variant="outline"
              size="sm"
              className="text-xs h-8 shrink-0 whitespace-nowrap text-amber-600 dark:text-amber-400 border-amber-500/30 hover:bg-amber-500/10 gap-1"
              title="Expire and invalidate this test checkout session"
              onClick={() => onExpire(latest.id)}
            >
              <Clock className="w-3.5 h-3.5" />
              <span>Expire</span>
            </Button>
          )}
          {(order.status === "paid" || latest?.status === "succeeded") && (
            <Button
              variant="outline"
              size="sm"
              className="text-xs h-8 shrink-0 whitespace-nowrap text-emerald-600 border-emerald-600/30 hover:bg-emerald-500/10 gap-1"
              onClick={() => onView(latest?.id || order.id)}
            >
              <Receipt className="w-3.5 h-3.5" />
              <span>Receipt</span>
            </Button>
          )}
          {latest && (
            <Button
              variant="ghost"
              size="sm"
              className="text-xs text-muted-foreground hover:text-foreground h-8 shrink-0 whitespace-nowrap"
              onClick={() => onView(latest.id)}
            >
              View
            </Button>
          )}
        </div>
      </td>
    </tr>
  )
}

interface OutboxItemProps {
  item: FulfillmentOutboxRecord
  copiedKey: string | null
  onCopy: (text: string, key: string) => void
  onViewAttempt: (attemptId: string) => void
}

function OutboxMobileCard({ item, copiedKey, onCopy, onViewAttempt }: OutboxItemProps) {
  return (
    <div className="p-4 space-y-3 bg-card hover:bg-muted/10 transition-colors">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <div className="flex items-center gap-2">
          <span className="font-mono text-xs font-semibold text-foreground truncate max-w-[130px]" title={item.id}>
            {item.id.slice(0, 8)}...
          </span>
          <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-muted text-foreground font-medium whitespace-nowrap inline-flex items-center shrink-0">
            {item.event_type || "order.paid"}
          </span>
        </div>
        <span
          className={`text-[11px] px-2.5 py-0.5 rounded font-mono font-medium uppercase whitespace-nowrap inline-flex items-center shrink-0 ${
            item.status === "completed"
              ? "bg-primary text-primary-foreground"
              : item.status === "pending"
              ? "bg-amber-500/15 text-amber-700 dark:text-amber-300 border border-amber-500/30"
              : "bg-destructive text-destructive-foreground"
          }`}
        >
          {item.status}
        </span>
      </div>

      <div className="space-y-1.5 text-xs font-mono">
        <div>
          <div className="text-[10px] text-muted-foreground uppercase tracking-wider">Outbox Task ID</div>
          <div className="flex items-center justify-between gap-1.5 mt-0.5 bg-background px-2 py-1 rounded border border-border/60 text-[11px]">
            <span className="truncate select-all">{item.id}</span>
            <button
              type="button"
              onClick={() => onCopy(item.id, `mob-out-${item.id}`)}
              className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
              title="Copy Outbox ID"
              aria-label="Copy Outbox ID"
            >
              {copiedKey === `mob-out-${item.id}` ? (
                <Check className="w-3.5 h-3.5 text-primary" />
              ) : (
                <Copy className="w-3.5 h-3.5" />
              )}
            </button>
          </div>
        </div>

        <div>
          <div className="text-[10px] text-muted-foreground uppercase tracking-wider">Order ID</div>
          <div className="flex items-center justify-between gap-1.5 mt-0.5 bg-background px-2 py-1 rounded border border-border/60 text-[11px]">
            <span className="truncate select-all">{item.order_id}</span>
            <button
              type="button"
              onClick={() => onCopy(item.order_id, `mob-ord-${item.order_id}`)}
              className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
              title="Copy Order ID"
              aria-label="Copy Order ID"
            >
              {copiedKey === `mob-ord-${item.order_id}` ? (
                <Check className="w-3.5 h-3.5 text-primary" />
              ) : (
                <Copy className="w-3.5 h-3.5" />
              )}
            </button>
          </div>
        </div>

        <div>
          <div className="text-[10px] text-muted-foreground uppercase tracking-wider">Payment Attempt ID</div>
          <div className="flex items-center justify-between gap-1.5 mt-0.5 bg-background px-2 py-1 rounded border border-border/60 text-[11px]">
            <span className="truncate select-all">{item.payment_attempt_id}</span>
            <div className="inline-flex items-center gap-1 shrink-0">
              <button
                type="button"
                onClick={() => onCopy(item.payment_attempt_id, `mob-att-${item.payment_attempt_id}`)}
                className="text-muted-foreground hover:text-foreground p-0.5 rounded transition-colors"
                title="Copy Attempt ID"
                aria-label="Copy Attempt ID"
              >
                {copiedKey === `mob-att-${item.payment_attempt_id}` ? (
                  <Check className="w-3.5 h-3.5 text-primary" />
                ) : (
                  <Copy className="w-3.5 h-3.5" />
                )}
              </button>
              <button
                type="button"
                onClick={() => onViewAttempt(item.payment_attempt_id)}
                className="text-muted-foreground hover:text-foreground p-0.5 rounded transition-colors"
                title="Inspect Attempt Details"
                aria-label="Inspect Attempt Details"
              >
                <ExternalLink className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
        </div>
      </div>

      <div className="flex items-center justify-between text-xs text-muted-foreground font-mono pt-1 border-t border-border/60">
        <span className="text-[10px]">Processed Time:</span>
        <span className="text-[11px]">
          {item.processed_at ? new Date(item.processed_at).toLocaleString() : "Pending"}
        </span>
      </div>

      <div className="flex items-center gap-2 pt-2 border-t border-border/40">
        <Button
          variant="outline"
          size="sm"
          className="text-xs h-8 flex-1 text-emerald-600 border-emerald-600/30 hover:bg-emerald-500/10 gap-1.5"
          onClick={() => onViewAttempt(item.payment_attempt_id)}
        >
          <Receipt className="w-3.5 h-3.5" />
          <span>View Receipt</span>
        </Button>
        <a
          href={getReceiptPdfUrl(item.payment_attempt_id, true)}
          download
          className="inline-flex"
          title="Download PDF"
        >
          <Button variant="ghost" size="sm" className="text-xs h-8 px-2.5 text-muted-foreground hover:text-foreground">
            <Download className="w-4 h-4" />
          </Button>
        </a>
      </div>
    </div>
  )
}

function OutboxDesktopRow({ item, copiedKey, onCopy, onViewAttempt }: OutboxItemProps) {
  return (
    <tr className="hover:bg-muted/30 transition-colors">
      <td className="px-6 py-3.5 font-mono text-xs whitespace-nowrap">
        <div className="flex items-center gap-1.5">
          <span className="text-foreground font-medium">{item.id.slice(0, 8)}...</span>
          <button
            type="button"
            onClick={() => onCopy(item.id, `desk-out-${item.id}`)}
            className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
            title="Copy Outbox ID"
            aria-label="Copy Outbox ID"
          >
            {copiedKey === `desk-out-${item.id}` ? (
              <Check className="w-3.5 h-3.5 text-primary" />
            ) : (
              <Copy className="w-3.5 h-3.5" />
            )}
          </button>
        </div>
      </td>
      <td className="px-6 py-3.5 font-mono text-xs whitespace-nowrap">
        <div className="flex items-center gap-1.5">
          <span className="text-muted-foreground">{item.order_id.slice(0, 8)}...</span>
          <button
            type="button"
            onClick={() => onCopy(item.order_id, `desk-ord-${item.order_id}`)}
            className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
            title="Copy Order ID"
            aria-label="Copy Order ID"
          >
            {copiedKey === `desk-ord-${item.order_id}` ? (
              <Check className="w-3.5 h-3.5 text-primary" />
            ) : (
              <Copy className="w-3.5 h-3.5" />
            )}
          </button>
        </div>
      </td>
      <td className="px-6 py-3.5 font-mono text-xs whitespace-nowrap">
        <div className="flex items-center gap-1.5">
          <span className="text-muted-foreground">{item.payment_attempt_id.slice(0, 8)}...</span>
          <button
            type="button"
            onClick={() => onCopy(item.payment_attempt_id, `desk-att-${item.payment_attempt_id}`)}
            className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
            title="Copy Attempt ID"
            aria-label="Copy Attempt ID"
          >
            {copiedKey === `desk-att-${item.payment_attempt_id}` ? (
              <Check className="w-3.5 h-3.5 text-primary" />
            ) : (
              <Copy className="w-3.5 h-3.5" />
            )}
          </button>
          <button
            type="button"
            onClick={() => onViewAttempt(item.payment_attempt_id)}
            className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
            title="Inspect Attempt Details"
            aria-label="Inspect Attempt Details"
          >
            <ExternalLink className="w-3.5 h-3.5" />
          </button>
        </div>
      </td>
      <td className="px-6 py-3.5 whitespace-nowrap">
        <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-muted text-foreground font-medium whitespace-nowrap inline-flex items-center shrink-0">
          {item.event_type || "order.paid"}
        </span>
      </td>
      <td className="px-6 py-3.5 whitespace-nowrap">
        <span
          className={`text-[11px] px-2 py-0.5 rounded font-mono font-medium uppercase whitespace-nowrap inline-flex items-center shrink-0 ${
            item.status === "completed"
              ? "bg-primary text-primary-foreground"
              : item.status === "pending"
              ? "bg-amber-500/15 text-amber-700 dark:text-amber-300 border border-amber-500/30"
              : "bg-destructive text-destructive-foreground"
          }`}
        >
          {item.status}
        </span>
      </td>
      <td className="px-6 py-3.5 text-muted-foreground font-mono text-xs whitespace-nowrap">
        {item.processed_at ? new Date(item.processed_at).toLocaleString() : "Pending"}
      </td>
      <td className="px-6 py-3.5 text-right whitespace-nowrap">
        <div className="inline-flex items-center gap-1.5">
          <Button
            variant="outline"
            size="sm"
            className="text-xs h-7 px-2.5 text-emerald-600 border-emerald-600/30 hover:bg-emerald-500/10 gap-1"
            onClick={() => onViewAttempt(item.payment_attempt_id)}
          >
            <Receipt className="w-3 h-3" />
            <span>Receipt</span>
          </Button>
          <a
            href={getReceiptPdfUrl(item.payment_attempt_id, true)}
            download
            className="inline-flex"
            title="Download PDF Receipt"
          >
            <Button variant="ghost" size="sm" className="text-xs h-7 px-2 text-muted-foreground hover:text-foreground">
              <Download className="w-3.5 h-3.5" />
            </Button>
          </a>
        </div>
      </td>
    </tr>
  )
}

interface WebhookItemProps {
  webhook: WebhookEventRecord
  copiedKey: string | null
  onCopy: (text: string, key: string) => void
}

function WebhookMobileCard({ webhook, copiedKey, onCopy }: WebhookItemProps) {
  return (
    <div className="p-4 space-y-2.5 bg-card hover:bg-muted/10 transition-colors">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <span className="font-mono text-xs font-semibold text-foreground truncate max-w-[150px]">
          {webhook.provider_event_id}
        </span>
        <span
          className={`text-[11px] px-2 py-0.5 rounded font-mono font-medium uppercase whitespace-nowrap inline-flex items-center shrink-0 ${
            webhook.processing_status === "processed"
              ? "bg-primary text-primary-foreground"
              : "bg-muted text-muted-foreground"
          }`}
        >
          {webhook.processing_status}
        </span>
      </div>

      <div className="flex items-center justify-between gap-2 text-xs">
        <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-muted text-foreground whitespace-nowrap inline-flex items-center shrink-0">
          {webhook.event_type}
        </span>
        <button
          type="button"
          onClick={() => onCopy(webhook.provider_event_id, `mob-wh-${webhook.id}`)}
          className="text-xs text-muted-foreground hover:text-foreground inline-flex items-center gap-1 font-mono"
        >
          {copiedKey === `mob-wh-${webhook.id}` ? (
            <>
              <Check className="w-3.5 h-3.5 text-primary" />
              <span className="text-primary text-[10px]">Copied</span>
            </>
          ) : (
            <>
              <Copy className="w-3.5 h-3.5" />
              <span className="text-[10px]">Copy ID</span>
            </>
          )}
        </button>
      </div>

      <div className="flex items-center justify-between text-[11px] text-muted-foreground font-mono pt-1 border-t border-border/60">
        <span>Received: {new Date(webhook.received_at).toLocaleTimeString()}</span>
        <span>Processed: {webhook.processed_at ? new Date(webhook.processed_at).toLocaleTimeString() : "—"}</span>
      </div>
    </div>
  )
}

// ============================================================================
// Main PaymentsHub Component
// ============================================================================

export function PaymentsHub() {
  const router = useRouter()
  const [activeTab, setActiveTab] = useState<"orders" | "webhooks" | "checklist">("orders")
  const [orders, setOrders] = useState<Order[]>([])
  const [webhooks, setWebhooks] = useState<WebhookEventRecord[]>([])
  const [outbox, setOutbox] = useState<FulfillmentOutboxRecord[]>([])
  const [simulatedChannel, setSimulatedChannel] = useState<"qrph" | "card_visa" | "gcash" | "maya" | "dob_bpi">("qrph")
  const [selectedTargetAttemptId, setSelectedTargetAttemptId] = useState<string>("")
  const [loading, setLoading] = useState(true)
  const [actionLoading, setActionLoading] = useState(false)
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null)
  const [copiedKey, setCopiedKey] = useState<string | null>(null)
  const [mounted, setMounted] = useState(false)
  const [gformsIngestUrl, setGformsIngestUrl] = useState<string>("/api/v1/integrations/google-forms/submit")
  const [gformsSecret, setGformsSecret] = useState<string>("")

  useEffect(() => {
    setMounted(true)
    if (typeof window !== "undefined") {
      setGformsIngestUrl(`${window.location.origin}/api/v1/integrations/google-forms/submit`)
    }
    if (process.env.NEXT_PUBLIC_GOOGLE_FORMS_SECRET) {
      setGformsSecret(process.env.NEXT_PUBLIC_GOOGLE_FORMS_SECRET)
    }
  }, [])

  const copyToClipboard = (text: string, key: string) => {
    try {
      if (typeof navigator !== "undefined" && navigator.clipboard) {
        navigator.clipboard.writeText(text).then(() => {
          setCopiedKey(key)
          setTimeout(() => setCopiedKey(null), 2000)
        }).catch(() => {
          fallbackCopy(text, key)
        })
      } else {
        fallbackCopy(text, key)
      }
    } catch {
      // ignore clipboard failures
    }
  }

  const fallbackCopy = (text: string, key: string) => {
    try {
      const el = document.createElement("textarea")
      el.value = text
      el.setAttribute("readonly", "")
      el.style.position = "absolute"
      el.style.left = "-9999px"
      document.body.appendChild(el)
      el.select()
      document.execCommand("copy")
      document.body.removeChild(el)
      setCopiedKey(key)
      setTimeout(() => setCopiedKey(null), 2000)
    } catch {
      // ignore
    }
  }

  // Search & Status Filter State for Orders Table
  const [searchQuery, setSearchQuery] = useState("")
  const [statusFilter, setStatusFilter] = useState<"all" | "pending" | "paid" | "cancelled">("all")

  // New Order Modal State
  const [showOrderModal, setShowOrderModal] = useState(false)
  const [showGFormsModal, setShowGFormsModal] = useState(false)
  const [orderCategory, setOrderCategory] = useState<"product_order" | "department_invoice">("product_order")
  const [referenceCode, setReferenceCode] = useState("Product A")
  const [orderTitle, setOrderTitle] = useState("")
  const [orderQuantity, setOrderQuantity] = useState(1)

  const productOptions = [
    { name: "Product A", unitPrice: 1 },
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
    // Live background polling loop every 4 seconds to sync Google Form & PayMongo updates
    const interval = setInterval(() => {
      fetchOrders().then((data) => {
        if (Array.isArray(data)) setOrders(data)
      }).catch(() => {})
      fetchWebhookEvents().then((data) => {
        if (Array.isArray(data)) setWebhooks(data)
      }).catch(() => {})
      fetchFulfillmentOutbox().then((data) => {
        if (Array.isArray(data)) setOutbox(data)
      }).catch(() => {})
    }, 4000)
    return () => clearInterval(interval)
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

  // Flat list of all attempts from orders
  const allAttempts = useMemo(() => {
    const list: { order: Order; attempt: NonNullable<Order["latest_attempt"]> }[] = []
    orders.forEach((o) => {
      if (o.latest_attempt) {
        list.push({ order: o, attempt: o.latest_attempt })
      }
    })
    return list
  }, [orders])

  const resolvedTarget = useMemo(() => {
    if (selectedTargetAttemptId) {
      const match = allAttempts.find((item) => item.attempt.id === selectedTargetAttemptId)
      if (match) return match
    }
    // Default to first pending / requires_action attempt if available, else first attempt
    const pendingMatch = allAttempts.find(
      (item) => item.attempt.status === "pending" || item.attempt.status === "requires_action"
    )
    return pendingMatch || allAttempts[0] || null
  }, [selectedTargetAttemptId, allAttempts])

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

  const handleProcessOutbox = async (retryFailed = false) => {
    setActionLoading(true)
    try {
      const res = await processFulfillmentOutbox(retryFailed)
      setFeedbackMessage(
        retryFailed
          ? `Outbox retry complete: Reprocessed ${res.processed_count} events.`
          : `Outbox processing complete: Processed ${res.processed_count} queued events.`
      )
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Outbox error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const handleExpireAttempt = async (attemptId: string, reason?: string) => {
    setActionLoading(true)
    try {
      const res = await expirePayment(attemptId, reason || "Manually expired via test orchestration console")
      setFeedbackMessage(
        `Checkout session expired & invalidated: ${res.attempt?.provider_payment_id || attemptId}. Gateway URL revoked.`
      )
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Expiration error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const handleFailAttempt = async (attemptId: string, code?: string, message?: string) => {
    setActionLoading(true)
    try {
      const res = await failPayment(attemptId, code || "PAYMENT_FAILED_TEST", message || "Test simulated payment failure")
      setFeedbackMessage(`Payment marked failed: ${attemptId}. Reason: ${res.attempt?.failure_message || "Failed"}`)
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Failure error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const handleCreateAndExpireTestOrder = async () => {
    setActionLoading(true)
    try {
      const res = await createAndExpireTestPayment(simulatedChannel)
      setFeedbackMessage(
        `Quick Test Success: Created fresh order ${res.order?.order_number} and immediately expired attempt ${res.attempt?.id} (PayMongo session ${res.attempt?.provider_payment_id || "simulated"}). Gateway link blocked!`
      )
      await loadAllData()
    } catch (err: any) {
      setFeedbackMessage(`Quick test error: ${err.message}`)
    } finally {
      setActionLoading(false)
    }
  }

  const handleSimulateWebhookEvent = async (type: "paid" | "failed" | "expired" | "duplicate") => {
    if (!resolvedTarget) {
      setFeedbackMessage("No orders with payment attempts found to simulate a webhook on.")
      return
    }
    const targetAttempt = resolvedTarget.attempt

    setActionLoading(true)
    try {
      if (type === "duplicate") {
        const res = await simulateWebhook(targetAttempt.id, "checkout_session.payment.paid", true)
        setFeedbackMessage(
          `Duplicate webhook test: ${
            res.result?.duplicate ? "Deduplicated cleanly (200 OK without double fulfillment)" : "Received"
          }`
        )
      } else {
        await simulatePaymentAttemptWebhook(targetAttempt.id, type, { forceOverride: true })
        if (type === "paid") {
          setFeedbackMessage(
            `Simulated signed 'checkout_session.payment.paid' webhook processed via ${simulatedChannel.toUpperCase()}. Official Receipt & Acknowledgement PDF generated & dispatched.`
          )
        } else if (type === "failed") {
          setFeedbackMessage(
            `Simulated signed 'payment.failed' webhook processed. Attempt marked failed.`
          )
        } else if (type === "expired") {
          setFeedbackMessage(
            `Simulated signed 'checkout_session.expired' webhook processed. Attempt marked expired.`
          )
        }
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
  const pendingOutboxCount = outbox.filter((item) => item.status === "pending").length
  const failedOutboxCount = outbox.filter((item) => item.status === "failed").length

  return (
    <div className="p-4 sm:p-6 lg:p-8 space-y-6 sm:space-y-8 max-w-7xl mx-auto">
      {/* Minimalist Header */}
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-4 border-b border-border pb-6">
        <div>
          <div className="flex items-center gap-2.5 flex-wrap">
            <p className="text-xs font-mono uppercase tracking-widest text-muted-foreground">
              Payment Gateway Engine
            </p>
            <span className="inline-flex items-center gap-1.5 text-[10px] font-mono uppercase px-2 py-0.5 rounded-full bg-accent/10 text-accent border border-accent/20 font-semibold">
              <span className="w-1.5 h-1.5 rounded-full bg-accent animate-pulse" />
              PayMongo Ready
            </span>
            <span className="inline-flex items-center gap-1.5 text-[10px] font-mono uppercase px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20 font-semibold">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
              Live Sync Active
            </span>
          </div>
          <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-foreground mt-1">
            Payments & Live Orchestration
          </h1>
        </div>
        <div className="flex items-center gap-2.5 flex-wrap">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setShowGFormsModal(true)}
            className="gap-2 border-border hover:bg-muted text-foreground"
          >
            <FileText className="w-3.5 h-3.5 text-blue-500" />
            <span>Google Forms Middleware</span>
          </Button>
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
              {outbox.length} Outbox records ({pendingOutboxCount} pending)
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Minimalist Tab Navigation */}
      <div role="tablist" aria-label="Payment Hub Sections" className="flex gap-4 sm:gap-6 border-b border-border overflow-x-auto whitespace-nowrap scrollbar-none pb-0.5">
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
                className="w-full pl-9 pr-8 py-1.5 text-xs bg-muted/30 border border-border rounded-md text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-ring font-mono"
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

            <div className="flex items-center gap-1.5 flex-wrap">
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
            <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border/60 pb-4">
              <div>
                <CardTitle className="text-base font-semibold">Order Ledger & Payment Operations</CardTitle>
                <CardDescription className="text-xs">
                  Showing {filteredOrders.length} of {orders.length} authoritative ledger orders
                </CardDescription>
              </div>
              <span className="text-[11px] font-mono text-muted-foreground sm:text-right">
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
                <>
                  {/* Mobile Stacked Card View (block md:hidden) */}
                  <div className="block md:hidden divide-y divide-border/70">
                    {filteredOrders.map((o) => (
                      <OrderMobileCard
                        key={o.id}
                        order={o}
                        copiedKey={copiedKey}
                        onCopy={copyToClipboard}
                        onReconcile={handleReconcileSingle}
                        onView={(attemptId) => router.push(`/payment/result/${attemptId}`)}
                        onPay={(orderId) => router.push(`/checkout?order_id=${orderId}`)}
                        onExpire={handleExpireAttempt}
                      />
                    ))}
                  </div>

                  {/* Desktop Table View (hidden md:block overflow-x-auto) */}
                  <div className="hidden md:block overflow-x-auto">
                    <table className="w-full min-w-[850px] text-left text-sm">
                      <thead className="bg-muted/40 border-b border-border text-[11px] text-muted-foreground uppercase font-mono">
                        <tr>
                          <th className="px-6 py-3 whitespace-nowrap">Order No.</th>
                          <th className="px-6 py-3 whitespace-nowrap">Title & Reference</th>
                          <th className="px-6 py-3 whitespace-nowrap">Category</th>
                          <th className="px-6 py-3 whitespace-nowrap">Amount</th>
                          <th className="px-6 py-3 whitespace-nowrap">Order Status</th>
                          <th className="px-6 py-3 whitespace-nowrap">Latest Attempt</th>
                          <th className="px-6 py-3 text-right whitespace-nowrap">Actions</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border/70">
                        {filteredOrders.map((o) => (
                          <OrderDesktopRow
                            key={o.id}
                            order={o}
                            copiedKey={copiedKey}
                            onCopy={copyToClipboard}
                            onReconcile={handleReconcileSingle}
                            onView={(attemptId) => router.push(`/payment/result/${attemptId}`)}
                            onPay={(orderId) => router.push(`/checkout?order_id=${orderId}`)}
                            onExpire={handleExpireAttempt}
                          />
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
            </CardContent>
          </Card>
        </div>
      )}

      {/* Tab 2: Live Orchestrator & Webhooks */}
      {activeTab === "webhooks" && (
        <div id="tabpanel-webhooks" role="tabpanel" aria-labelledby="tab-webhooks" className="space-y-6">
          <Card className="shadow-none border-border/90 overflow-hidden">
            <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border/60 pb-4">
              <div>
                <CardTitle className="text-base font-semibold">Incoming PayMongo Webhook Events</CardTitle>
                <CardDescription className="text-xs">
                  Signed payloads verified with HMAC-SHA256 and deduplicated by provider event ID
                </CardDescription>
              </div>
              <span className="text-[11px] font-mono px-2.5 py-1 rounded-full bg-primary text-primary-foreground shrink-0 w-fit">
                HMAC-SHA256 Active
              </span>
            </CardHeader>
            <CardContent className="p-0">
              {webhooks.length === 0 ? (
                <div className="p-8 text-center text-sm text-muted-foreground">
                  No webhook events received yet. Simulate one in the Checklist tab.
                </div>
              ) : (
                <>
                  {/* Mobile Stacked Card View for Webhooks */}
                  <div className="block md:hidden divide-y divide-border/70">
                    {webhooks.map((w) => (
                      <WebhookMobileCard
                        key={w.id}
                        webhook={w}
                        copiedKey={copiedKey}
                        onCopy={copyToClipboard}
                      />
                    ))}
                  </div>

                  {/* Desktop Table View for Webhooks */}
                  <div className="hidden md:block overflow-x-auto">
                    <table className="w-full min-w-[700px] text-left text-xs">
                      <thead className="bg-muted/40 border-b border-border text-muted-foreground uppercase font-mono">
                        <tr>
                          <th className="px-6 py-3 whitespace-nowrap">Event ID</th>
                          <th className="px-6 py-3 whitespace-nowrap">Event Type</th>
                          <th className="px-6 py-3 whitespace-nowrap">Status</th>
                          <th className="px-6 py-3 whitespace-nowrap">Received At</th>
                          <th className="px-6 py-3 whitespace-nowrap">Processed At</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border/70">
                        {webhooks.map((w) => (
                          <tr key={w.id} className="hover:bg-muted/30">
                            <td className="px-6 py-3.5 font-mono text-foreground font-medium whitespace-nowrap">
                              <div className="flex items-center gap-1.5">
                                <span className="truncate max-w-[200px]" title={w.provider_event_id}>
                                  {w.provider_event_id}
                                </span>
                                <button
                                  type="button"
                                  onClick={() => copyToClipboard(w.provider_event_id, `desk-wh-${w.id}`)}
                                  className="text-muted-foreground hover:text-foreground p-0.5 rounded shrink-0 transition-colors"
                                  title="Copy Event ID"
                                  aria-label="Copy Event ID"
                                >
                                  {copiedKey === `desk-wh-${w.id}` ? (
                                    <Check className="w-3.5 h-3.5 text-primary" />
                                  ) : (
                                    <Copy className="w-3.5 h-3.5" />
                                  )}
                                </button>
                              </div>
                            </td>
                            <td className="px-6 py-3.5 whitespace-nowrap">
                              <span className="bg-muted text-foreground font-mono px-2 py-0.5 rounded whitespace-nowrap inline-flex items-center shrink-0">
                                {w.event_type}
                              </span>
                            </td>
                            <td className="px-6 py-3.5 whitespace-nowrap">
                              <span
                                className={`px-2 py-0.5 rounded font-mono font-medium whitespace-nowrap inline-flex items-center shrink-0 ${
                                  w.processing_status === "processed"
                                    ? "bg-primary text-primary-foreground"
                                    : "bg-muted text-muted-foreground"
                                }`}
                              >
                                {w.processing_status}
                              </span>
                            </td>
                            <td className="px-6 py-3.5 text-muted-foreground font-mono whitespace-nowrap">
                              {new Date(w.received_at).toLocaleString()}
                            </td>
                            <td className="px-6 py-3.5 text-muted-foreground font-mono whitespace-nowrap">
                              {w.processed_at ? new Date(w.processed_at).toLocaleString() : "—"}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
            </CardContent>
          </Card>

          <Card className="shadow-none border-border/90 overflow-hidden">
            <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border/60 pb-4">
              <div>
                <CardTitle className="text-base font-semibold">Fulfillment Outbox Monitor</CardTitle>
                <CardDescription className="text-xs">
                  Reliable background queue for downstream ledger updates, receipts, and audit logging
                </CardDescription>
              </div>
              <div className="flex items-center gap-2 flex-wrap">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => handleProcessOutbox(false)}
                  disabled={actionLoading || pendingOutboxCount === 0}
                  className="gap-1.5 text-xs h-7 border-border hover:bg-muted text-foreground"
                >
                  <Play className={`w-3 h-3 ${actionLoading ? "animate-spin" : ""}`} />
                  <span>Drain Outbox Queue ({pendingOutboxCount} pending)</span>
                </Button>
                {failedOutboxCount > 0 && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleProcessOutbox(true)}
                    disabled={actionLoading}
                    className="gap-1.5 text-xs h-7 border-accent/40 text-accent hover:bg-accent/10"
                  >
                    <RotateCcw className={`w-3 h-3 ${actionLoading ? "animate-spin" : ""}`} />
                    <span>Retry Failed ({failedOutboxCount})</span>
                  </Button>
                )}
                <span className="text-[11px] font-mono px-2.5 py-1 rounded-full bg-accent text-accent-foreground font-semibold shrink-0 w-fit">
                  Transactional Outbox
                </span>
              </div>
            </CardHeader>
            <CardContent className="p-0">
              {outbox.length === 0 ? (
                <div className="p-8 text-center text-sm text-muted-foreground">
                  No outbox queue records found.
                </div>
              ) : (
                <>
                  {/* Mobile Stacked Card View for Outbox */}
                  <div className="block md:hidden divide-y divide-border/70">
                    {outbox.map((item) => (
                      <OutboxMobileCard
                        key={item.id}
                        item={item}
                        copiedKey={copiedKey}
                        onCopy={copyToClipboard}
                        onViewAttempt={(attemptId) => router.push(`/payment/result/${attemptId}`)}
                      />
                    ))}
                  </div>

                  {/* Desktop Table View for Outbox */}
                  <div className="hidden md:block overflow-x-auto">
                    <table className="w-full min-w-[750px] text-left text-xs">
                      <thead className="bg-muted/40 border-b border-border text-muted-foreground uppercase font-mono">
                        <tr>
                          <th className="px-6 py-3 whitespace-nowrap">Outbox ID</th>
                          <th className="px-6 py-3 whitespace-nowrap">Order ID</th>
                          <th className="px-6 py-3 whitespace-nowrap">Attempt ID</th>
                          <th className="px-6 py-3 whitespace-nowrap">Event Type</th>
                          <th className="px-6 py-3 whitespace-nowrap">Status</th>
                          <th className="px-6 py-3 whitespace-nowrap">Processed Time</th>
                          <th className="px-6 py-3 whitespace-nowrap text-right">Receipt / Actions</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border/70">
                        {outbox.map((item) => (
                          <OutboxDesktopRow
                            key={item.id}
                            item={item}
                            copiedKey={copiedKey}
                            onCopy={copyToClipboard}
                            onViewAttempt={(attemptId) => router.push(`/payment/result/${attemptId}`)}
                          />
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
            </CardContent>
          </Card>
        </div>
      )}

      {/* Tab 3: Sandbox & Go-Live Checklist */}
      {activeTab === "checklist" && (
        <div id="tabpanel-checklist" role="tabpanel" aria-labelledby="tab-checklist" className="space-y-6">
          <Card className="shadow-none border-border/90">
            <CardHeader className="flex flex-col sm:flex-row sm:items-start justify-between gap-4 border-b border-border/60 pb-4">
              <div>
                <CardTitle className="text-base font-semibold">Interactive Sandbox & Gateway Testing Suite</CardTitle>
                <CardDescription className="text-xs">
                  Test authoritative gateway session invalidation, HMAC-signed webhooks, and state machine invariants live
                </CardDescription>
              </div>
              <Button
                variant="default"
                size="sm"
                onClick={handleCreateAndExpireTestOrder}
                disabled={actionLoading}
                className="gap-1.5 text-xs font-semibold bg-accent text-accent-foreground hover:bg-accent/90 shrink-0"
              >
                <Clock className="w-3.5 h-3.5" />
                <span>Quick Test: Create & Expire Fresh Order</span>
              </Button>
            </CardHeader>
            <CardContent className="space-y-5 pt-4">
              {/* Target Attempt Selector */}
              <div className="p-3.5 rounded-lg border border-border/80 bg-muted/20 space-y-3">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                  <label htmlFor="target-attempt-select" className="text-xs font-semibold text-foreground uppercase tracking-wide">
                    Target Payment Attempt for Testing:
                  </label>
                  {resolvedTarget && (
                    <div className="flex items-center gap-2 text-xs">
                      <span className="text-muted-foreground font-mono">Status:</span>
                      <span
                        className={`px-2 py-0.5 rounded text-[10px] font-mono font-bold uppercase ${
                          resolvedTarget.attempt.status === "succeeded"
                            ? "bg-primary/10 text-foreground border border-primary/20"
                            : resolvedTarget.attempt.status === "failed"
                            ? "bg-accent/10 text-accent border border-accent/20"
                            : resolvedTarget.attempt.status === "requires_action" || resolvedTarget.attempt.status === "pending"
                            ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border border-amber-500/20"
                            : "bg-muted text-muted-foreground border border-border"
                        }`}
                      >
                        {resolvedTarget.attempt.status}
                      </span>
                    </div>
                  )}
                </div>

                <select
                  id="target-attempt-select"
                  value={resolvedTarget ? resolvedTarget.attempt.id : ""}
                  onChange={(e) => setSelectedTargetAttemptId(e.target.value)}
                  className="w-full text-xs font-mono bg-card border border-border rounded-md p-2 text-foreground focus:outline-none focus:ring-1 focus:ring-ring"
                >
                  {allAttempts.map(({ order, attempt }) => (
                    <option key={attempt.id} value={attempt.id}>
                      [{attempt.status.toUpperCase()}] {order.order_number} · {order.title} ({formatPhpFromMinor(attempt.amount_minor)}) — Attempt {attempt.id.slice(0, 8)}... {attempt.provider_payment_id ? `(${attempt.provider_payment_id})` : ""}
                    </option>
                  ))}
                </select>

                {resolvedTarget && (
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pt-2 border-t border-border/60 text-xs font-mono">
                    <div className="space-y-0.5">
                      <div className="text-[11px] text-foreground font-semibold">
                        Order: {resolvedTarget.order.order_number} — {resolvedTarget.order.title}
                      </div>
                      <div className="text-[10px] text-muted-foreground">
                        Session: {resolvedTarget.attempt.provider_payment_id || resolvedTarget.attempt.id} · Method: {resolvedTarget.attempt.payment_method || "paymongo"}
                      </div>
                    </div>
                    <div className="flex items-center gap-2 flex-wrap">
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        disabled={actionLoading || resolvedTarget.attempt.status === "succeeded"}
                        onClick={() => handleExpireAttempt(resolvedTarget.attempt.id)}
                        className="text-xs h-7 text-amber-600 dark:text-amber-400 border-amber-500/30 hover:bg-amber-500/10 gap-1"
                        title={resolvedTarget.attempt.status === "succeeded" ? "Cannot expire paid session" : "Revoke gateway session URL via PayMongo /expire API"}
                      >
                        <Clock className="w-3 h-3" />
                        <span>Authoritative Expire</span>
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        disabled={actionLoading || resolvedTarget.attempt.status === "succeeded"}
                        onClick={() => handleFailAttempt(resolvedTarget.attempt.id)}
                        className="text-xs h-7 text-accent border-accent/30 hover:bg-accent/10 gap-1"
                        title={resolvedTarget.attempt.status === "succeeded" ? "Cannot fail paid session" : "Mark attempt failed"}
                      >
                        <XCircle className="w-3 h-3" />
                        <span>Direct Fail</span>
                      </Button>
                    </div>
                  </div>
                )}

                {resolvedTarget && (resolvedTarget.attempt.status === "succeeded" || resolvedTarget.order.status === "paid") && (
                  <div className="flex items-start gap-2 p-2 rounded bg-amber-500/10 border border-amber-500/20 text-amber-700 dark:text-amber-300 text-xs">
                    <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
                    <div>
                      <span className="font-semibold">Terminal State Immutability Active: </span>
                      This attempt has already been paid and settled. Accounting records cannot be expired or failed after settlement. To test gateway session expiration or failure blocking, select a pending attempt or click <strong>Quick Test: Create & Expire Fresh Order</strong> above.
                    </div>
                  </div>
                )}
              </div>

              <div>
                <label className="text-xs font-semibold text-foreground uppercase tracking-wide block mb-2">
                  1. Select Payment Method Rail for Simulation:
                </label>
                <div className="grid grid-cols-2 sm:grid-cols-5 gap-2">
                  {[
                    { id: "qrph", name: "Scan GQRPh code to pay", badge: "QR Ph" },
                    { id: "card_visa", name: "Card (VISA 3D Secure)", badge: "VISA" },
                    { id: "gcash", name: "E-Wallet (GCash)", badge: "GCash" },
                    { id: "maya", name: "E-Wallet (Maya)", badge: "Maya" },
                    { id: "dob_bpi", name: "Online Banking (BPI)", badge: "BPI" },
                  ].map((ch) => (
                    <button
                      key={ch.id}
                      type="button"
                      onClick={() => setSimulatedChannel(ch.id as any)}
                      className={`p-2.5 rounded-lg border text-left text-xs transition-colors flex flex-col justify-between ${
                        simulatedChannel === ch.id
                          ? "border-emerald-500 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 font-semibold"
                          : "border-border bg-card text-muted-foreground hover:bg-muted/40"
                      }`}
                    >
                      <span className="font-bold">{ch.badge}</span>
                      <span className="text-[10px] opacity-80 truncate">{ch.name}</span>
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-foreground uppercase tracking-wide block mb-2">
                  2. Trigger Simulation on Target Attempt:
                </label>
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
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
                    onClick={() => handleSimulateWebhookEvent("expired")}
                    disabled={actionLoading}
                    className="gap-1.5 h-auto py-3.5 px-4 text-left flex-col items-start border-border hover:border-amber-500"
                  >
                    <div className="flex items-center gap-1.5 font-semibold text-amber-500 text-xs">
                      <Clock className="w-4 h-4 text-amber-500" />
                      <span>Simulate Expired Webhook</span>
                    </div>
                    <span className="text-[11px] font-mono text-muted-foreground">
                      checkout_session.expired
                    </span>
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

      {/* Google Forms Middleware Modal */}
      {showGFormsModal && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="gforms-dialog-title"
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
        >
          <Card className="max-w-xl w-full border-border shadow-2xl">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <div>
                <div className="flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
                  <CardTitle id="gforms-dialog-title" className="text-base font-semibold">
                    Google Forms Serverless Middleware
                  </CardTitle>
                </div>
                <CardDescription className="text-xs mt-1">
                  High-concurrency buffer handling up to 5,000+ simultaneous checkout requests
                </CardDescription>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                onClick={() => setShowGFormsModal(false)}
                aria-label="Close dialog"
                className="text-muted-foreground hover:text-foreground"
              >
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4 pt-4 text-xs">
              <div className="p-3 rounded-lg bg-blue-500/10 border border-blue-500/20 text-blue-700 dark:text-blue-300">
                <p className="font-semibold mb-1 flex items-center gap-1.5">
                  <FileText className="w-3.5 h-3.5" />
                  <span>How Google Forms Middleware Works</span>
                </p>
                <p className="leading-relaxed">
                  Google Forms handles initial form submission bursts with zero strain on your cloud server. Google Apps Script then paces HTTP calls into your live system, creating PayMongo payment links, emailing them to users, and streaming orders live into this dashboard.
                </p>
              </div>

              <div className="space-y-3">
                <div>
                  <label className="text-[11px] font-semibold text-muted-foreground uppercase tracking-wider block mb-1">
                    Live Webhook Ingest Endpoint
                  </label>
                  <div className="flex items-center gap-2">
                    <code className="p-2 rounded bg-muted/80 font-mono text-[11px] flex-1 border border-border select-all break-all">
                      {gformsIngestUrl}
                    </code>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        copyToClipboard(
                          gformsIngestUrl,
                          "gforms_url"
                        )
                      }
                      className="shrink-0"
                    >
                      {copiedKey === "gforms_url" ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                    </Button>
                  </div>
                </div>

                <div>
                  <label className="text-[11px] font-semibold text-muted-foreground uppercase tracking-wider block mb-1">
                    Authentication Header (X-Google-Forms-Secret)
                  </label>
                  <div className="flex items-center gap-2">
                    <code className="p-2 rounded bg-muted/80 font-mono text-[11px] flex-1 border border-border select-all">
                      {gformsSecret}
                    </code>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => copyToClipboard(gformsSecret, "gforms_secret")}
                      className="shrink-0"
                    >
                      {copiedKey === "gforms_secret" ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                    </Button>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-2 pt-1">
                  <div className="p-2.5 rounded border border-border bg-muted/30">
                    <span className="text-[11px] text-muted-foreground block">Ingested Form Orders</span>
                    <span className="text-base font-bold text-foreground font-mono">
                      {orders.filter((o) => o.metadata?.source === "google_forms").length}
                    </span>
                  </div>
                  <div className="p-2.5 rounded border border-border bg-muted/30">
                    <span className="text-[11px] text-muted-foreground block">Live Heartbeat Sync</span>
                    <span className="text-xs font-semibold text-emerald-600 dark:text-emerald-400 flex items-center gap-1.5 mt-0.5">
                      <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
                      Active (4s interval)
                    </span>
                  </div>
                </div>
              </div>
            </CardContent>
            <CardFooter className="flex justify-between items-center border-t border-border/60 pt-4 mt-2">
              <span className="text-[11px] text-muted-foreground">
                Script template: <code className="font-mono">scripts/google_apps_script/Code.gs</code>
              </span>
              <Button type="button" variant="outline" size="sm" onClick={() => setShowGFormsModal(false)}>
                Close
              </Button>
            </CardFooter>
          </Card>
        </div>
      )}
    </div>
  )
}

"use client"

import React, { useEffect, useState, use } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardDescription, CardHeader, CardTitle, CardFooter } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
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
  Download,
  ExternalLink,
  Mail,
  Send,
  Check,
  Copy,
  QrCode,
  CreditCard,
  Smartphone,
  Building2,
  FileText,
} from "lucide-react"
import {
  pollPaymentStatus,
  reconcilePayment,
  fetchPaymentReceipt,
  getReceiptPdfUrl,
  resendReceiptEmail,
  PaymentAttempt,
  ReceiptData,
  formatPhpFromMinor,
} from "@/lib/payment-api"

interface PageProps {
  params: Promise<{ paymentId: string }>
}

function getChannelIcon(code: string) {
  if (code === "qrph") return QrCode
  if (code.startsWith("card")) return CreditCard
  if (code.startsWith("dob") || code === "bpi") return Building2
  return Smartphone
}

export default function PaymentResultPage({ params }: PageProps) {
  const resolvedParams = use(params)
  const paymentId = resolvedParams.paymentId
  const router = useRouter()

  const [payment, setPayment] = useState<PaymentAttempt | null>(null)
  const [receipt, setReceipt] = useState<ReceiptData | null>(null)
  const [loading, setLoading] = useState(true)
  const [isReconciling, setIsReconciling] = useState(false)
  const [pollCount, setPollCount] = useState(0)
  const [error, setError] = useState<string | null>(null)

  // Resend email state
  const [emailInput, setEmailInput] = useState("")
  const [isSendingEmail, setIsSendingEmail] = useState(false)
  const [emailFeedback, setEmailFeedback] = useState<{ success: boolean; message: string } | null>(null)
  const [copiedKey, setCopiedKey] = useState<string | null>(null)

  const isTerminal = (status?: string) => {
    return status === "succeeded" || status === "failed" || status === "cancelled" || status === "expired"
  }

  const handleCopy = (text: string, key: string) => {
    navigator.clipboard.writeText(text)
    setCopiedKey(key)
    setTimeout(() => setCopiedKey(null), 2000)
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
        if (data.receipt) {
          setReceipt(data.receipt)
        } else if (data.status === "succeeded") {
          try {
            const rData = await fetchPaymentReceipt(paymentId)
            if (active) setReceipt(rData)
          } catch {}
        }
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
      if (updated.receipt) {
        setReceipt(updated.receipt)
      } else if (updated.status === "succeeded") {
        const rData = await fetchPaymentReceipt(paymentId)
        setReceipt(rData)
      }
    } catch (err: any) {
      setError(err.message || "Manual reconciliation error")
    } finally {
      setIsReconciling(false)
    }
  }

  const handleResendEmail = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSendingEmail(true)
    setEmailFeedback(null)
    try {
      const res = await resendReceiptEmail(paymentId, emailInput || undefined)
      setEmailFeedback({
        success: true,
        message: res.message || `Dispatched PDF receipt to ${res.recipient}`,
      })
      if (res.receipt) {
        setReceipt(res.receipt)
      }
    } catch (err: any) {
      setEmailFeedback({
        success: false,
        message: err.message || "Failed to dispatch email",
      })
    } finally {
      setIsSendingEmail(false)
    }
  }

  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <div className="flex flex-col items-center gap-3">
          <Loader2 className="w-8 h-8 animate-spin text-accent" />
          <p className="text-sm text-muted-foreground">Retrieving payment status and official receipt...</p>
        </div>
      </div>
    )
  }

  if (error || !payment) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <Card className="max-w-md w-full border-border">
          <CardHeader>
            <div className="flex items-center gap-2 text-destructive">
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
    <div className="min-h-screen bg-background py-8 px-4 md:px-8 max-w-4xl mx-auto space-y-6">
      {/* Top Breadcrumb / Return Navigation */}
      <div className="flex items-center justify-between">
        <Button
          variant="ghost"
          size="sm"
          className="text-xs text-muted-foreground hover:text-foreground gap-1.5"
          onClick={() => router.push("/")}
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Back to Dashboard</span>
        </Button>
        <div className="text-xs text-muted-foreground font-mono">
          Payment Reference: {payment.id.slice(0, 14)}...
        </div>
      </div>

      {/* Non-succeeded Payment Status Card (Failed, Expired, Cancelled, Polling) */}
      {status !== "succeeded" && (
        <Card className="border-border shadow-md">
          <CardHeader className="text-center pb-4">
            <div className="flex justify-center mb-3">
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
              {status === "failed" && "Payment Failed"}
              {status === "expired" && "Payment Session Expired"}
              {status === "cancelled" && "Payment Cancelled"}
              {(status === "pending" || status === "requires_action" || status === "processing") &&
                "Awaiting Payment Settlement..."}
              {status === "unknown" && "Settlement Status Uncertain"}
            </CardTitle>

            <CardDescription className="text-sm mt-1 max-w-lg mx-auto">
              {status === "failed" && (failure_message || "The payment transaction could not be completed.")}
              {status === "expired" && "The checkout window closed before completion. Please initiate a new session."}
              {status === "cancelled" && "The payment was cancelled by user request."}
              {(status === "pending" || status === "requires_action" || status === "processing") &&
                "We are waiting for PayMongo's authoritative signed webhook confirmation."}
              {status === "unknown" &&
                "A network timeout occurred. Please trigger reconciliation to synchronize with PayMongo."}
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 p-3.5 rounded-lg bg-muted/40 border border-border text-center">
              <div>
                <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">Amount</span>
                <span className="font-bold text-foreground text-lg">
                  {formatPhpFromMinor(amount_minor)}
                </span>
              </div>
              <div>
                <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">Order Reference</span>
                <span className="font-mono text-xs font-semibold text-foreground">
                  {order ? order.order_number : "—"}
                </span>
              </div>
              <div>
                <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">Gateway Rail</span>
                <span className="text-xs font-medium text-foreground">PayMongo Live</span>
              </div>
              <div>
                <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">Status</span>
                <span className="text-xs font-mono font-semibold uppercase text-foreground">
                  {status}
                </span>
              </div>
            </div>

            {!isTerminal(status) && (
              <div className="p-3.5 rounded-lg bg-blue-500/10 border border-blue-500/20 text-xs text-blue-700 dark:text-blue-300 space-y-2">
                <div className="flex items-center gap-2 font-medium">
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Polling PayMongo Live Webhooks (Attempt {pollCount}/30)</span>
                </div>
                <p>
                  Transactions paid via QR Ph, GCash, Maya, or Card settle within seconds. You can trigger an instant reconciliation check below.
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
          </CardFooter>
        </Card>
      )}

      {/* Succeeded Status: Loading Receipt State if generating */}
      {status === "succeeded" && !receipt && (
        <Card className="border-border shadow-md text-center p-8">
          <div className="flex flex-col items-center gap-3">
            <Loader2 className="w-8 h-8 animate-spin text-emerald-500" />
            <p className="text-sm font-semibold text-foreground">Generating Official Receipt & Treasury Acknowledgement Report...</p>
            <p className="text-xs text-muted-foreground">Synthesizing ISO-32000-1 vector PDF and recording cryptographic audit digest.</p>
          </div>
        </Card>
      )}

      {/* Official Receipt & Treasury Acknowledgement Report (Portal Copy) */}
      {status === "succeeded" && receipt && (
        <Card className="border-border shadow-lg overflow-hidden bg-card">
          {/* Header Banner */}
          <div className="bg-slate-900 text-white p-5 md:p-6 border-b border-slate-800">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
              <div>
                <span className="text-[10px] tracking-wider uppercase text-slate-400 font-mono block">
                  {receipt.issuer.company_name} &middot; {receipt.issuer.department}
                </span>
                <h2 className="text-xl font-bold text-emerald-400 mt-0.5">
                  {receipt.document_title}
                </h2>
                <p className="text-xs text-slate-300 mt-0.5">
                  TIN: {receipt.issuer.tin} &middot; Authoritative Portal Copy &middot; Verified PayMongo Rail
                </p>
              </div>
              <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 text-xs font-semibold self-start sm:self-center">
                <ShieldCheck className="w-4 h-4" />
                <span>PAID & VERIFIED</span>
              </span>
            </div>
          </div>

          <CardContent className="p-5 md:p-6 space-y-6">
            {/* Control Bar */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 p-3.5 rounded-lg bg-muted/40 border border-border">
              <div className="flex items-center justify-between sm:justify-start sm:gap-4">
                <div>
                  <span className="text-[10px] text-muted-foreground uppercase font-mono block">Official Receipt No.</span>
                  <span className="font-mono font-bold text-foreground text-sm">{receipt.official_receipt_number}</span>
                </div>
                <button
                  type="button"
                  onClick={() => handleCopy(receipt.official_receipt_number, "or_num")}
                  className="text-muted-foreground hover:text-foreground p-1 rounded transition-colors"
                  title="Copy OR Number"
                >
                  {copiedKey === "or_num" ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                </button>
              </div>

              <div className="flex items-center justify-between sm:justify-end sm:gap-4">
                <div className="sm:text-right">
                  <span className="text-[10px] text-muted-foreground uppercase font-mono block">Acknowledgement Report No.</span>
                  <span className="font-mono font-bold text-foreground text-sm">{receipt.acknowledgement_number}</span>
                </div>
                <button
                  type="button"
                  onClick={() => handleCopy(receipt.acknowledgement_number, "ack_num")}
                  className="text-muted-foreground hover:text-foreground p-1 rounded transition-colors"
                  title="Copy Acknowledgement Number"
                >
                  {copiedKey === "ack_num" ? <Check className="w-3.5 h-3.5 text-emerald-500" /> : <Copy className="w-3.5 h-3.5" />}
                </button>
              </div>
            </div>

            {/* Section I: Settlement & Order Particulars */}
            <div>
              <div className="text-xs font-semibold text-foreground uppercase tracking-wide pb-2 border-b border-border flex items-center justify-between">
                <span>Section I &middot; Settlement &amp; Order Particulars</span>
                <span className="text-[11px] font-mono text-muted-foreground">{receipt.category_label}</span>
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 pt-3 text-sm">
                <div className="space-y-2">
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs">
                    <span className="text-muted-foreground">Order Reference:</span>
                    <span className="font-mono font-semibold text-foreground">{receipt.order_number}</span>
                  </div>
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs">
                    <span className="text-muted-foreground">Particulars / Title:</span>
                    <span className="font-medium text-foreground text-right truncate max-w-[200px]">{receipt.order_title}</span>
                  </div>
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs">
                    <span className="text-muted-foreground">Reference / SKU:</span>
                    <span className="font-mono text-foreground">{receipt.reference_code} (Qty: {receipt.quantity})</span>
                  </div>
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs">
                    <span className="text-muted-foreground">Settlement Time (PHT):</span>
                    <span className="text-foreground">{receipt.paid_at_manila}</span>
                  </div>
                </div>

                <div className="space-y-2">
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs">
                    <span className="text-muted-foreground">Vatable Sales (Net 12%):</span>
                    <span className="font-mono text-foreground">₱{receipt.vatable_sales_php}</span>
                  </div>
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs">
                    <span className="text-muted-foreground">12% VAT:</span>
                    <span className="font-mono text-foreground">₱{receipt.vat_amount_php}</span>
                  </div>
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs">
                    <span className="text-muted-foreground">Gateway Processing Fee:</span>
                    <span className="font-mono text-foreground">₱{receipt.gateway_fee_php} (Covered)</span>
                  </div>
                  <div className="flex justify-between py-1 border-b border-border/50 text-xs font-bold text-emerald-600 dark:text-emerald-400">
                    <span>Total Amount Receipted:</span>
                    <span className="font-mono text-sm">₱{receipt.amount_php}</span>
                  </div>
                </div>
              </div>
            </div>

            {/* Section II: Payment Method Channel Verification Matrix */}
            <div className="p-4 rounded-lg bg-muted/30 border border-border space-y-3">
              <div className="text-xs font-semibold text-foreground uppercase tracking-wide flex items-center justify-between">
                <span>Payment Method &amp; PayMongo Rail Verification</span>
                <span className="text-[11px] font-mono text-emerald-600 dark:text-emerald-400 font-bold">
                  {receipt.payment_method_label}
                </span>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
                <div>
                  <span className="text-muted-foreground">Gateway Rail:</span>
                  <span className="ml-2 font-medium text-foreground">{receipt.payment_rail}</span>
                </div>
                <div>
                  <span className="text-muted-foreground">Provider Session ID:</span>
                  <span className="ml-2 font-mono text-foreground text-[11px]">{receipt.provider_payment_id}</span>
                </div>
              </div>

              {/* 5-Channel Badges Strip */}
              <div className="pt-2">
                <span className="text-[10px] text-muted-foreground uppercase font-mono block mb-1.5">
                  Supported PayMongo Channels &amp; Active Verification:
                </span>
                <div className="grid grid-cols-1 sm:grid-cols-5 gap-2 text-center">
                  {receipt.supported_channels.map((ch) => {
                    const Icon = getChannelIcon(ch.code)
                    return (
                      <div
                        key={ch.code}
                        className={`p-2.5 rounded-lg border text-xs flex flex-col items-center justify-between gap-1.5 transition-all ${
                          ch.selected
                            ? "bg-emerald-500/15 border-emerald-500/50 text-emerald-700 dark:text-emerald-300 font-bold ring-1 ring-emerald-500/30"
                            : "bg-background border-border/70 text-muted-foreground opacity-75"
                        }`}
                      >
                        <div className="flex items-center gap-1.5 text-xs font-semibold">
                          <Icon className={`w-4 h-4 shrink-0 ${ch.selected ? "text-emerald-600 dark:text-emerald-400" : "text-muted-foreground"}`} />
                          <span>{ch.badge}</span>
                        </div>
                        <span className="text-[10px] font-normal leading-tight">
                          {ch.name}
                        </span>
                        {ch.selected ? (
                          <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 text-[9px] font-bold uppercase tracking-wider">
                            <Check className="w-2.5 h-2.5" />
                            Settled Rail
                          </span>
                        ) : (
                          <span className="text-[9px] text-muted-foreground/60 uppercase">
                            Available
                          </span>
                        )}
                      </div>
                    )
                  })}
                </div>
              </div>
            </div>

            {/* Section III: Treasury Acknowledgement Report & Audit Digest */}
            <div className="p-4 rounded-lg bg-slate-900 text-slate-100 space-y-3 text-xs font-mono">
              <div className="text-emerald-400 font-bold text-xs uppercase tracking-wider flex items-center gap-2">
                <ShieldCheck className="w-4 h-4 text-emerald-400" />
                <span>Treasury Acknowledgement Report &amp; Audit Digest</span>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-1 border-t border-slate-800 text-[11px]">
                <div>
                  <span className="text-slate-400 block">Ledger Status:</span>
                  <span className="text-white font-semibold">{receipt.acknowledgement_status}</span>
                </div>
                <div>
                  <span className="text-slate-400 block">Outbox Integrity:</span>
                  <span className="text-white">HMAC-SHA256 Signed ({receipt.outbox_status.toUpperCase()})</span>
                </div>
                <div className="sm:col-span-2">
                  <span className="text-slate-400 block">SHA-256 Audit Verification Digest:</span>
                  <div className="flex items-center justify-between gap-2 mt-0.5 bg-slate-950 px-2.5 py-1.5 rounded border border-slate-800 text-[11px] text-emerald-300">
                    <span className="truncate select-all">{receipt.verification_hash}</span>
                    <button
                      type="button"
                      onClick={() => handleCopy(receipt.verification_hash, "hash")}
                      className="text-slate-400 hover:text-white shrink-0 p-1 rounded"
                      title="Copy SHA-256 Digest"
                    >
                      {copiedKey === "hash" ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                    </button>
                  </div>
                </div>
              </div>
            </div>

            {/* SMTP Dispatch & Resend Widget */}
            <div className="p-4 rounded-lg border border-border bg-muted/20 space-y-3">
              <div className="flex items-center justify-between flex-wrap gap-2">
                <div className="flex items-center gap-2">
                  <Mail className="w-4 h-4 text-accent" />
                  <span className="text-xs font-semibold text-foreground uppercase tracking-wide">
                    SMTP Email Receipt Dispatch
                  </span>
                </div>
                <span
                  className={`text-[11px] px-2.5 py-0.5 rounded font-mono font-bold uppercase ${
                    receipt.smtp_delivery.status === "sent"
                      ? "bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30"
                      : receipt.smtp_delivery.status === "failed"
                      ? "bg-destructive/20 text-destructive border border-destructive/30"
                      : "bg-muted text-muted-foreground border border-border"
                  }`}
                >
                  {receipt.smtp_delivery.status}
                </span>
              </div>

              <div className="text-xs text-muted-foreground">
                Target Recipient:{" "}
                <strong className="text-foreground font-mono">
                  {receipt.smtp_delivery.recipient || "Configured Treasury User"}
                </strong>
                {receipt.smtp_delivery.sent_at && (
                  <span className="ml-2 text-[11px]">
                    (Sent at {new Date(receipt.smtp_delivery.sent_at).toLocaleTimeString()})
                  </span>
                )}
                <div className="text-[11px] mt-0.5">
                  Attachment: <code className="bg-muted px-1.5 py-0.5 rounded text-foreground">{receipt.pdf_filename}</code>
                </div>
              </div>

              {/* Inline Resend Form */}
              <form onSubmit={handleResendEmail} className="pt-2 flex flex-col sm:flex-row gap-2">
                <Input
                  type="email"
                  placeholder="Enter custom email or leave blank for default..."
                  value={emailInput}
                  onChange={(e) => setEmailInput(e.target.value)}
                  className="text-xs h-9"
                />
                <Button
                  type="submit"
                  disabled={isSendingEmail}
                  size="sm"
                  className="shrink-0 h-9 gap-1.5 text-xs bg-accent text-accent-foreground hover:bg-accent/90"
                >
                  {isSendingEmail ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  ) : (
                    <Send className="w-3.5 h-3.5" />
                  )}
                  <span>{isSendingEmail ? "Sending..." : "Resend to SMTP"}</span>
                </Button>
              </form>

              {emailFeedback && (
                <div
                  className={`p-2 rounded text-xs flex items-center gap-1.5 ${
                    emailFeedback.success
                      ? "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 border border-emerald-500/20"
                      : "bg-destructive/10 text-destructive border border-destructive/20"
                  }`}
                >
                  {emailFeedback.success ? (
                    <CheckCircle2 className="w-4 h-4 shrink-0 text-emerald-500" />
                  ) : (
                    <XCircle className="w-4 h-4 shrink-0 text-destructive" />
                  )}
                  <span>{emailFeedback.message}</span>
                </div>
              )}
            </div>
          </CardContent>

          <CardFooter className="bg-muted/30 border-t border-border p-4 flex flex-col sm:flex-row items-center justify-between gap-3 text-xs text-muted-foreground">
            <div>
              Acme Manufacturing Inc. &middot; Corporate Treasury Automated Fulfillment Engine
            </div>
            <div className="flex items-center gap-2">
              <a
                href={getReceiptPdfUrl(paymentId, true)}
                download
                className="inline-flex"
              >
                <Button size="sm" variant="outline" className="h-8 gap-1 text-xs">
                  <Download className="w-3.5 h-3.5" />
                  <span>Download PDF</span>
                </Button>
              </a>
              <a
                href={getReceiptPdfUrl(paymentId, false)}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex"
              >
                <Button size="sm" variant="secondary" className="h-8 gap-1 text-xs">
                  <ExternalLink className="w-3.5 h-3.5" />
                  <span>View PDF</span>
                </Button>
              </a>
            </div>
          </CardFooter>
        </Card>
      )}
    </div>
  )
}

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
  User,
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
  const [showCustomerEmailForm, setShowCustomerEmailForm] = useState(false)

  const isTerminal = (status?: string) => {
    return status === "succeeded" || status === "failed" || status === "cancelled" || status === "expired"
  }

  const handleCopy = (text: string, key: string) => {
    navigator.clipboard.writeText(text)
    setCopiedKey(key)
    setTimeout(() => setCopiedKey(null), 2000)
  }

  // Real-time synchronization polling loop
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

        // Automatically continue real-time gateway synchronization if non-terminal
        if (!isTerminal(data.status) && pollCount < 30) {
          timer = setTimeout(() => {
            setPollCount((prev) => prev + 1)
          }, 2000)
        }
      } catch (err: any) {
        if (!active) return
        setError(err.message || "Failed to synchronize payment status")
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
      setError(err.message || "Manual synchronization error")
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
          <p className="text-sm font-medium text-foreground">Synchronizing payment status and official receipt...</p>
          <p className="text-xs text-muted-foreground font-mono">Querying PayMongo gateway rails in real-time</p>
        </div>
      </div>
    )
  }

  if (error || !payment) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <Card className="max-w-md w-full border-border bg-card shadow-sm">
          <CardHeader>
            <div className="flex items-center gap-2 text-destructive">
              <XCircle className="w-5 h-5 text-accent" />
              <CardTitle className="text-foreground">Payment Status Notice</CardTitle>
            </div>
            <CardDescription className="text-muted-foreground">{error || "Unable to locate payment record."}</CardDescription>
          </CardHeader>
          <CardFooter>
            <Button variant="outline" className="w-full border-border text-foreground hover:bg-muted" onClick={() => router.push("/")}>
              <ArrowLeft className="w-4 h-4 mr-2" />
              Return to Dashboard
            </Button>
          </CardFooter>
        </Card>
      </div>
    )
  }

  const { status, amount_minor, order, paid_at, failure_message } = payment

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
          Ref: {payment.id.slice(0, 14)}...
        </div>
      </div>

      {/* Non-succeeded Payment Status Card (Awaiting Settlement, Failed, Expired, Cancelled) */}
      {status !== "succeeded" && (
        <Card className="border-border bg-card shadow-sm">
          <CardHeader className="text-center pb-4">
            <div className="flex justify-center mb-3">
              {status === "failed" && (
                <div className="p-3 rounded-full bg-destructive/10 text-destructive border border-destructive/30">
                  <XCircle className="w-12 h-12" />
                </div>
              )}
              {(status === "expired" || status === "cancelled" || payment.failure_code === "SOURCE_EXPIRED" || payment.session_expired) && (
                <div className="p-3 rounded-full bg-accent/10 text-accent border border-accent/30">
                  <AlertTriangle className="w-12 h-12" />
                </div>
              )}
              {(status === "pending" || status === "requires_action" || status === "processing" || status === "unknown") && (
                <div className="p-3 rounded-full bg-muted text-foreground border border-border">
                  <Clock className="w-12 h-12 animate-pulse text-foreground" />
                </div>
              )}
            </div>

            <CardTitle className="text-2xl font-bold text-foreground">
              {status === "failed" && "Payment Failed"}
              {(status === "expired" || payment.failure_code === "SOURCE_EXPIRED" || payment.session_expired) && "Payment Session Expired"}
              {status === "cancelled" && "Payment Cancelled"}
              {(status === "pending" || status === "requires_action" || status === "processing") &&
                "Awaiting Payment Settlement"}
              {status === "unknown" && "Settlement Status Pending"}
            </CardTitle>

            <CardDescription className="text-sm mt-1 max-w-lg mx-auto text-muted-foreground">
              {status === "failed" && (failure_message || "The payment transaction could not be completed.")}
              {(status === "expired" || payment.failure_code === "SOURCE_EXPIRED" || payment.session_expired) &&
                "The PayMongo checkout session or payment source has timed out. Payment channels (QR Ph, Card, GCash, Maya, Online Banking) are locked on expired sessions. Please click below to generate an active checkout session."}
              {status === "cancelled" && "The payment was cancelled by user request."}
              {(status === "pending" || status === "requires_action" || status === "processing") &&
                "Real-time synchronization is monitoring PayMongo for authoritative settlement confirmation."}
              {status === "unknown" &&
                "Network verification is in progress. The ledger will synchronize automatically."}
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-4">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 p-3.5 rounded-lg bg-muted/40 border border-border text-center">
              <div>
                <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">Amount</span>
                <span className="font-bold text-foreground text-lg font-mono tabular-nums">
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

            {/* Real-Time Background Synchronization Banner */}
            {!isTerminal(status) && (
              <div className="p-4 rounded-lg bg-muted/30 border border-border text-xs text-foreground space-y-2.5">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2 font-semibold">
                    <span className="relative flex h-2.5 w-2.5">
                      <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-accent opacity-75"></span>
                      <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-accent"></span>
                    </span>
                    <span>Real-Time Gateway Synchronization Active</span>
                  </div>
                  <span className="font-mono text-[10px] text-muted-foreground uppercase tracking-wider">
                    Cycle {pollCount}/30
                  </span>
                </div>
                <p className="text-xs text-muted-foreground leading-relaxed">
                  Settlements completed via QR Ph, GCash, Maya, Online Banking, or Card synchronize automatically with the Acme Ledger. No manual action is required.
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
                className="w-full sm:flex-1 gap-2 border-border text-foreground hover:bg-muted font-mono text-xs"
              >
                <RefreshCw className={`w-3.5 h-3.5 ${isReconciling ? "animate-spin text-accent" : ""}`} />
                <span>{isReconciling ? "Synchronizing..." : "Instant Sync / Refresh"}</span>
              </Button>
            )}

            {(status === "failed" || status === "expired" || status === "cancelled" || payment.failure_code === "SOURCE_EXPIRED" || payment.session_expired) && order && (
              <Button
                className="w-full sm:flex-1 bg-accent text-accent-foreground hover:bg-accent/90 font-medium cursor-pointer"
                onClick={() => router.push(`/checkout?order_id=${order.id}`)}
              >
                {(status === "expired" || payment.failure_code === "SOURCE_EXPIRED" || payment.session_expired) ? "Start New Checkout" : "Try Again"}
              </Button>
            )}
          </CardFooter>
        </Card>
      )}

      {/* Succeeded Status: Loading Receipt State if generating */}
      {status === "succeeded" && !receipt && (
        <Card className="border-border bg-card shadow-sm text-center p-8">
          <div className="flex flex-col items-center gap-3">
            <Loader2 className="w-8 h-8 animate-spin text-accent" />
            <p className="text-sm font-semibold text-foreground">Generating Official Receipt & Treasury Acknowledgement Report...</p>
            <p className="text-xs text-muted-foreground font-mono">Synthesizing ISO-32000-1 vector PDF and recording cryptographic audit digest.</p>
          </div>
        </Card>
      )}

      {/* Succeeded Status: Customer-Facing Receipt Experience & Internal Treasury Acknowledgement */}
      {status === "succeeded" && receipt && (
        <div className="space-y-6">
          {/* Deliverable 1: Customer-Facing Receipt Card */}
          <Card className="border-border shadow-sm overflow-hidden bg-card">
            {/* 1. Header with Brand & Payment Status Focal Point */}
            <div className="p-6 sm:p-8 border-b border-border space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                <div>
                  <span className="text-[11px] font-semibold tracking-wider uppercase text-muted-foreground block">
                    {receipt.issuer.company_name} &middot; {receipt.issuer.department}
                  </span>
                </div>
                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-[#12A579]/10 text-[#0d7c5a] border border-[#12A579]/30 dark:bg-[#12A579]/20 dark:text-[#12A579] dark:border-[#12A579]/40 text-xs font-semibold self-start sm:self-center">
                  <CheckCircle2 className="w-4 h-4 text-[#12A579]" />
                  <span>Payment Confirmed</span>
                </span>
              </div>

              {/* Big Prominent Total Amount (Visual Focal Point) */}
              <div className="pt-2">
                <span className="text-[11px] text-muted-foreground uppercase font-mono tracking-wider block mb-1">
                  Total Amount Paid
                </span>
                <div className="flex items-baseline gap-2">
                  <h1 className="text-4xl sm:text-5xl font-extrabold tracking-tight font-mono tabular-nums text-foreground">
                    ₱{receipt.amount_php}
                  </h1>
                  <span className="text-xs font-semibold text-muted-foreground uppercase font-mono">
                    {receipt.currency}
                  </span>
                </div>
                <p className="text-sm text-muted-foreground mt-1.5">
                  Your payment has been confirmed.
                </p>
              </div>

              {/* Action Buttons: Primary Download PDF, Secondary Email & View */}
              <div className="pt-3 flex flex-wrap items-center gap-3">
                <a
                  href={getReceiptPdfUrl(paymentId, true)}
                  download
                  className="inline-flex"
                >
                  <Button className="h-10 px-5 gap-2 text-xs font-semibold bg-primary text-primary-foreground hover:bg-primary/90 shadow-sm">
                    <Download className="w-4 h-4" />
                    <span>Download PDF</span>
                  </Button>
                </a>
                <Button
                  variant="outline"
                  className="h-10 px-4 gap-2 text-xs font-medium border-border text-foreground hover:bg-muted"
                  onClick={() => setShowCustomerEmailForm((prev) => !prev)}
                >
                  <Mail className="w-3.5 h-3.5" />
                  <span>Email Receipt</span>
                </Button>
                <a
                  href={getReceiptPdfUrl(paymentId, false)}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex"
                >
                  <Button variant="outline" className="h-10 px-4 gap-2 text-xs font-medium border-border text-foreground hover:bg-muted">
                    <ExternalLink className="w-3.5 h-3.5" />
                    <span>View PDF</span>
                  </Button>
                </a>
              </div>

              {/* Inline Quick Customer Email Receipt Drawer */}
              {showCustomerEmailForm && (
                <div className="mt-3 p-3.5 rounded-lg bg-muted/40 border border-border space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold text-foreground">
                      Email Official Receipt PDF
                    </span>
                    <button
                      type="button"
                      onClick={() => setShowCustomerEmailForm(false)}
                      className="text-xs text-muted-foreground hover:text-foreground cursor-pointer"
                    >
                      Close
                    </button>
                  </div>
                  <form onSubmit={handleResendEmail} className="flex flex-col sm:flex-row gap-2">
                    <Input
                      type="email"
                      placeholder={receipt.customer_email || "Enter your email address..."}
                      value={emailInput}
                      onChange={(e) => setEmailInput(e.target.value)}
                      className="text-xs h-9 bg-background border-border text-foreground font-sans"
                    />
                    <Button
                      type="submit"
                      disabled={isSendingEmail}
                      size="sm"
                      className="shrink-0 h-9 gap-1.5 text-xs bg-primary text-primary-foreground hover:bg-primary/90 font-medium"
                    >
                      {isSendingEmail ? (
                        <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      ) : (
                        <Send className="w-3.5 h-3.5" />
                      )}
                      <span>{isSendingEmail ? "Sending..." : "Send Receipt"}</span>
                    </Button>
                  </form>
                  {emailFeedback && (
                    <div
                      className={`p-2 rounded text-xs flex items-center gap-1.5 ${
                        emailFeedback.success
                          ? "bg-[#12A579]/10 text-[#0d7c5a] border border-[#12A579]/30 dark:text-[#12A579]"
                          : "bg-destructive/10 text-destructive border border-destructive/30"
                      }`}
                    >
                      {emailFeedback.success ? (
                        <CheckCircle2 className="w-4 h-4 shrink-0 text-[#12A579]" />
                      ) : (
                        <XCircle className="w-4 h-4 shrink-0 text-destructive" />
                      )}
                      <span>{emailFeedback.message}</span>
                    </div>
                  )}
                </div>
              )}
            </div>

            <CardContent className="p-6 sm:p-8 space-y-6">
              {/* Receipt Identifiers Directly Below Amount */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 p-4 rounded-lg bg-muted/40 border border-border">
                <div className="flex items-center justify-between">
                  <div>
                    <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">
                      Invoice / Receipt Reference
                    </span>
                    <span className="font-mono font-bold text-foreground text-sm tracking-tight">
                      {receipt.official_receipt_number}
                    </span>
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-muted-foreground hover:text-foreground"
                    onClick={() => handleCopy(receipt.official_receipt_number, "or_num")}
                    aria-label="Copy Receipt Number"
                  >
                    {copiedKey === "or_num" ? <Check className="w-3.5 h-3.5 text-[#12A579]" /> : <Copy className="w-3.5 h-3.5" />}
                  </Button>
                </div>

                <div className="flex items-center justify-between sm:border-l sm:border-border sm:pl-4">
                  <div>
                    <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">
                      Acknowledgement No.
                    </span>
                    <span className="font-mono font-bold text-foreground text-sm tracking-tight">
                      {receipt.acknowledgement_number}
                    </span>
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-muted-foreground hover:text-foreground cursor-pointer"
                    onClick={() => handleCopy(receipt.acknowledgement_number, "ack_num")}
                    aria-label="Copy Acknowledgement Number"
                  >
                    {copiedKey === "ack_num" ? <Check className="w-3.5 h-3.5 text-[#12A579]" /> : <Copy className="w-3.5 h-3.5" />}
                  </Button>
                </div>
              </div>

              {/* Simplified Settlement Details: Single Readable Itemized Section */}
              <div className="space-y-4">
                <div className="flex items-center justify-between pb-2 border-b border-border">
                  <h3 className="text-xs font-semibold text-foreground uppercase tracking-wider">
                    Transaction Details
                  </h3>
                  <span className="text-[11px] font-mono text-muted-foreground">
                    {receipt.category_label}
                  </span>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-y-3.5 gap-x-6 text-xs p-4 rounded-lg bg-muted/20 border border-border">
                  <div>
                    <span className="text-muted-foreground block text-[11px]">Order Reference</span>
                    <span className="font-mono font-semibold text-foreground text-xs mt-0.5 block">
                      {receipt.order_number}
                    </span>
                  </div>
                  <div>
                    <span className="text-muted-foreground block text-[11px]">Settlement Date &amp; Time</span>
                    <span className="text-foreground text-xs mt-0.5 block">
                      {receipt.paid_at_manila}
                    </span>
                  </div>
                  <div>
                    <span className="text-muted-foreground block text-[11px]">Confirmed Payment Method</span>
                    <span className="inline-flex items-center gap-1.5 font-medium text-foreground text-xs mt-0.5">
                      {React.createElement(getChannelIcon(receipt.payment_method_code), {
                        className: "w-3.5 h-3.5 text-muted-foreground shrink-0",
                      })}
                      <span>{receipt.payment_method_label} &middot; PayMongo</span>
                    </span>
                  </div>
                  <div>
                    <span className="text-muted-foreground block text-[11px]">Provider Reference</span>
                    <span className="font-mono text-foreground text-xs mt-0.5 block truncate select-all" title={receipt.provider_payment_id}>
                      {receipt.provider_payment_id}
                    </span>
                  </div>
                </div>

                {/* Verified Customer & Payer Information */}
                <div className="p-4 rounded-lg bg-muted/20 border border-border text-xs space-y-3">
                  <div className="flex items-center justify-between pb-2 border-b border-border/60">
                    <span className="text-xs font-semibold text-foreground uppercase tracking-wider flex items-center gap-1.5">
                      <User className="w-3.5 h-3.5 text-accent" />
                      <span>Customer &amp; Payer Information</span>
                    </span>
                    <span className="text-[10px] font-mono text-muted-foreground uppercase bg-muted/60 px-2 py-0.5 rounded">
                      Verified Payer Record
                    </span>
                  </div>

                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                    <div>
                      <span className="text-muted-foreground block text-[11px]">Customer / Payer Name</span>
                      <span className="font-semibold text-foreground text-xs mt-0.5 block">
                        {receipt.customer?.name || receipt.customer_name || "Valued Customer"}
                      </span>
                    </div>
                    <div>
                      <span className="text-muted-foreground block text-[11px]">Email Address</span>
                      <span className="font-mono text-foreground text-xs mt-0.5 block truncate" title={receipt.customer?.email || receipt.customer_email || "Not Provided"}>
                        {receipt.customer?.email || receipt.customer_email || "Not Provided"}
                      </span>
                    </div>
                    <div>
                      <span className="text-muted-foreground block text-[11px]">Mobile Phone / GCash No.</span>
                      <span className="font-mono text-foreground text-xs mt-0.5 block">
                        {receipt.customer?.phone || receipt.customer_phone || "Not Provided"}
                      </span>
                    </div>
                  </div>
                </div>

                {/* Itemized Particulars Table with Tabular Numerals and Wrapped Descriptions */}
                <div className="border border-border rounded-lg overflow-hidden text-xs">
                  <div className="bg-muted/40 px-3.5 py-2 font-semibold text-foreground grid grid-cols-12 gap-2 text-[11px] border-b border-border">
                    <span className="col-span-6 sm:col-span-7">Particulars / Description</span>
                    <span className="col-span-2 text-center font-mono">Qty</span>
                    <span className="col-span-2 text-right font-mono hidden sm:block">Unit Cost</span>
                    <span className="col-span-4 sm:col-span-3 text-right font-mono">Amount</span>
                  </div>
                  <div className="divide-y divide-border/60">
                    {(receipt.line_items && receipt.line_items.length > 0 ? receipt.line_items : [
                      {
                        reference_code: receipt.reference_code,
                        title: receipt.order_title,
                        quantity: receipt.quantity,
                        unit_price_php: receipt.unit_price_php,
                        amount_php: receipt.amount_php,
                      }
                    ]).map((item, idx) => (
                      <div key={idx} className="px-3.5 py-2.5 grid grid-cols-12 gap-2 items-center">
                        <div className="col-span-6 sm:col-span-7 pr-2">
                          <span className="font-mono text-[10px] text-muted-foreground block">
                            {item.reference_code}
                          </span>
                          <p className="text-foreground font-medium text-xs break-words whitespace-normal leading-relaxed">
                            {item.title}
                          </p>
                        </div>
                        <div className="col-span-2 text-center font-mono tabular-nums text-foreground">
                          {item.quantity}
                        </div>
                        <div className="col-span-2 text-right font-mono tabular-nums text-muted-foreground hidden sm:block">
                          ₱{item.unit_price_php}
                        </div>
                        <div className="col-span-4 sm:col-span-3 text-right font-mono tabular-nums font-semibold text-foreground">
                          ₱{item.amount_php}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              {/* Payment Summary Table with Right-Aligned Tabular Numerals */}
              <div className="space-y-3 pt-2">
                <div className="flex items-center justify-between pb-2 border-b border-border">
                  <h3 className="text-xs font-semibold text-foreground uppercase tracking-wider">
                    Payment Summary
                  </h3>
                  <span className="text-[11px] text-muted-foreground font-mono">
                    {receipt.tax_classification_label || "12% VAT Inclusive"}
                  </span>
                </div>

                <div className="divide-y divide-border/60 text-xs">
                  {receipt.tax_classification === "vat_exempt" ? (
                    <div className="flex justify-between py-2">
                      <span className="text-muted-foreground">VAT-Exempt Sales:</span>
                      <span className="font-mono tabular-nums text-foreground">₱{receipt.vat_exempt_sales_php || receipt.amount_php}</span>
                    </div>
                  ) : receipt.tax_classification === "zero_rated" ? (
                    <div className="flex justify-between py-2">
                      <span className="text-muted-foreground">Zero-Rated Sales (0% VAT):</span>
                      <span className="font-mono tabular-nums text-foreground">₱{receipt.zero_rated_sales_php || receipt.amount_php}</span>
                    </div>
                  ) : receipt.tax_classification === "non_vat" ? (
                    <div className="flex justify-between py-2">
                      <span className="text-muted-foreground">Non-VAT Sales:</span>
                      <span className="font-mono tabular-nums text-foreground">₱{receipt.non_vat_sales_php || receipt.amount_php}</span>
                    </div>
                  ) : (
                    <>
                      <div className="flex justify-between py-2">
                        <span className="text-muted-foreground">Vatable Sales (Net of 12% VAT):</span>
                        <span className="font-mono tabular-nums text-foreground">₱{receipt.vatable_sales_php}</span>
                      </div>
                      <div className="flex justify-between py-2">
                        <span className="text-muted-foreground">Value-Added Tax (12% VAT):</span>
                        <span className="font-mono tabular-nums text-foreground">₱{receipt.vat_amount_php}</span>
                      </div>
                    </>
                  )}
                  <div className="flex justify-between py-2">
                    <span className="text-muted-foreground">Processing Fee Charged to Customer:</span>
                    <span className="font-mono tabular-nums text-foreground">₱0.00 (Waived)</span>
                  </div>
                  <div className="flex justify-between py-3 font-bold text-foreground text-sm">
                    <span className="text-sm">Total Paid:</span>
                    <span className="font-mono tabular-nums text-lg font-extrabold text-foreground">₱{receipt.amount_php}</span>
                  </div>
                </div>
              </div>
            </CardContent>

            <CardFooter className="bg-muted/30 border-t border-border p-4 sm:p-5 flex flex-col sm:flex-row items-center justify-between gap-3 text-xs text-muted-foreground">
              <div>
                Acme Manufacturing Inc. &middot; Corporate Accounting &amp; Treasury
              </div>
              <div className="text-[11px] font-mono">
                TIN: {receipt.issuer.tin}
              </div>
            </CardFooter>
          </Card>

          {/* Deliverable 2: Internal Treasury Acknowledgement & Audit Disclosure */}
          <details className="group border border-border rounded-lg bg-card/60 overflow-hidden shadow-xs transition-colors">
            <summary className="flex items-center justify-between p-4 cursor-pointer hover:bg-muted/30 select-none">
              <div className="flex items-center gap-2">
                <ShieldCheck className="w-4 h-4 text-muted-foreground group-hover:text-foreground transition-colors" />
                <div>
                  <span className="text-xs font-semibold text-foreground uppercase tracking-wider block">
                    Payment Verification &amp; Treasury Audit Details
                  </span>
                  <span className="text-[11px] text-muted-foreground">
                    Provider confirmation, ledger posting state, and SHA-256 audit digest
                  </span>
                </div>
              </div>
              <span className="text-xs text-muted-foreground group-open:rotate-180 transition-transform font-mono px-2">
                &#9660;
              </span>
            </summary>

            <div className="p-4 sm:p-5 pt-2 border-t border-border space-y-4 text-xs font-mono">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-[11px]">
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">Payment Status</span>
                  <span className="text-foreground font-semibold mt-0.5 flex items-center gap-1.5">
                    <span className="inline-block w-2 h-2 rounded-full bg-[#12A579]"></span>
                    <span>{receipt.payment_status || "PAYMENT CONFIRMED"}</span>
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">Receipt Generation</span>
                  <span className="text-foreground font-semibold mt-0.5 block">
                    {receipt.receipt_status || "RECEIPT GENERATED"}
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">General Ledger Status</span>
                  <span className={`font-semibold mt-0.5 block ${receipt.ledger_status === "POSTED TO ACME GENERAL LEDGER" ? "text-[#0d7c5a] dark:text-[#12A579]" : "text-amber-600 dark:text-amber-400"}`}>
                    {receipt.ledger_status || "PENDING LEDGER POSTING"}
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">Outbox Delivery State</span>
                  <span className="text-foreground font-semibold mt-0.5 block">
                    {receipt.outbox_status.toUpperCase()} (SHA-256 Audit Trail)
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">Provider Payment / Session ID</span>
                  <span className="text-foreground select-all break-all mt-0.5 block">
                    {receipt.provider_payment_id}
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">Payment Attempt ID</span>
                  <span className="text-foreground select-all break-all mt-0.5 block">
                    {receipt.payment_attempt_id}
                  </span>
                </div>
                <div className="sm:col-span-2">
                  <span className="text-muted-foreground block text-[10px] uppercase">Customer &amp; Payer Record</span>
                  <span className="font-mono text-foreground text-xs select-all mt-0.5 block">
                    {receipt.customer?.name || receipt.customer_name || "Valued Customer"} &middot; {receipt.customer?.email || receipt.customer_email || "No Email"} &middot; {receipt.customer?.phone || receipt.customer_phone || "No Phone"}
                  </span>
                </div>
                <div className="sm:col-span-2">
                  <span className="text-muted-foreground block text-[10px] uppercase">SHA-256 Audit Verification Digest</span>
                  <div className="flex items-center justify-between gap-2 mt-1 bg-background px-3 py-2 rounded border border-border text-[11px] text-foreground">
                    <span className="truncate select-all font-mono">{receipt.verification_hash}</span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="h-6 w-6 text-muted-foreground hover:text-foreground shrink-0 cursor-pointer"
                      onClick={() => handleCopy(receipt.verification_hash, "hash")}
                      title="Copy SHA-256 Digest"
                      aria-label="Copy SHA-256 Digest"
                    >
                      {copiedKey === "hash" ? <Check className="w-3.5 h-3.5 text-[#12A579]" /> : <Copy className="w-3.5 h-3.5 text-muted-foreground" />}
                    </Button>
                  </div>
                </div>
              </div>

              {/* Supported PayMongo Channels & Active Verification Strip */}
              <div className="pt-3 border-t border-border/70">
                <span className="text-[10px] text-muted-foreground uppercase tracking-wider block mb-2 font-mono">
                  Supported PayMongo Rails &amp; Confirmed Rail:
                </span>
                <div className="grid grid-cols-2 sm:grid-cols-5 gap-2 text-center">
                  {receipt.supported_channels.map((ch) => {
                    const Icon = getChannelIcon(ch.code)
                    return (
                      <div
                        key={ch.code}
                        className={`p-2.5 rounded-md border text-xs flex flex-col items-center justify-between gap-1 transition-all ${
                          ch.selected
                            ? "bg-primary text-primary-foreground border-primary font-bold shadow-xs"
                            : "bg-background border-border text-muted-foreground opacity-75"
                        }`}
                      >
                        <div className="flex items-center gap-1.5 text-xs font-semibold">
                          <Icon className="w-3.5 h-3.5 shrink-0" />
                          <span>{ch.badge}</span>
                        </div>
                        <span className="text-[9px] uppercase font-mono tracking-wider">
                          {ch.selected ? "Settled Rail" : "Supported"}
                        </span>
                      </div>
                    )
                  })}
                </div>
              </div>

              {/* SMTP Dispatch & Resend Form */}
              <div className="pt-3 border-t border-border/70 space-y-3">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <div className="flex items-center gap-1.5 text-foreground font-semibold text-xs">
                    <Mail className="w-3.5 h-3.5 text-muted-foreground" />
                    <span>SMTP Email Receipt Dispatch</span>
                  </div>
                  <span
                    className={`text-[10px] px-2 py-0.5 rounded font-mono font-bold uppercase ${
                      receipt.smtp_delivery.status === "sent"
                        ? "bg-emerald-50 text-emerald-700 border border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-400 dark:border-emerald-800"
                        : receipt.smtp_delivery.status === "failed"
                        ? "bg-destructive/10 text-destructive border border-destructive/30"
                        : "bg-muted text-muted-foreground border border-border"
                    }`}
                  >
                    {receipt.smtp_delivery.status}
                  </span>
                </div>

                <div className="text-[11px] text-muted-foreground">
                  Target Recipient:{" "}
                  <strong className="text-foreground font-mono">
                    {receipt.smtp_delivery.recipient || receipt.customer_email || "Configured Treasury User"}
                  </strong>
                  {receipt.smtp_delivery.sent_at && (
                    <span className="ml-2 text-[10px]">
                      (Sent at {new Date(receipt.smtp_delivery.sent_at).toLocaleTimeString()})
                    </span>
                  )}
                  <div className="text-[10px] mt-0.5">
                    Attachment: <code className="bg-muted px-1.5 py-0.5 rounded text-foreground font-mono">{receipt.pdf_filename}</code>
                  </div>
                </div>

                {/* Inline Resend Form */}
                <form onSubmit={handleResendEmail} className="pt-1 flex flex-col sm:flex-row gap-2">
                  <Input
                    type="email"
                    placeholder="Enter custom email or leave blank for default..."
                    value={emailInput}
                    onChange={(e) => setEmailInput(e.target.value)}
                    className="text-xs h-8 bg-background border-border text-foreground font-sans"
                  />
                  <Button
                    type="submit"
                    disabled={isSendingEmail}
                    size="sm"
                    className="shrink-0 h-8 gap-1.5 text-xs bg-primary text-primary-foreground hover:bg-primary/90 font-medium"
                  >
                    {isSendingEmail ? (
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                    ) : (
                      <Send className="w-3.5 h-3.5" />
                    )}
                    <span>{isSendingEmail ? "Sending..." : "Resend Email"}</span>
                  </Button>
                </form>

                {emailFeedback && (
                  <div
                    className={`p-2.5 rounded-md text-xs flex items-center gap-1.5 font-sans ${
                      emailFeedback.success
                        ? "bg-emerald-50 text-emerald-700 border border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-400 dark:border-emerald-800 font-medium"
                        : "bg-destructive/10 text-destructive border border-destructive/30"
                    }`}
                  >
                    {emailFeedback.success ? (
                      <CheckCircle2 className="w-4 h-4 shrink-0 text-emerald-600" />
                    ) : (
                      <XCircle className="w-4 h-4 shrink-0 text-destructive" />
                    )}
                    <span>{emailFeedback.message}</span>
                  </div>
                )}
              </div>

              {/* Statutory Note pursuant to BIR RMC No. 77-2024 */}
              <div className="pt-2 text-[10px] text-muted-foreground/80 leading-normal border-t border-border/70 font-sans">
                {receipt.tax_compliance_note || "Electronic payment settlement record. Pursuant to BIR RMC No. 77-2024, official commercial VAT Invoices are issued separately by Corporate Accounting upon customer tax identification verification."}
              </div>
            </div>
          </details>
        </div>
      )}
    </div>
  )
}

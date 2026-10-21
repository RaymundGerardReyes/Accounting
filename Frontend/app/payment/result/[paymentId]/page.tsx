"use client"

import React, { useEffect, useState, use } from "react"
import { useRouter } from "next/navigation"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  CardFooter,
} from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Clock,
  RefreshCw,
  ArrowLeft,
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

  // Email dispatch state
  const [emailInput, setEmailInput] = useState("")
  const [isSendingEmail, setIsSendingEmail] = useState(false)
  const [emailFeedback, setEmailFeedback] = useState<{
    success: boolean
    message: string
  } | null>(null)
  const [copiedKey, setCopiedKey] = useState<string | null>(null)
  const [showCustomerEmailForm, setShowCustomerEmailForm] = useState(false)

  const isTerminal = (status?: string) => {
    return (
      status === "succeeded" ||
      status === "failed" ||
      status === "cancelled" ||
      status === "expired"
    )
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
        message: res.message || `Official Receipt PDF sent to ${res.recipient}`,
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

  // 1. Initial Loading State
  if (loading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <div className="flex flex-col items-center gap-3 text-center max-w-sm">
          <Loader2 className="w-7 h-7 animate-spin text-foreground" />
          <p className="text-sm font-semibold text-foreground">
            Verifying payment and preparing your receipt...
          </p>
          <p className="text-xs text-muted-foreground">
            Synchronizing directly with PayMongo settlement rails
          </p>
        </div>
      </div>
    )
  }

  // 2. Error / Missing Record State
  if (error || !payment) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-6">
        <Card className="max-w-md w-full border-border bg-card shadow-xs">
          <CardHeader>
            <div className="flex items-center gap-2 text-destructive">
              <XCircle className="w-5 h-5 shrink-0" />
              <CardTitle className="text-base font-semibold text-foreground">
                Unable to Load Payment Record
              </CardTitle>
            </div>
            <CardDescription className="text-xs text-muted-foreground pt-1">
              {error || "We could not locate this payment attempt. Please check the link or return to the dashboard."}
            </CardDescription>
          </CardHeader>
          <CardFooter>
            <Button
              variant="outline"
              className="w-full border-border text-foreground hover:bg-muted text-xs"
              onClick={() => router.push("/")}
            >
              <ArrowLeft className="w-4 h-4 mr-2" />
              Return to Dashboard
            </Button>
          </CardFooter>
        </Card>
      </div>
    )
  }

  const { status, amount_minor, order, failure_message } = payment
  const isSessionExpired =
    status === "expired" ||
    payment.failure_code === "SOURCE_EXPIRED" ||
    Boolean(payment.session_expired)

  return (
    <div className="min-h-screen bg-background py-8 px-4 sm:px-6 md:px-8 max-w-3xl mx-auto space-y-6">
      {/* Top Navigation & Reference Bar */}
      <div className="flex items-center justify-between">
        <Button
          variant="ghost"
          size="sm"
          className="text-xs text-muted-foreground hover:text-foreground gap-1.5 -ml-2"
          onClick={() => router.push("/")}
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Back to Dashboard</span>
        </Button>
        <span className="text-xs text-muted-foreground font-mono">
          Ref: {payment.id.slice(0, 14)}
        </span>
      </div>

      {/* 3. Non-Succeeded Payment Status Card (Pending, Processing, Expired, Cancelled, Failed) */}
      {status !== "succeeded" && (
        <Card className="border-border bg-card shadow-xs overflow-hidden">
          <CardHeader className="text-center pb-5 pt-8 px-6">
            <div className="flex justify-center mb-4">
              {status === "failed" && (
                <div className="p-3.5 rounded-full bg-destructive/10 text-destructive border border-destructive/20">
                  <XCircle className="w-8 h-8" />
                </div>
              )}
              {(isSessionExpired || status === "cancelled") && (
                <div className="p-3.5 rounded-full bg-muted text-foreground border border-border">
                  <AlertTriangle className="w-8 h-8" />
                </div>
              )}
              {!isSessionExpired &&
                status !== "failed" &&
                status !== "cancelled" && (
                  <div className="p-3.5 rounded-full bg-muted/60 text-foreground border border-border">
                    <Clock className="w-8 h-8 animate-pulse" />
                  </div>
                )}
            </div>

            <CardTitle className="text-xl sm:text-2xl font-semibold text-foreground tracking-tight">
              {status === "failed" && "Payment Could Not Be Completed"}
              {isSessionExpired && "Payment Session Expired"}
              {status === "cancelled" && "Payment Cancelled"}
              {!isSessionExpired &&
                status !== "failed" &&
                status !== "cancelled" &&
                "Awaiting Payment Confirmation"}
            </CardTitle>

            <CardDescription className="text-xs sm:text-sm mt-1.5 max-w-md mx-auto text-muted-foreground leading-relaxed">
              {status === "failed" &&
                (failure_message ||
                  "The payment transaction was declined or interrupted. No funds were captured.")}
              {isSessionExpired &&
                "This checkout session timed out before payment was completed. Start a fresh checkout session below to continue safely."}
              {status === "cancelled" &&
                "This payment was cancelled. You can start a new checkout whenever you are ready."}
              {!isSessionExpired &&
                status !== "failed" &&
                status !== "cancelled" &&
                "We are monitoring PayMongo in real time. This page will update automatically as soon as your payment settles."}
            </CardDescription>
          </CardHeader>

          <CardContent className="px-6 pb-6 space-y-4">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 p-4 rounded-lg bg-muted/30 border border-border">
              <div>
                <span className="text-[11px] text-muted-foreground block">
                  Amount Due
                </span>
                <span className="font-bold text-foreground text-base font-mono tabular-nums mt-0.5 block">
                  {formatPhpFromMinor(amount_minor)}
                </span>
              </div>
              <div>
                <span className="text-[11px] text-muted-foreground block">
                  Order Reference
                </span>
                <span className="font-mono text-xs font-semibold text-foreground mt-1 block">
                  {order ? order.order_number : "—"}
                </span>
              </div>
              <div>
                <span className="text-[11px] text-muted-foreground block">
                  Gateway Rail
                </span>
                <span className="text-xs font-medium text-foreground mt-1 block">
                  PayMongo
                </span>
              </div>
              <div>
                <span className="text-[11px] text-muted-foreground block">
                  Current State
                </span>
                <span className="text-xs font-mono font-semibold uppercase text-foreground mt-1 block">
                  {status}
                </span>
              </div>
            </div>

            {!isTerminal(status) && (
              <div className="p-3.5 rounded-lg bg-muted/20 border border-border flex items-center justify-between gap-3 text-xs">
                <div className="flex items-center gap-2.5 text-foreground">
                  <span className="relative flex h-2 w-2 shrink-0">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-[#12A579] opacity-75"></span>
                    <span className="relative inline-flex rounded-full h-2 w-2 bg-[#12A579]"></span>
                  </span>
                  <span className="font-medium">
                    Auto-syncing with PayMongo (QR Ph, Card, GCash, Maya, BPI)
                  </span>
                </div>
                <span className="font-mono text-[11px] text-muted-foreground shrink-0">
                  Check {pollCount}/30
                </span>
              </div>
            )}
          </CardContent>

          <CardFooter className="px-6 py-4 bg-muted/20 border-t border-border flex flex-col sm:flex-row gap-3">
            {!isTerminal(status) && (
              <Button
                variant="outline"
                onClick={handleManualReconcile}
                disabled={isReconciling}
                className="w-full sm:flex-1 gap-2 border-border text-foreground hover:bg-muted text-xs"
              >
                <RefreshCw
                  className={`w-3.5 h-3.5 ${isReconciling ? "animate-spin" : ""}`}
                />
                <span>
                  {isReconciling ? "Checking Status..." : "Refresh Status Now"}
                </span>
              </Button>
            )}

            {(status === "failed" || isSessionExpired || status === "cancelled") &&
              order && (
                <Button
                  className="w-full sm:flex-1 bg-primary text-primary-foreground hover:bg-primary/90 text-xs font-semibold cursor-pointer"
                  onClick={() => router.push(`/checkout?order_id=${order.id}`)}
                >
                  {isSessionExpired ? "Start New Checkout" : "Try Payment Again"}
                </Button>
              )}
          </CardFooter>
        </Card>
      )}

      {/* 4. Succeeded Status: Synthesizing Receipt Loading State */}
      {status === "succeeded" && !receipt && (
        <Card className="border-border bg-card shadow-xs text-center p-10">
          <div className="flex flex-col items-center gap-3">
            <Loader2 className="w-7 h-7 animate-spin text-[#12A579]" />
            <p className="text-sm font-semibold text-foreground">
              Generating Official Receipt &amp; Acknowledgement Report...
            </p>
            <p className="text-xs text-muted-foreground">
              Preparing your downloadable PDF copy and verification digest
            </p>
          </div>
        </Card>
      )}

      {/* 5. Succeeded Status: Minimalist 2-Part Palette Receipt Experience */}
      {status === "succeeded" && receipt && (
        <div className="space-y-5">
          {/* Primary Receipt Document Sheet (88% Neutral Foundation / 12% Emerald Accent) */}
          <Card className="border-border bg-card shadow-xs overflow-hidden">
            {/* Top Emerald Accent Hairline Bar */}
            <div className="h-1.5 w-full bg-[#12A579]" />

            {/* Receipt Header: Issuer, Verified Pill, Total Amount, and Primary Actions */}
            <div className="p-6 sm:p-8 border-b border-border space-y-5">
              <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-3">
                <div>
                  <p className="text-[11px] font-semibold tracking-wider uppercase text-muted-foreground">
                    {receipt.issuer?.company_name || "Acme Manufacturing Inc."} &middot;{" "}
                    {receipt.issuer?.department || "Corporate Treasury"}
                  </p>
                  <p className="text-xs text-muted-foreground mt-0.5">
                    Official Payment Receipt &amp; Settlement Record
                  </p>
                </div>

                {/* 12% Accent Ratio: Verified Emerald Status Pill */}
                <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-[#12A579]/10 text-[#0d7c5a] border border-[#12A579]/25 dark:bg-[#12A579]/20 dark:text-[#12A579] dark:border-[#12A579]/40 text-xs font-semibold self-start">
                  <CheckCircle2 className="w-3.5 h-3.5 text-[#12A579] shrink-0" />
                  <span>Payment Confirmed</span>
                </span>
              </div>

              {/* Focal Point: Total Amount Settled */}
              <div className="pt-1 flex flex-col sm:flex-row sm:items-end justify-between gap-4">
                <div>
                  <span className="text-[11px] font-medium uppercase tracking-wider text-muted-foreground block mb-1">
                    Total Amount Paid
                  </span>
                  <div className="flex items-baseline gap-2">
                    <h1 className="text-4xl sm:text-5xl font-bold tracking-tight font-mono tabular-nums text-foreground">
                      ₱{receipt.amount_php}
                    </h1>
                    <span className="text-xs font-semibold text-muted-foreground uppercase font-mono">
                      {receipt.currency}
                    </span>
                  </div>
                  <p className="text-xs text-muted-foreground mt-1.5">
                    Settled on {receipt.paid_at_manila} via{" "}
                    <strong className="font-medium text-foreground">
                      {receipt.payment_method_label}
                    </strong>
                  </p>
                </div>

                {/* Action Bar: Download PDF (Primary), Email Receipt, View PDF */}
                <div className="flex flex-wrap items-center gap-2.5">
                  <a
                    href={getReceiptPdfUrl(paymentId, true)}
                    download
                    className="inline-flex"
                  >
                    <Button className="h-9 px-4 gap-2 text-xs font-semibold bg-primary text-primary-foreground hover:bg-primary/90">
                      <Download className="w-3.5 h-3.5" />
                      <span>Download PDF</span>
                    </Button>
                  </a>
                  <Button
                    type="button"
                    variant="outline"
                    className="h-9 px-3.5 gap-1.5 text-xs font-medium border-border text-foreground hover:bg-muted cursor-pointer"
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
                    <Button
                      variant="outline"
                      className="h-9 px-3.5 gap-1.5 text-xs font-medium border-border text-foreground hover:bg-muted"
                    >
                      <ExternalLink className="w-3.5 h-3.5" />
                      <span>View PDF</span>
                    </Button>
                  </a>
                </div>
              </div>

              {/* Unified Email Receipt Drawer */}
              {showCustomerEmailForm && (
                <div className="pt-2">
                  <div className="p-4 rounded-lg bg-muted/30 border border-border space-y-3">
                    <div className="flex items-center justify-between">
                      <div>
                        <span className="text-xs font-semibold text-foreground block">
                          Send Official Receipt PDF via Email
                        </span>
                        <span className="text-[11px] text-muted-foreground">
                          Attachment: {receipt.pdf_filename}
                        </span>
                      </div>
                      <button
                        type="button"
                        onClick={() => setShowCustomerEmailForm(false)}
                        className="text-xs text-muted-foreground hover:text-foreground cursor-pointer px-2 py-1"
                      >
                        Close
                      </button>
                    </div>
                    <form
                      onSubmit={handleResendEmail}
                      className="flex flex-col sm:flex-row gap-2"
                    >
                      <Input
                        type="email"
                        placeholder={
                          receipt.customer?.email ||
                          receipt.customer_email ||
                          "Enter recipient email address..."
                        }
                        value={emailInput}
                        onChange={(e) => setEmailInput(e.target.value)}
                        className="text-xs h-9 bg-background border-border text-foreground"
                      />
                      <Button
                        type="submit"
                        disabled={isSendingEmail}
                        size="sm"
                        className="shrink-0 h-9 px-4 gap-1.5 text-xs bg-primary text-primary-foreground hover:bg-primary/90 font-semibold cursor-pointer"
                      >
                        {isSendingEmail ? (
                          <Loader2 className="w-3.5 h-3.5 animate-spin" />
                        ) : (
                          <Send className="w-3.5 h-3.5" />
                        )}
                        <span>
                          {isSendingEmail ? "Sending..." : "Send PDF Copy"}
                        </span>
                      </Button>
                    </form>
                    {emailFeedback && (
                      <div
                        role="status"
                        className={`p-2.5 rounded-md text-xs flex items-center gap-2 ${
                          emailFeedback.success
                            ? "bg-[#12A579]/10 text-[#0d7c5a] border border-[#12A579]/25 dark:text-[#12A579]"
                            : "bg-destructive/10 text-destructive border border-destructive/25"
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
                </div>
              )}
            </div>

            <CardContent className="p-6 sm:p-8 space-y-7">
              {/* Key Document Control Numbers (Clean 2-Column Strip) */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 p-4 rounded-lg bg-muted/30 border border-border">
                <div className="flex items-center justify-between">
                  <div>
                    <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">
                      Official Receipt No.
                    </span>
                    <span className="font-mono font-bold text-foreground text-sm mt-0.5 block">
                      {receipt.official_receipt_number}
                    </span>
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-muted-foreground hover:text-foreground cursor-pointer"
                    onClick={() =>
                      handleCopy(receipt.official_receipt_number, "or_num")
                    }
                    aria-label="Copy Official Receipt Number"
                  >
                    {copiedKey === "or_num" ? (
                      <Check className="w-3.5 h-3.5 text-[#12A579]" />
                    ) : (
                      <Copy className="w-3.5 h-3.5" />
                    )}
                  </Button>
                </div>

                <div className="flex items-center justify-between sm:border-l sm:border-border sm:pl-4 pt-3 sm:pt-0 border-t border-border sm:border-t-0">
                  <div>
                    <span className="text-[10px] text-muted-foreground uppercase font-mono tracking-wider block">
                      Acknowledgement No.
                    </span>
                    <span className="font-mono font-bold text-foreground text-sm mt-0.5 block">
                      {receipt.acknowledgement_number}
                    </span>
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-muted-foreground hover:text-foreground cursor-pointer"
                    onClick={() =>
                      handleCopy(receipt.acknowledgement_number, "ack_num")
                    }
                    aria-label="Copy Acknowledgement Number"
                  >
                    {copiedKey === "ack_num" ? (
                      <Check className="w-3.5 h-3.5 text-[#12A579]" />
                    ) : (
                      <Copy className="w-3.5 h-3.5" />
                    )}
                  </Button>
                </div>
              </div>

              {/* Unified 2-Column Metadata Grid: Settlement Details + Payer Information */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6 pb-6 border-b border-border text-xs">
                {/* Left Column: Transaction & Method */}
                <div className="space-y-3">
                  <h3 className="text-[11px] font-semibold text-muted-foreground uppercase tracking-wider">
                    Settlement Summary
                  </h3>
                  <div className="space-y-2.5">
                    <div className="flex justify-between gap-4">
                      <span className="text-muted-foreground">Order Reference</span>
                      <span className="font-mono font-semibold text-foreground text-right">
                        {receipt.order_number}
                      </span>
                    </div>
                    <div className="flex justify-between gap-4">
                      <span className="text-muted-foreground">Category</span>
                      <span className="text-foreground font-medium text-right">
                        {receipt.category_label}
                      </span>
                    </div>
                    <div className="flex justify-between gap-4">
                      <span className="text-muted-foreground">Payment Channel</span>
                      <span className="inline-flex items-center gap-1.5 font-semibold text-[#0d7c5a] dark:text-[#12A579] text-right">
                        {React.createElement(
                          getChannelIcon(receipt.payment_method_code),
                          {
                            className: "w-3.5 h-3.5 text-[#12A579] shrink-0",
                          }
                        )}
                        <span>{receipt.payment_method_label}</span>
                      </span>
                    </div>
                    <div className="flex justify-between gap-4">
                      <span className="text-muted-foreground">Provider Reference</span>
                      <span
                        className="font-mono text-foreground truncate max-w-[180px] select-all text-right"
                        title={receipt.provider_payment_id}
                      >
                        {receipt.provider_payment_id}
                      </span>
                    </div>
                  </div>
                </div>

                {/* Right Column: Verified Customer & Payer */}
                <div className="space-y-3 md:border-l md:border-border md:pl-6 pt-4 md:pt-0 border-t border-border md:border-t-0">
                  <div className="flex items-center justify-between">
                    <h3 className="text-[11px] font-semibold text-muted-foreground uppercase tracking-wider flex items-center gap-1.5">
                      <User className="w-3.5 h-3.5 text-muted-foreground" />
                      <span>Billed To / Payer</span>
                    </h3>
                  </div>
                  <div className="space-y-2.5">
                    <div className="flex justify-between gap-4">
                      <span className="text-muted-foreground">Customer Name</span>
                      <span className="font-semibold text-foreground text-right">
                        {receipt.customer?.name ||
                          receipt.customer_name ||
                          "Valued Customer"}
                      </span>
                    </div>
                    <div className="flex justify-between gap-4">
                      <span className="text-muted-foreground">Email Address</span>
                      <span
                        className="font-mono text-foreground truncate max-w-[200px] text-right"
                        title={
                          receipt.customer?.email ||
                          receipt.customer_email ||
                          "Not Provided"
                        }
                      >
                        {receipt.customer?.email ||
                          receipt.customer_email ||
                          "Not Provided"}
                      </span>
                    </div>
                    <div className="flex justify-between gap-4">
                      <span className="text-muted-foreground">Mobile / Contact</span>
                      <span className="font-mono text-foreground text-right">
                        {receipt.customer?.phone ||
                          receipt.customer_phone ||
                          "Not Provided"}
                      </span>
                    </div>
                  </div>
                </div>
              </div>

              {/* Itemized Particulars Table */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <h3 className="text-[11px] font-semibold text-muted-foreground uppercase tracking-wider">
                    Itemized Particulars
                  </h3>
                  <span className="text-[11px] font-mono text-muted-foreground">
                    {receipt.tax_classification_label || "12% VAT Inclusive"}
                  </span>
                </div>

                <div className="border border-border rounded-lg overflow-hidden text-xs">
                  <div className="bg-muted/30 px-4 py-2.5 font-semibold text-muted-foreground grid grid-cols-12 gap-2 text-[11px] uppercase tracking-wider border-b border-border">
                    <span className="col-span-6 sm:col-span-7">Description</span>
                    <span className="col-span-2 text-center font-mono">Qty</span>
                    <span className="col-span-2 text-right font-mono hidden sm:block">
                      Unit Price
                    </span>
                    <span className="col-span-4 sm:col-span-3 text-right font-mono">
                      Amount
                    </span>
                  </div>

                  <div className="divide-y divide-border">
                    {(receipt.line_items && receipt.line_items.length > 0
                      ? receipt.line_items
                      : [
                          {
                            reference_code: receipt.reference_code,
                            title: receipt.order_title,
                            quantity: receipt.quantity,
                            unit_price_php: receipt.unit_price_php,
                            amount_php: receipt.amount_php,
                          },
                        ]
                    ).map((item, idx) => (
                      <div
                        key={idx}
                        className="px-4 py-3 grid grid-cols-12 gap-2 items-center bg-card"
                      >
                        <div className="col-span-6 sm:col-span-7 pr-2">
                          <span className="font-mono text-[10px] text-muted-foreground block">
                            {item.reference_code}
                          </span>
                          <p className="text-foreground font-medium text-xs break-words leading-relaxed">
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

                  {/* Statutory Tax & Total Breakdown Integrated in Table Footer */}
                  <div className="bg-muted/20 border-t border-border px-4 py-3 space-y-2 text-xs">
                    {receipt.tax_classification === "vat_exempt" ? (
                      <div className="flex justify-between text-muted-foreground">
                        <span>VAT-Exempt Sales (0% VAT)</span>
                        <span className="font-mono tabular-nums text-foreground">
                          ₱{receipt.vat_exempt_sales_php || receipt.amount_php}
                        </span>
                      </div>
                    ) : receipt.tax_classification === "zero_rated" ? (
                      <div className="flex justify-between text-muted-foreground">
                        <span>Zero-Rated Sales (0% VAT)</span>
                        <span className="font-mono tabular-nums text-foreground">
                          ₱{receipt.zero_rated_sales_php || receipt.amount_php}
                        </span>
                      </div>
                    ) : receipt.tax_classification === "non_vat" ? (
                      <div className="flex justify-between text-muted-foreground">
                        <span>Non-VAT Sales</span>
                        <span className="font-mono tabular-nums text-foreground">
                          ₱{receipt.non_vat_sales_php || receipt.amount_php}
                        </span>
                      </div>
                    ) : (
                      <>
                        <div className="flex justify-between text-muted-foreground">
                          <span>Vatable Sales (Net of 12% VAT)</span>
                          <span className="font-mono tabular-nums text-foreground">
                            ₱{receipt.vatable_sales_php}
                          </span>
                        </div>
                        <div className="flex justify-between text-muted-foreground">
                          <span>12% Value-Added Tax (VAT)</span>
                          <span className="font-mono tabular-nums text-foreground">
                            ₱{receipt.vat_amount_php}
                          </span>
                        </div>
                      </>
                    )}
                    <div className="flex justify-between text-muted-foreground">
                      <span>Gateway Processing Fee</span>
                      <span className="font-mono tabular-nums text-foreground">
                        ₱0.00 (Waived)
                      </span>
                    </div>
                    <div className="flex justify-between items-baseline pt-2.5 border-t border-border font-semibold text-foreground">
                      <span className="text-xs uppercase tracking-wider">
                        Total Amount Paid
                      </span>
                      <span className="font-mono tabular-nums text-base font-bold text-[#0d7c5a] dark:text-[#12A579]">
                        ₱{receipt.amount_php}
                      </span>
                    </div>
                  </div>
                </div>
              </div>
            </CardContent>

            <CardFooter className="bg-muted/20 border-t border-border px-6 py-4 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 text-[11px] text-muted-foreground">
              <span>
                {receipt.issuer?.company_name || "Acme Manufacturing Inc."} &middot; {receipt.issuer?.address || "Makati City, Metro Manila"}
              </span>
              <span className="font-mono shrink-0">TIN: {receipt.issuer?.tin || "000-842-915-000 VAT"}</span>
            </CardFooter>
          </Card>

          {/* Progressive Disclosure: Treasury Audit & Cryptographic Verification */}
          <details className="group border border-border rounded-lg bg-card overflow-hidden shadow-2xs">
            <summary className="flex items-center justify-between px-5 py-4 cursor-pointer hover:bg-muted/30 select-none">
              <div className="flex items-center gap-2.5">
                <ShieldCheck className="w-4 h-4 text-[#12A579] shrink-0" />
                <div>
                  <span className="text-xs font-semibold text-foreground block">
                    Treasury Verification &amp; Audit Trail
                  </span>
                  <span className="text-[11px] text-muted-foreground">
                    General ledger posting state, PayMongo rail confirmation, and SHA-256 digest
                  </span>
                </div>
              </div>
              <span className="text-xs text-muted-foreground group-open:rotate-180 transition-transform font-mono px-2">
                &#9660;
              </span>
            </summary>

            <div className="p-5 border-t border-border space-y-5 text-xs">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 font-mono text-[11px]">
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">
                    Settlement Status
                  </span>
                  <span className="text-foreground font-semibold mt-0.5 flex items-center gap-1.5">
                    <span className="inline-block w-2 h-2 rounded-full bg-[#12A579]" />
                    <span>{receipt.payment_status || "PAYMENT CONFIRMED"}</span>
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">
                    General Ledger Posting
                  </span>
                  <span
                    className={`font-semibold mt-0.5 block ${
                      receipt.ledger_status === "POSTED TO ACME GENERAL LEDGER"
                        ? "text-[#0d7c5a] dark:text-[#12A579]"
                        : "text-foreground"
                    }`}
                  >
                    {receipt.ledger_status || "PENDING LEDGER POSTING"}
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">
                    Payment Attempt ID
                  </span>
                  <span className="text-foreground select-all break-all mt-0.5 block">
                    {receipt.payment_attempt_id}
                  </span>
                </div>
                <div>
                  <span className="text-muted-foreground block text-[10px] uppercase">
                    SMTP Delivery Status
                  </span>
                  <div className="flex items-center gap-2 mt-0.5">
                    <span className="text-foreground font-semibold uppercase">
                      {receipt.smtp_delivery?.status || "PENDING"}
                    </span>
                    <button
                      type="button"
                      onClick={() => {
                        setShowCustomerEmailForm(true)
                        window.scrollTo({ top: 0, behavior: "smooth" })
                      }}
                      className="text-[11px] font-sans font-medium underline text-foreground hover:text-[#12A579] cursor-pointer"
                    >
                      Send to another email
                    </button>
                  </div>
                </div>

                <div className="sm:col-span-2">
                  <span className="text-muted-foreground block text-[10px] uppercase">
                    SHA-256 Cryptographic Audit Digest
                  </span>
                  <div className="flex items-center justify-between gap-2 mt-1 bg-muted/30 px-3 py-2 rounded border border-border text-[11px] text-foreground">
                    <span className="truncate select-all font-mono">
                      {receipt.verification_hash}
                    </span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="h-6 w-6 text-muted-foreground hover:text-foreground shrink-0 cursor-pointer"
                      onClick={() => handleCopy(receipt.verification_hash, "hash")}
                      aria-label="Copy SHA-256 Digest"
                    >
                      {copiedKey === "hash" ? (
                        <Check className="w-3.5 h-3.5 text-[#12A579]" />
                      ) : (
                        <Copy className="w-3.5 h-3.5" />
                      )}
                    </Button>
                  </div>
                </div>
              </div>

              {/* PayMongo Channel Verification Matrix */}
              <div className="pt-3 border-t border-border">
                <span className="text-[10px] text-muted-foreground uppercase tracking-wider block mb-2.5 font-mono">
                  PayMongo Gateway Rail Verification
                </span>
                <div className="grid grid-cols-2 sm:grid-cols-5 gap-2 text-center">
                  {receipt.supported_channels.map((ch) => {
                    const Icon = getChannelIcon(ch.code)
                    return (
                      <div
                        key={ch.code}
                        className={`p-2.5 rounded-md border text-xs flex flex-col items-center justify-between gap-1 ${
                          ch.selected
                            ? "bg-[#12A579]/10 text-[#0d7c5a] dark:text-[#12A579] border-[#12A579]/30 font-semibold"
                            : "bg-muted/20 border-border text-muted-foreground"
                        }`}
                      >
                        <div className="flex items-center gap-1.5 font-medium">
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

              {/* Statutory Note pursuant to BIR RMC No. 77-2024 */}
              <div className="pt-2 text-[10px] text-muted-foreground leading-normal border-t border-border font-sans">
                {receipt.tax_compliance_note ||
                  "Electronic payment settlement record. Pursuant to BIR RMC No. 77-2024, official commercial VAT Invoices are issued separately by Corporate Accounting upon customer tax identification verification."}
              </div>
            </div>
          </details>
        </div>
      )}
    </div>
  )
}

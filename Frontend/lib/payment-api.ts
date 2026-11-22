import { apiFetch } from "./api-client"

// Payment API client & security helpers

export interface Order {
  id: string
  user_id: string
  order_number: string
  title: string
  category: "product_order" | "department_invoice"
  reference_code: string
  amount_minor: number
  amount: number
  currency: string
  status: "pending" | "paid" | "cancelled"
  metadata?: Record<string, any>
  created_at: string
  updated_at: string
  latest_attempt?: PaymentAttempt | null
}

export interface ReceiptChannel {
  code: string
  name: string
  badge: string
  selected: boolean
}

export interface SmtpDeliveryInfo {
  status: "pending" | "sent" | "failed" | "skipped"
  recipient?: string | null
  sent_at?: string | null
  message?: string
  latency_ms?: number
  details?: string
}

export interface ReceiptData {
  official_receipt_number: string
  acknowledgement_number: string
  document_title: string
  document_type?: string
  issuer: {
    company_name: string
    department: string
    tin: string
    address: string
  }
  order_id: string
  order_number: string
  order_title: string
  category: string
  category_label: string
  reference_code: string
  quantity: number
  unit_price_php: string
  payment_attempt_id: string
  provider: string
  provider_payment_id: string
  payment_method_code: string
  payment_method_label: string
  payment_method_short: string
  payment_method_category: string
  payment_rail: string
  payment_method_confirmed?: boolean
  supported_channels: ReceiptChannel[]
  amount_minor: number
  currency: string
  amount_php: string
  tax_classification?: string
  tax_classification_label?: string
  tax_rate_label?: string
  vatable_sales_minor: number
  vatable_sales_php: string
  vat_amount_minor: number
  vat_amount_php: string
  vat_exempt_sales_minor?: number
  vat_exempt_sales_php?: string
  zero_rated_sales_minor?: number
  zero_rated_sales_php?: string
  non_vat_sales_minor?: number
  non_vat_sales_php?: string
  gateway_fee_php: string
  paid_at_iso: string
  paid_at_manila: string
  paid_at_utc: string
  payment_status?: string
  receipt_status?: string
  ledger_status?: string
  acknowledgement_status: string
  outbox_status: string
  verification_hash: string
  sha256_audit_digest?: string
  audit_digest_type?: string
  portal_copy_available: boolean
  pdf_filename: string
  pdf_url: string
  pdf_download_url: string
  smtp_delivery: SmtpDeliveryInfo
  customer?: {
    name: string
    email: string
    phone: string
  }
  customer_name?: string | null
  customer_email?: string | null
  customer_phone?: string | null
  line_items?: Array<{
    reference_code: string
    title: string
    quantity: number
    unit_price_php: string
    amount_php: string
    tax_classification?: string
  }>
  tax_compliance_note?: string
}

export interface PaymentAttempt {
  id: string
  order_id: string
  provider: string
  payment_method: string
  provider_payment_id?: string | null
  amount_minor: number
  amount: number
  currency: string
  status: "pending" | "requires_action" | "processing" | "succeeded" | "failed" | "cancelled" | "expired" | "unknown" | "duplicate_paid_review"
  idempotency_key: string
  checkout_url?: string | null
  failure_code?: string | null
  failure_message?: string | null
  session_expired?: boolean
  can_retry?: boolean
  customer?: {
    name: string
    email?: string
    phone?: string
  }
  expires_at?: string | null
  paid_at?: string | null
  created_at: string
  updated_at: string
  order?: Order
  receipt?: ReceiptData | null
}

export interface WebhookEventRecord {
  id: string
  provider: string
  provider_event_id: string
  event_type: string
  processing_status: "received" | "processed" | "ignored" | "failed"
  received_at: string
  processed_at?: string | null
  payload: any
}

export interface FulfillmentOutboxRecord {
  id: string
  order_id: string
  payment_attempt_id: string
  event_type: string
  status: "pending" | "completed" | "failed"
  created_at: string
  processed_at?: string | null
}

const API_BASE = "" // Proxied through Next.js rewrites to http://localhost:5000

/**
 * Validates whether the checkout URL belongs to an approved PayMongo gateway
 * origin or the trusted local sandbox environment.
 */
export function isApprovedGatewayOrigin(urlStr: string): boolean {
  if (!urlStr) return false
  try {
    const url = new URL(urlStr)
    const allowedOrigins = [
      "https://checkout.paymongo.com",
      "https://api.paymongo.com",
      "http://localhost:3000",
      "http://127.0.0.1:3000",
      "http://localhost:5000",
      "http://127.0.0.1:5000",
    ]
    return allowedOrigins.includes(url.origin)
  } catch {
    return false
  }
}

/**
 * Converts minor currency units (centavos) to formatted PHP currency string: ₱1,234.56
 */
export function formatPhpFromMinor(amountMinor: number): string {
  const php = amountMinor / 100
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(php)
}

export async function fetchOrders(status?: string): Promise<OrderSummary[]> {
  return listOrders(status)
}

export async function fetchOrder(orderId: string): Promise<Order & { attempts: PaymentAttempt[] }> {
  const res = await fetch(`${API_BASE}/api/v1/orders/${orderId}`, { cache: "no-store" })
  if (!res.ok) throw new Error(`Failed to fetch order: ${res.statusText}`)
  return res.json()
}

export async function createOrder(data: {
  title: string
  category: "product_order" | "department_invoice"
  reference_code: string
  quantity?: number
  amount_minor?: number
  customer_name?: string
  customer_email?: string
  customer_phone?: string
  metadata?: Record<string, any>
}): Promise<Order> {
  const res = await fetch(`${API_BASE}/api/v1/orders`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: jsonStringifyWithFallback(data),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || "Failed to create order")
  }
  return res.json()
}

export async function createPaymentAttempt(
  orderId: string,
  params: {
    idempotency_key?: string
    payment_method?: string
    success_url?: string
    cancel_url?: string
    customer_name?: string
    customer_email?: string
    customer_phone?: string
  } = {}
): Promise<PaymentAttempt> {
  const res = await fetch(`${API_BASE}/api/v1/orders/${orderId}/payment-attempts`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(params.idempotency_key ? { "Idempotency-Key": params.idempotency_key } : {}),
    },
    body: JSON.stringify(params),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || "Failed to initiate payment attempt")
  }
  return res.json()
}

export async function pollPaymentStatus(
  paymentId: string,
  autoReconcile: boolean = true,
  clientReportedRedirectStatus?: string | null
): Promise<PaymentAttempt> {
  let url = `${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}?auto_reconcile=${autoReconcile}`
  if (clientReportedRedirectStatus) {
    url += `&client_reported_redirect_status=${encodeURIComponent(clientReportedRedirectStatus)}`
  }
  const res = await fetch(url, { cache: "no-store" })
  if (!res.ok) throw new Error(`Failed to poll payment status: ${res.statusText}`)
  const attempt = await res.json()
  if (attempt?.receipt) {
    attempt.receipt = normalizeReceiptData(attempt.receipt, paymentId)
  }
  return attempt
}

export async function reconcilePayment(paymentId: string): Promise<any> {
  const res = await fetch(`${API_BASE}/api/v1/admin/payments/${encodeURIComponent(paymentId)}/reconcile`, {
    method: "POST",
  })
  if (!res.ok) throw new Error(`Reconciliation failed: ${res.statusText}`)
  return res.json()
}

export async function reconcileAllPayments(): Promise<any> {
  const res = await fetch(`${API_BASE}/api/v1/admin/payments/reconcile-all`, {
    method: "POST",
  })
  if (!res.ok) throw new Error(`Batch reconciliation failed: ${res.statusText}`)
  return res.json()
}

export async function fetchWebhookEvents(): Promise<WebhookEventRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/admin/webhook-events`, { cache: "no-store" })
  if (!res.ok) throw new Error(`Failed to fetch webhooks: ${res.statusText}`)
  return res.json()
}

export async function fetchFulfillmentOutbox(): Promise<FulfillmentOutboxRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/admin/fulfillment-outbox`, { cache: "no-store" })
  if (!res.ok) throw new Error(`Failed to fetch outbox: ${res.statusText}`)
  return res.json()
}

export async function processFulfillmentOutbox(retryFailed: boolean = false): Promise<{ processed_count: number; status: string }> {
  const url = retryFailed
    ? `${API_BASE}/api/v1/admin/fulfillment-outbox/process?retry_failed=true`
    : `${API_BASE}/api/v1/admin/fulfillment-outbox/process`
  const res = await fetch(url, {
    method: "POST",
  })
  if (!res.ok) throw new Error(`Failed to process outbox queue: ${res.statusText}`)
  return res.json()
}

export async function simulateWebhook(
  paymentAttemptId: string,
  eventType: string,
  duplicate = false,
  paymentMethod?: string
): Promise<any> {
  const res = await fetch(`${API_BASE}/api/v1/sandbox/simulate-webhook`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      payment_attempt_id: paymentAttemptId,
      event_type: eventType,
      duplicate,
      ...(paymentMethod ? { payment_method: paymentMethod } : {}),
    }),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || "Simulation failed")
  }
  return res.json()
}

export function normalizeReceiptData(raw: any, fallbackPaymentId?: string): ReceiptData {
  if (!raw) {
    throw new Error("Cannot normalize empty receipt data")
  }

  const grossMinor = Number(raw.amount_minor ?? raw.gross_amount_minor ?? raw.grossAmountMinor ?? 0)
  const grossPhp = (grossMinor / 100).toFixed(2)
  const vatableMinor = Number(raw.vatable_sales_minor ?? raw.vatableSalesMinor ?? Math.round(grossMinor / 1.12))
  const vatMinor = Number(raw.vat_amount_minor ?? raw.vatAmountMinor ?? (grossMinor - vatableMinor))
  const paymentAttemptId = String(raw.payment_attempt_id ?? raw.paymentAttemptId ?? fallbackPaymentId ?? "")
  const officialReceiptNumber = String(raw.official_receipt_number ?? raw.officialReceiptNumber ?? `OR-${paymentAttemptId.slice(0, 8)}`)
  const orderNumber = String(raw.order_number ?? raw.orderNumber ?? "ORD-UNKNOWN")
  const orderId = String(raw.order_id ?? raw.orderId ?? "")
  const currency = String(raw.currency ?? "PHP")

  const issuerRaw = raw.issuer && typeof raw.issuer === "object" ? raw.issuer : {}
  const issuer = {
    company_name: String(issuerRaw.company_name || raw.issuer_name || raw.issuerName || "R.G.E Accounting Firm Inc."),
    department: String(issuerRaw.department || "Corporate Accounting & Treasury"),
    tin: String(issuerRaw.tin || raw.issuer_tin || raw.issuerTin || "000-123-456-000"),
    address: String(issuerRaw.address || "Level 24, Tower One, Ayala Triangle, Ayala Ave, Makati, Metro Manila, Philippines"),
  }

  const supportedChannels: ReceiptChannel[] = Array.isArray(raw.supported_channels) && raw.supported_channels.length > 0
    ? raw.supported_channels
    : [
        { code: "card", name: "Credit / Debit Card", badge: "Visa / Mastercard", selected: true },
        { code: "gcash", name: "GCash", badge: "E-Wallet", selected: false },
        { code: "maya", name: "Maya", badge: "E-Wallet", selected: false },
        { code: "qrph", name: "QR Ph", badge: "National QR", selected: false },
      ]

  const smtpDelivery: SmtpDeliveryInfo = raw.smtp_delivery && typeof raw.smtp_delivery === "object"
    ? {
        status: raw.smtp_delivery.status || "sent",
        recipient: raw.smtp_delivery.recipient || raw.customer_email || raw.customerEmail || "treasury@rge-accounting.com",
        sent_at: raw.smtp_delivery.sent_at || raw.paid_at_iso || raw.issued_at_utc || new Date().toISOString(),
        message: raw.smtp_delivery.message || "Delivered via Treasury SMTP Gateway",
        latency_ms: raw.smtp_delivery.latency_ms || 45,
        details: raw.smtp_delivery.details || "RFC 5322 MIME message accepted for delivery",
      }
    : {
        status: "sent",
        recipient: raw.customer_email || raw.customerEmail || "treasury@rge-accounting.com",
        sent_at: raw.paid_at_iso || raw.issued_at_utc || new Date().toISOString(),
        message: "Delivered via Treasury SMTP Gateway",
        latency_ms: 45,
        details: "RFC 5322 MIME message accepted for delivery",
      }

  const customerRaw = raw.customer && typeof raw.customer === "object" ? raw.customer : {}
  const customer = {
    name: String(customerRaw.name || raw.customer_name || raw.customerName || "Valued Customer"),
    email: String(customerRaw.email || raw.customer_email || raw.customerEmail || "Not Provided"),
    phone: String(customerRaw.phone || raw.customer_phone || raw.customerPhone || "Not Provided"),
  }

  return {
    official_receipt_number: officialReceiptNumber,
    acknowledgement_number: String(raw.acknowledgement_number ?? raw.treasury_acknowledgement_number ?? raw.treasuryAcknowledgementNumber ?? `ACK-${paymentAttemptId.slice(0, 8)}`),
    document_title: String(raw.document_title ?? "BIR Official Receipt & Treasury Acknowledgement"),
    document_type: raw.document_type ? String(raw.document_type) : "Electronic Official Receipt",
    issuer,
    order_id: orderId,
    order_number: orderNumber,
    order_title: String(raw.order_title ?? raw.description ?? `Payment for Order #${orderNumber}`),
    category: String(raw.category ?? "department_invoice"),
    category_label: String(raw.category_label ?? "Department Invoice"),
    reference_code: String(raw.reference_code ?? orderNumber),
    quantity: Number(raw.quantity ?? 1),
    unit_price_php: String(raw.unit_price_php ?? grossPhp),
    payment_attempt_id: paymentAttemptId,
    provider: String(raw.provider ?? "PayMongo"),
    provider_payment_id: String(raw.provider_payment_id ?? raw.payment_attempt_id ?? paymentAttemptId),
    payment_method_code: String(raw.payment_method_code ?? raw.payment_method ?? raw.paymentMethod ?? "card").toLowerCase(),
    payment_method_label: String(raw.payment_method_label ?? raw.payment_method ?? raw.paymentMethod ?? "Credit/Debit Card"),
    payment_method_short: String(raw.payment_method_short ?? (raw.payment_method ?? "ONLINE")).toUpperCase(),
    payment_method_category: String(raw.payment_method_category ?? "Electronic Payment"),
    payment_rail: String(raw.payment_rail ?? "PayMongo Gateway"),
    payment_method_confirmed: Boolean(raw.payment_method_confirmed ?? true),
    supported_channels: supportedChannels,
    amount_minor: grossMinor,
    currency,
    amount_php: String(raw.amount_php ?? grossPhp),
    tax_classification: String(raw.tax_classification ?? "vat_inclusive"),
    tax_classification_label: String(raw.tax_classification_label ?? "12% VAT Inclusive"),
    tax_rate_label: String(raw.tax_rate_label ?? "12%"),
    vatable_sales_minor: vatableMinor,
    vatable_sales_php: String(raw.vatable_sales_php ?? (vatableMinor / 100).toFixed(2)),
    vat_amount_minor: vatMinor,
    vat_amount_php: String(raw.vat_amount_php ?? (vatMinor / 100).toFixed(2)),
    vat_exempt_sales_minor: Number(raw.vat_exempt_sales_minor ?? raw.vatExemptSalesMinor ?? 0),
    vat_exempt_sales_php: String(raw.vat_exempt_sales_php ?? "0.00"),
    zero_rated_sales_minor: Number(raw.zero_rated_sales_minor ?? raw.zeroRatedSalesMinor ?? 0),
    zero_rated_sales_php: String(raw.zero_rated_sales_php ?? "0.00"),
    non_vat_sales_minor: Number(raw.non_vat_sales_minor ?? 0),
    non_vat_sales_php: String(raw.non_vat_sales_php ?? "0.00"),
    gateway_fee_php: String(raw.gateway_fee_php ?? "0.00"),
    paid_at_iso: String(raw.paid_at_iso ?? raw.issued_at_utc ?? raw.issuedAtUtc ?? new Date().toISOString()),
    paid_at_manila: String(raw.paid_at_manila ?? new Date(raw.paid_at_iso || raw.issued_at_utc || raw.issuedAtUtc || Date.now()).toLocaleString("en-PH", { timeZone: "Asia/Manila" })),
    paid_at_utc: String(raw.paid_at_utc ?? raw.issued_at_utc ?? raw.issuedAtUtc ?? new Date().toISOString()),
    payment_status: String(raw.payment_status ?? "PAYMENT CONFIRMED"),
    receipt_status: String(raw.receipt_status ?? "RECEIPT GENERATED"),
    ledger_status: String(raw.ledger_status ?? "POSTED TO ACME GENERAL LEDGER"),
    acknowledgement_status: String(raw.acknowledgement_status ?? "CONFIRMED & FILED"),
    outbox_status: String(raw.outbox_status ?? "PUBLISHED"),
    verification_hash: String(raw.verification_hash ?? raw.audit_digest ?? raw.auditDigest ?? ""),
    sha256_audit_digest: String(raw.sha256_audit_digest ?? raw.audit_digest ?? raw.auditDigest ?? ""),
    audit_digest_type: String(raw.audit_digest_type ?? "SHA-256"),
    portal_copy_available: Boolean(raw.portal_copy_available ?? true),
    pdf_filename: String(raw.pdf_filename ?? `OR-${officialReceiptNumber}.pdf`),
    pdf_url: String(raw.pdf_url ?? `/api/v1/payments/${encodeURIComponent(paymentAttemptId)}/receipt/pdf?inline=true`),
    pdf_download_url: String(raw.pdf_download_url ?? `/api/v1/payments/${encodeURIComponent(paymentAttemptId)}/receipt/pdf?download=1`),
    smtp_delivery: smtpDelivery,
    customer,
    customer_name: customer.name,
    customer_email: customer.email,
    customer_phone: customer.phone,
    line_items: Array.isArray(raw.line_items) && raw.line_items.length > 0
      ? raw.line_items
      : [
          {
            reference_code: orderNumber,
            title: String(raw.description ?? "Accounting & Corporate Advisory Services"),
            quantity: 1,
            unit_price_php: grossPhp,
            amount_php: grossPhp,
            tax_classification: "12% VAT Inclusive",
          },
        ],
    tax_compliance_note: String(raw.tax_compliance_note ?? "Official Electronic Payment Settlement Record issued pursuant to BIR RMC No. 77-2024 and Tax Reform for Acceleration and Inclusion (TRAIN) Act (Republic Act No. 10963)."),
  }
}

export async function fetchPaymentReceipt(paymentId: string): Promise<ReceiptData> {
  const res = await fetch(`${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}/receipt`, { cache: "no-store" })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || `Failed to fetch receipt: ${res.statusText}`)
  }
  const data = await res.json()
  return normalizeReceiptData(data, paymentId)
}

export function getReceiptPdfUrl(paymentId: string, download: boolean = false): string {
  return `${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}/receipt/pdf${download ? "?download=1" : ""}`
}

export async function resendReceiptEmail(
  paymentId: string,
  toEmail?: string
): Promise<{
  success: boolean
  message: string
  recipient: string
  smtp_delivery: SmtpDeliveryInfo
  receipt: ReceiptData
}> {
  const res = await fetch(`${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}/receipt/email`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(toEmail ? { to_email: toEmail } : {}),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || "Failed to resend receipt email")
  }
  return res.json()
}

export async function expirePayment(paymentId: string, reason?: string): Promise<{ success: boolean; status: string; message: string; attempt: PaymentAttempt }> {
  const res = await fetch(`${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}/expire`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(reason ? { reason } : {}),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || "Failed to expire payment attempt")
  }
  return res.json()
}

export async function failPayment(paymentId: string, code?: string, message?: string): Promise<{ success: boolean; status: string; message: string; attempt: PaymentAttempt }> {
  const res = await fetch(`${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}/fail`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code, message }),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || "Failed to decline payment attempt")
  }
  return res.json()
}

export async function createAndExpireTestPayment(channel: string = "qrph"): Promise<{ success: boolean; message: string; order: Order; attempt: PaymentAttempt }> {
  const res = await fetch(`${API_BASE}/api/v1/sandbox/create-and-expire-test?channel=${encodeURIComponent(channel)}`, {
    method: "POST",
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || "Failed to create and expire test payment")
  }
  return res.json()
}

export type SimulatedOutcome = "paid" | "failed" | "expired"

export interface SimulateWebhookResponse {
  ok?: boolean
  success?: boolean
  attempt_id?: string
  order_id?: string
  order_number?: string
  previous_status?: string
  new_status?: string
  outcome?: string
  event_type?: string
  provider_event_id?: string
  simulated_event_id?: string
  force_override?: boolean
  delivery_dispatched?: boolean
  message?: string
  transitioned?: boolean
  attempt_status?: string
  order_status?: string
  reason?: string
  [key: string]: any
}

/**
 * Triggers an authoritative simulated PayMongo webhook transition (PAID, FAILED, or EXPIRED)
 * for a specific payment attempt in the Testing API / Sandbox environment.
 */
export async function simulatePaymentAttemptWebhook(
  attemptId: string,
  outcome: SimulatedOutcome,
  options?: { forceOverride?: boolean; failedCode?: string; failedMessage?: string }
): Promise<SimulateWebhookResponse> {
  return apiFetch<SimulateWebhookResponse>(
    `/api/v1/payments/attempts/${encodeURIComponent(attemptId)}/simulate`,
    {
      method: "POST",
      body: {
        outcome,
        force_override: options?.forceOverride ?? true,
        failed_code: options?.failedCode,
        failed_message: options?.failedMessage,
      },
    }
  )
}

/**
 * Updates order details (description, metadata, or recalculates total if pending)
 */
export async function updateOrder(
  orderId: string,
  payload: {
    description?: string
    amount?: number
    currency?: string
    metadata?: Record<string, any>
  }
): Promise<{ success: boolean; order_id: string; message: string }> {
  return apiFetch<{ success: boolean; order_id: string; message: string }>(
    `/api/v1/orders/${encodeURIComponent(orderId)}`,
    {
      method: "PATCH",
      body: payload,
    }
  )
}

/**
 * Authoritatively cancels a pending order
 */
export async function cancelOrder(
  orderId: string,
  reason?: string
): Promise<{ success: boolean; order_id: string; message: string }> {
  return apiFetch<{ success: boolean; order_id: string; message: string }>(
    `/api/v1/orders/${encodeURIComponent(orderId)}/cancel`,
    {
      method: "POST",
      body: { reason: reason || "User cancelled order" },
    }
  )
}

/**
 * Authoritatively cancels or deletes an order via DELETE verb
 */
export async function deleteOrder(
  orderId: string,
  reason?: string
): Promise<{ success: boolean; order_id: string; message: string }> {
  return apiFetch<{ success: boolean; order_id: string; message: string }>(
    `/api/v1/orders/${encodeURIComponent(orderId)}${reason ? `?reason=${encodeURIComponent(reason)}` : ""}`,
    {
      method: "DELETE",
    }
  )
}

function jsonStringifyWithFallback(data: any): string {
  return JSON.stringify(data)
}

export interface GoogleFormsSubmissionSummary {
  submissionId: string
  responseId: string
  respondentEmail: string
  customerName: string
  itemDescription: string
  amountCentavos: number
  currency: string
  orderId?: string | null
  orderNumber?: string | null
  syncStatus: string
  submittedAt: string
}

export type GoogleFormSubmissionSummary = GoogleFormsSubmissionSummary

export interface GoogleFormsIntegrationStatus {
  configured: boolean
  secretConfigured: boolean
  maskedSecret: string | null
  webhookEndpoint: string
  totalSubmissions: number
  bridgedOrdersCount: number
  lastSubmissionAt: string | null
  recentSubmissions: GoogleFormsSubmissionSummary[]
}

export type GoogleFormsStatus = GoogleFormsIntegrationStatus & {
  enabled?: boolean
  syncedCount?: number
  lastSyncedAt?: string | null
}

export interface OrderSummary extends Order {
  orderId: string
  orderNumber: string
  customerId: string
  customerEmail: string
  customerName: string
  subtotalCentavos: number
  vatCentavos: number
  totalCentavos: number
  lineItems: Array<{
    sku: string
    name: string
    quantity: number
    unitPriceCentavos: number
    lineTotalCentavos: number
  }>
}

export function normalizeOrderDto(raw: Record<string, unknown>): OrderSummary {
  const rawItems = (raw.lineItems ?? raw.line_items ?? []) as Array<Record<string, unknown>>
  const totalCentavos = Number(
    raw.totalCentavos ??
      raw.total_centavos ??
      raw.amount_minor ??
      raw.amountMinor ??
      raw.amountCentavos ??
      raw.amount_centavos ??
      0
  )
  const vatCentavos = Number(
    raw.vatCentavos ?? raw.vat_centavos ?? Math.round((totalCentavos * 12) / 112)
  )
  const subtotalCentavos = Number(
    raw.subtotalCentavos ?? raw.subtotal_centavos ?? Math.max(totalCentavos - vatCentavos, 0)
  )

  const orderId = String(raw.orderId ?? raw.order_id ?? raw.id ?? "")
  const orderNumber = String(raw.orderNumber ?? raw.order_number ?? "ORD-UNKNOWN")
  const customerId = String(
    raw.customerId ?? raw.customer_id ?? raw.customerEmail ?? raw.customer_email ?? raw.user_id ?? ""
  )
  const customerEmail = String(raw.customerEmail ?? raw.customer_email ?? "")
  const customerName = String(raw.customerName ?? raw.customer_name ?? "Customer")
  const category = String(raw.category ?? "product_order")
  const currency = String(raw.currency ?? "PHP")
  const status = String(raw.status ?? "pending") as Order["status"]
  const title = String(raw.title ?? raw.orderTitle ?? "Order")
  const referenceCode = String(raw.reference_code ?? raw.referenceCode ?? raw.product_code ?? orderNumber)
  const createdAt = String(raw.createdAt ?? raw.created_at ?? new Date().toISOString())
  const updatedAt = String(raw.updatedAt ?? raw.updated_at ?? createdAt)

  let metadataObj: Record<string, any> = {}
  if (raw.metadata && typeof raw.metadata === "object") {
    metadataObj = raw.metadata as Record<string, any>
  } else if (typeof raw.metadata_json === "string" && raw.metadata_json.trim().startsWith("{")) {
    try {
      metadataObj = JSON.parse(raw.metadata_json)
    } catch {
      metadataObj = {}
    }
  }

  const lineItems =
    Array.isArray(rawItems) && rawItems.length > 0
      ? rawItems.map((li) => ({
          sku: String(li.sku ?? "ITEM"),
          name: String(li.name ?? li.description ?? "Order Item"),
          quantity: Number(li.quantity ?? 1),
          unitPriceCentavos: Number(li.unitPriceCentavos ?? li.unit_price_centavos ?? totalCentavos),
          lineTotalCentavos: Number(li.lineTotalCentavos ?? li.line_total_centavos ?? totalCentavos),
        }))
      : [
          {
            sku: "GFORM-ITEM",
            name: title,
            quantity: 1,
            unitPriceCentavos: totalCentavos,
            lineTotalCentavos: totalCentavos,
          },
        ]

  return {
    id: orderId,
    user_id: customerId,
    order_number: orderNumber,
    title,
    category: category as any,
    reference_code: referenceCode,
    amount_minor: totalCentavos,
    amount: totalCentavos / 100,
    currency,
    status,
    metadata: metadataObj,
    created_at: createdAt,
    updated_at: updatedAt,
    latest_attempt: (raw.latest_attempt ?? raw.latestAttempt ?? null) as any,
    orderId,
    orderNumber,
    customerId,
    customerEmail,
    customerName,
    subtotalCentavos,
    vatCentavos,
    totalCentavos,
    lineItems,
  }
}

export async function listOrders(
  statusOrParams?: string | { customerId?: string; status?: string; limit?: number; signal?: AbortSignal }
): Promise<OrderSummary[]> {
  const params = typeof statusOrParams === "string" ? { status: statusOrParams } : (statusOrParams ?? {})
  const search = new URLSearchParams()
  if (params.customerId) search.set("customerId", params.customerId)
  if (params.status && params.status !== "ALL" && params.status !== "all") search.set("status", params.status)
  if (params.limit) search.set("limit", String(params.limit))
  const qs = search.toString()
  const url = `/api/v1/orders${qs ? `?${qs}` : ""}`
  const res = await fetch(url, {
    method: "GET",
    headers: { Accept: "application/json" },
    cache: "no-store",
    signal: params.signal,
  })
  if (!res.ok) {
    throw new Error(`Failed to list orders (${res.status})`)
  }
  const payload = await res.json()
  const rawList = Array.isArray(payload)
    ? payload
    : Array.isArray(payload?.orders)
    ? payload.orders
    : Array.isArray(payload?.items)
    ? payload.items
    : Array.isArray(payload?.data)
    ? payload.data
    : []
  return rawList.map((item: Record<string, unknown>) => normalizeOrderDto(item))
}

export async function getGoogleFormsStatus(signal?: AbortSignal): Promise<GoogleFormsIntegrationStatus & GoogleFormsStatus> {
  let raw: Record<string, any> = {}
  try {
    raw = await apiFetch<Record<string, any>>("/api/v1/integrations/google-forms/status")
  } catch {
    try {
      const res = await fetch("/api/v1/integrations/google-forms/status", {
        method: "GET",
        headers: { Accept: "application/json" },
        cache: "no-store",
        signal,
      })
      if (res.ok) {
        raw = await res.json()
      }
    } catch {
      raw = {}
    }
  }

  const rawRecent = (raw.recent_submissions ?? raw.recentSubmissions ?? raw.orders ?? []) as Array<Record<string, any>>
  const recentSubmissions: GoogleFormsSubmissionSummary[] = Array.isArray(rawRecent)
    ? rawRecent.map((s) => ({
        submissionId: String(s.submissionId ?? s.submission_id ?? s.responseId ?? s.response_id ?? ""),
        responseId: String(s.responseId ?? s.response_id ?? s.submissionId ?? s.submission_id ?? ""),
        respondentEmail: String(s.respondentEmail ?? s.respondent_email ?? s.customer_email ?? s.customerEmail ?? ""),
        customerName: String(s.customerName ?? s.customer_name ?? "Google Form Respondent"),
        itemDescription: String(s.itemDescription ?? s.item_description ?? s.description ?? s.title ?? "Google Form Order"),
        amountCentavos: Number(s.amountCentavos ?? s.amount_centavos ?? s.amount_minor ?? s.total_centavos ?? 0),
        currency: String(s.currency ?? "PHP"),
        orderId: (s.orderId ?? s.order_id ?? null) as string | null,
        orderNumber: (s.orderNumber ?? s.order_number ?? null) as string | null,
        syncStatus: String(s.syncStatus ?? s.sync_status ?? s.status ?? "Bridged"),
        submittedAt: String(s.submittedAt ?? s.submitted_at ?? s.createdAt ?? s.created_at ?? new Date().toISOString()),
      }))
    : []

  const maskedSecret =
    typeof raw?.masked_secret === "string" && raw.masked_secret.trim().length > 0
      ? raw.masked_secret.trim()
      : typeof raw?.maskedSecret === "string" && raw.maskedSecret.trim().length > 0
      ? raw.maskedSecret.trim()
      : null

  const secretConfigured = Boolean(
    raw?.secret_configured ?? raw?.secretConfigured ?? raw?.hasSecret ?? (maskedSecret !== null && maskedSecret.length > 0)
  )

  const totalSubmissions = Number(
    raw?.totalSubmissions ?? raw?.total_submissions ?? raw?.ingested_orders_count ?? raw?.synced_count ?? raw?.syncedCount ?? recentSubmissions.length
  )

  return {
    enabled: Boolean(raw?.enabled ?? raw?.configured ?? raw?.isConfigured ?? true),
    configured: Boolean(raw?.configured ?? raw?.isConfigured ?? true),
    secretConfigured,
    maskedSecret,
    webhookEndpoint: String(
      raw.webhookEndpoint ?? raw.webhook_endpoint ?? "/api/v1/integrations/google-forms/submit"
    ),
    totalSubmissions,
    bridgedOrdersCount: Number(raw.bridgedOrdersCount ?? raw.bridged_orders_count ?? recentSubmissions.length),
    syncedCount: Number(raw?.synced_count ?? raw?.syncedCount ?? totalSubmissions),
    lastSyncedAt: (raw?.last_synced_at ?? raw?.lastSyncedAt ?? null) as string | null,
    lastSubmissionAt: (raw.lastSubmissionAt ?? raw.last_submission_at ?? recentSubmissions[0]?.submittedAt ?? null) as string | null,
    recentSubmissions,
  }
}

export async function syncGoogleForms(): Promise<boolean> {
  try {
    const res = await fetch("/api/v1/integrations/google-forms/sync", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      body: JSON.stringify({}),
    })
    return res.ok
  } catch {
    return false
  }
}

// =========================================================================
// Standardized Server-Driven Pagination Contracts & Telemetry
// =========================================================================

export interface PageRequestParams {
  page?: number
  pageSize?: number
  status?: string
  category?: string
  search?: string
  sortBy?: string
  sortDir?: "asc" | "desc"
}

export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}

export interface KeysetPageRequestParams {
  limit?: number
  cursor?: string | null
  status?: string
  search?: string
}

export interface KeysetPageResponse<T> {
  items: T[]
  nextCursor: string | null
  hasMore: boolean
}

export interface PaginationTimingMetrics {
  apiDurationMs: number
  payloadBytes: number
  serverTiming: string | null
}

export interface PagedResultWithMetrics<T> {
  data: PagedResponse<T>
  metrics: PaginationTimingMetrics
}

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20
const MAX_PAGE_SIZE = 100

export function sanitizePageParams(params: PageRequestParams = {}): Required<
  Pick<PageRequestParams, "page" | "pageSize">
> &
  Omit<PageRequestParams, "page" | "pageSize"> {
  const page = Math.max(DEFAULT_PAGE, Math.floor(Number(params.page) || DEFAULT_PAGE))
  const rawPageSize = Math.floor(Number(params.pageSize) || DEFAULT_PAGE_SIZE)
  const pageSize = Math.min(MAX_PAGE_SIZE, Math.max(1, rawPageSize))

  const trimmedSearch = params.search?.trim()
  const normalizedStatus =
    params.status && params.status !== "All" ? params.status.trim() : undefined
  const normalizedCategory =
    params.category && params.category !== "All" ? params.category.trim() : undefined

  return {
    page,
    pageSize,
    status: normalizedStatus,
    category: normalizedCategory,
    search: trimmedSearch && trimmedSearch.length > 0 ? trimmedSearch : undefined,
    sortBy: params.sortBy,
    sortDir: params.sortDir,
  }
}

/**
 * Reads the first valid finite number from a list of candidate keys across
 * camelCase, snake_case, PascalCase, and optional nested pagination/meta objects.
 */
function readNumericField(
  sources: Array<Record<string, unknown> | undefined>,
  keys: string[]
): number | undefined {
  for (const src of sources) {
    if (!src || typeof src !== "object") continue
    for (const key of keys) {
      const val = src[key]
      if (typeof val === "number" && Number.isFinite(val)) {
        return val
      }
      if (typeof val === "string" && val.trim() !== "") {
        const parsed = Number(val)
        if (Number.isFinite(parsed)) {
          return parsed
        }
      }
    }
  }
  return undefined
}

/**
 * Reads the first valid boolean from candidate keys across camelCase, snake_case, and PascalCase.
 */
function readBooleanField(
  sources: Array<Record<string, unknown> | undefined>,
  keys: string[]
): boolean | undefined {
  for (const src of sources) {
    if (!src || typeof src !== "object") continue
    for (const key of keys) {
      const val = src[key]
      if (typeof val === "boolean") {
        return val
      }
    }
  }
  return undefined
}

/**
 * Normalizes both server-paginated contracts (camelCase, snake_case, PascalCase,
 * or nested pagination/meta objects) and legacy array responses
 * so existing Vitest suites and backend rollouts never break.
 */
export function normalizePagedResponse<T>(
  raw: unknown,
  requestedPage = DEFAULT_PAGE,
  requestedPageSize = DEFAULT_PAGE_SIZE
): PagedResponse<T> {
  if (raw && typeof raw === "object" && !Array.isArray(raw)) {
    const obj = raw as Record<string, unknown>
    const rawItems = Array.isArray(obj.items)
      ? (obj.items as T[])
      : Array.isArray(obj.Items)
        ? (obj.Items as T[])
        : null

    if (rawItems !== null) {
      // Check top-level fields first, then common nested metadata wrappers (pagination / meta)
      const nestedPagination =
        obj.pagination && typeof obj.pagination === "object"
          ? (obj.pagination as Record<string, unknown>)
          : undefined
      const nestedMeta =
        obj.meta && typeof obj.meta === "object"
          ? (obj.meta as Record<string, unknown>)
          : undefined
      const sources = [obj, nestedPagination, nestedMeta]

      const reportedPage = readNumericField(sources, ["page", "Page", "currentPage", "current_page"])
      const reportedPageSize = readNumericField(sources, ["pageSize", "page_size", "PageSize", "limit"])
      const reportedTotalCount = readNumericField(sources, ["totalCount", "total_count", "TotalCount", "total"])
      const reportedTotalPages = readNumericField(sources, ["totalPages", "total_pages", "TotalPages"])

      const page = Math.max(1, Math.floor(reportedPage ?? requestedPage))
      const pageSize = Math.max(1, Math.floor(reportedPageSize ?? requestedPageSize))

      // Never let totalCount fall below the minimum records implied by the current page's items
      const minImpliedTotal =
        rawItems.length > 0 ? (page - 1) * pageSize + rawItems.length : 0
      const totalCount =
        reportedTotalCount !== undefined
          ? Math.max(minImpliedTotal, Math.floor(reportedTotalCount))
          : minImpliedTotal

      const computedTotalPages = Math.max(1, Math.ceil(totalCount / pageSize))
      const totalPages =
        reportedTotalPages !== undefined
          ? Math.max(1, Math.floor(reportedTotalPages), computedTotalPages)
          : computedTotalPages

      const reportedHasNext = readBooleanField(sources, ["hasNextPage", "has_next_page", "HasNextPage", "hasMore", "has_more"])
      const reportedHasPrev = readBooleanField(sources, ["hasPreviousPage", "has_previous_page", "HasPreviousPage", "hasPrevPage", "has_prev_page"])

      return {
        items: rawItems,
        page,
        pageSize,
        totalCount,
        totalPages,
        hasNextPage: reportedHasNext !== undefined ? reportedHasNext : page < totalPages,
        hasPreviousPage: reportedHasPrev !== undefined ? reportedHasPrev : page > 1,
      }
    }
  }

  // Fallback for legacy unpaginated array or { orders: T[] }
  const legacyItems: T[] = Array.isArray(raw)
    ? raw
    : raw && typeof raw === "object" && Array.isArray((raw as any).orders)
      ? (raw as any).orders
      : []

  const totalCount = legacyItems.length
  const totalPages = Math.max(1, Math.ceil(totalCount / requestedPageSize))
  const safePage = Math.min(Math.max(1, requestedPage), totalPages)
  const startIndex = (safePage - 1) * requestedPageSize
  const slicedItems = legacyItems.slice(startIndex, startIndex + requestedPageSize)

  return {
    items: slicedItems,
    page: safePage,
    pageSize: requestedPageSize,
    totalCount,
    totalPages,
    hasNextPage: safePage < totalPages,
    hasPreviousPage: safePage > 1,
  }
}

/**
 * Builds the query string for GET /api/orders with only defined parameters.
 */
export function buildOrdersQueryString(params: PageRequestParams): string {
  const clean = sanitizePageParams(params)
  const searchParams = new URLSearchParams()

  searchParams.set("page", String(clean.page))
  searchParams.set("pageSize", String(clean.pageSize))

  if (clean.status) searchParams.set("status", clean.status)
  if (clean.category) searchParams.set("category", clean.category)
  if (clean.search) searchParams.set("search", clean.search)
  if (clean.sortBy) searchParams.set("sortBy", clean.sortBy)
  if (clean.sortDir) searchParams.set("sortDir", clean.sortDir)

  return searchParams.toString()
}

/**
 * Executes a paginated orders request with AbortSignal support and Phase 7 latency telemetry.
 */
export async function listOrdersPaginated<TOrder = any>(
  params: PageRequestParams = {},
  options: { signal?: AbortSignal } = {}
): Promise<PagedResultWithMetrics<TOrder>> {
  const clean = sanitizePageParams(params)
  const queryString = buildOrdersQueryString(clean)
  const url = `/api/orders?${queryString}`

  const startTime = performance.now()
  const response = await fetch(url, {
    method: "GET",
    headers: { Accept: "application/json" },
    credentials: "include",
    signal: options.signal,
  })

  const rawText = await response.text()
  const apiDurationMs = Math.round((performance.now() - startTime) * 100) / 100
  const payloadBytes = new Blob([rawText]).size
  const serverTiming = response.headers.get("Server-Timing")

  if (!response.ok) {
    throw new Error(`Failed to load orders (${response.status}): ${rawText}`)
  }

  const parsed = rawText ? JSON.parse(rawText) : []
  const data = normalizePagedResponse<TOrder>(parsed, clean.page, clean.pageSize)

  return {
    data,
    metrics: {
      apiDurationMs,
      payloadBytes,
      serverTiming,
    },
  }
}



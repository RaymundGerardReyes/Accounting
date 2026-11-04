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

export async function fetchOrders(status?: string): Promise<Order[]> {
  const url = status ? `${API_BASE}/api/v1/orders?status=${status}` : `${API_BASE}/api/v1/orders`
  const res = await fetch(url, { cache: "no-store" })
  if (!res.ok) throw new Error(`Failed to fetch orders: ${res.statusText}`)
  return res.json()
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

export async function pollPaymentStatus(paymentId: string, autoReconcile: boolean = true): Promise<PaymentAttempt> {
  const url = `${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}?auto_reconcile=${autoReconcile}`
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



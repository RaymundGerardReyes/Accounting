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
  return res.json()
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

export async function fetchPaymentReceipt(paymentId: string): Promise<ReceiptData> {
  const res = await fetch(`${API_BASE}/api/v1/payments/${encodeURIComponent(paymentId)}/receipt`, { cache: "no-store" })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.detail || `Failed to fetch receipt: ${res.statusText}`)
  }
  return res.json()
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
  ok: boolean
  provider_event_id: string
  transitioned: boolean
  event_type?: string
  attempt_id?: string
  attempt_status?: string
  order_id?: string
  order_status?: string
  reason?: string
  [key: string]: any
}

/**
 * Triggers a simulated webhook transition (PAID, FAILED, or EXPIRED)
 * for a specific payment attempt in the Testing API.
 */
export async function simulatePaymentAttemptWebhook(
  attemptId: string,
  outcome: SimulatedOutcome,
  options?: { forceOverride?: boolean; failedCode?: string; failedMessage?: string }
): Promise<SimulateWebhookResponse> {
  return apiFetch<SimulateWebhookResponse>(
    `/v1/payments/attempts/${encodeURIComponent(attemptId)}/simulate`,
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

function jsonStringifyWithFallback(data: any): string {
  return JSON.stringify(data)
}


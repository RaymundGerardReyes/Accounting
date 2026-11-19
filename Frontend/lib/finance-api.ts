// CPA-Audited Finance Operations API Client & Helpers

export interface FinanceUser {
  id: string
  employee_code?: string
  full_name: string
  email: string
  department_id?: string
  cost_center_id?: string
  staff_type?: "field" | "non_field" | string
  employee_group?: string
  status?: string
  employment_status?: string
  base_location?: string
  is_active?: boolean
  roles?: string[]
}

export interface PolicyRuleRecord {
  id: string
  policy_version_id: string
  rule_code: string
  rule_name: string
  staff_type_scope: string
  expense_category_scope: string
  location_scope: string
  NumericLimitMinor: number | null
  numeric_limit_minor: number | null
  frequency_limit: number | null
  frequency_window: string | null
  requires_pdf_evidence: boolean
  requires_mfar: boolean
  requires_exception_on_breach: boolean
  enforcement_mode: "hard_block" | "soft_warn" | "exception_eligible"
  rule_config_json: Record<string, any>
}

export interface PolicyVersionRecord {
  id: string
  policy_id: string
  version_number: number
  status: "draft" | "pending_approval" | "active" | "retired" | string
  effective_from: string
  effective_to?: string | null
  effective_until?: string | null
  change_reason?: string
  change_justification?: string
  created_by_user_id?: string
  author_id?: string
  approved_by_user_id?: string | null
  approver_id?: string | null
  published_by_user_id?: string | null
  aggregate_monthly_limit_minor?: number
  numeric_limit_minor?: number
  max_reports_per_period?: number
  rules?: PolicyRuleRecord[]
}

export interface FinancialPolicyRecord {
  id: string
  policy_code: string
  policy_name?: string
  name?: string
  scope_type?: string
  scope_workflow?: string
  is_active?: boolean
  versions: PolicyVersionRecord[]
}

export interface ExceptionRequestRecord {
  id: string
  exception_number: string
  requester_user_id?: string
  employee_id?: string
  employee_user_id?: string
  policy_version_id?: string
  rule_code?: string
  violated_rule_code?: string
  target_entity_type?: string
  target_entity_id?: string | null
  target_period_key?: string
  reporting_period?: string
  requested_amount_minor: number
  requested_amount?: number
  approved_amount_minor?: number | null
  approved_amount?: number | null
  reason_code?: string
  justification?: string
  business_justification?: string
  supporting_doc_ref_id?: string | null
  status: "draft" | "submitted" | "under_review" | "approved" | "rejected" | "expired" | "consumed" | string
  reviewed_by_user_id?: string | null
  review_notes?: string | null
  valid_until?: string | null
  consumed_by_entity_type?: string | null
  consumed_by_entity_id?: string | null
  consumed_at?: string | null
  created_at?: string
}

export interface AdvanceReconciliation {
  released_amount_minor: number
  released_amount?: number
  approved_expenses_applied_minor: number
  approved_expenses_applied?: number
  cash_returned_minor: number
  cash_returned?: number
  reimbursement_paid_minor: number
  reimbursement_paid?: number
  rejected_shortfall_minor?: number
  rejected_expenses_minor?: number
  outstanding_balance_minor: number
  outstanding_balance?: number
  excess_reimbursable_minor?: number
  excess_reimbursable?: number
}

export interface DisbursementRecord {
  id: string
  disbursement_number: string
  source_entity_type?: string
  source_entity_id?: string
  advance_id?: string
  payee_type?: string
  payee_id?: string
  payee_name?: string
  payment_method: string
  payment_amount_minor: number
  amount_minor?: number
  amount?: number
  currency?: string
  recipient_account_ref?: string | null
  status: string
  maker_id?: string
  maker_user_id?: string
  verifier_id?: string | null
  verifier_user_id?: string | null
  bank_approver_id?: string | null
  bank_approver_user_id?: string | null
  executed_by_id?: string | null
  executed_by_user_id?: string | null
  evidence_verifier_id?: string | null
  evidence_verified_by_user_id?: string | null
  bank_reference?: string | null
  bank_reference_number?: string | null
  value_date?: string | null
  evidence_document_id?: string | null
  proof_document_ref_id?: string | null
  failure_reason?: string | null
  is_settled_immediately_by_bank?: boolean
  created_at?: string
  created_at_utc?: string
  executed_at?: string | null
}

export interface AdvanceLedgerEntryRecord {
  id: string
  entry_type: string
  amount_minor: number
  signed_amount_minor: number
  currency_code: string
  created_by_actor_id: string
  occurred_at_utc: string
}

export interface PayablePaymentRecord {
  id: string
  payment_number: string
  payable_id: string
  purchase_request_id: string
  vendor_id: string
  payment_amount_minor: number
  executed_amount_minor?: number | null
  currency: string
  payment_method: string
  status: string
  maker_id: string
  prepared_at_utc?: string
  verifier_id?: string | null
  verified_at_utc?: string | null
  bank_approver_id?: string | null
  bank_approved_at_utc?: string | null
  executed_by_id?: string | null
  execution_date?: string | null
  executed_at_utc?: string | null
  bank_reference?: string | null
  remittance_doc_id?: string | null
  evidence_verifier_id?: string | null
  evidence_verified_at_utc?: string | null
  reconciliation_status?: string
  reconciliation_notes?: string | null
  failure_reason?: string | null
  created_at_utc?: string
}

export interface VendorBankChangeRecord {
  id: string
  vendor_id: string
  previous_bank_ref?: string | null
  new_bank_name: string
  masked_account_number: string
  new_bank_account_ref: string
  reason: string
  status: string
  requested_by_id: string
  approved_by_id?: string | null
  requested_at_utc?: string
}

export interface CashAdvanceRecord {
  id: string
  advance_number: string
  employee_user_id: string
  department_id: string
  cost_center_id: string
  purpose: string
  project_trip_ref: string
  period_key: string
  requested_amount_minor: number
  requested_amount: number
  approved_amount_minor: number | null
  approved_amount: number | null
  currency: string
  needed_by_date: string
  liquidation_due_date: string
  policy_version_id: string
  exception_request_id: string | null
  approval_status: string
  payment_status: string
  liquidation_review_status: string
  settlement_status: string
  posting_status: string
  submitted_at: string | null
  approved_at: string | null
  released_at: string | null
  closed_at: string | null
  created_at: string
  reconciliation: AdvanceReconciliation
  disbursements?: DisbursementRecord[]
}

export interface ExpenseLineRecord {
  id: string
  expense_report_id: string
  line_number: number
  expense_date: string
  category: string
  merchant_name: string
  description: string
  location_type: "in_base" | "out_of_base"
  receipt_or_number: string
  amount_minor: number
  amount: number
  vat_Convert_minor?: number
  tax_code_id: string | null
  review_status: "pending" | "approved" | "rejected"
  rejection_reason: string | null
  reviewed_by_user_id: string | null
}

export interface RuleEvaluationItem {
  rule_code: string
  outcome: "pass" | "soft_warn" | "exception_covered" | "hard_block"
  message: string
  Blocking: boolean
  blocking: boolean
  exception_eligible: boolean
  exception_id: string | null
}

export interface PolicyEvaluationResult {
  overall_passed: boolean
  policy_version_id: string
  period_key: string
  total_amount_minor: number
  projected_period_amount_minor?: number
  existing_submission_count?: number
  covered_by_exception_ids: string[]
  blocking_breaches: RuleEvaluationItem[]
  warnings: RuleEvaluationItem[]
  rule_evaluations: RuleEvaluationItem[]
}

export interface ExpenseReportRecord {
  id: string
  report_number: string
  employee_user_id: string
  department_id: string
  cost_center_id: string
  period_key: string
  title: string
  staff_type_snapshot: string
  out_of_base_flag: boolean
  consolidated_receipt_doc_id: string | null
  mfar_doc_id: string | null
  policy_version_id: string
  exception_request_id: string | null
  total_reported_Minor?: number
  total_reported_minor: number
  total_reported: number
  total_approved_minor: number
  total_approved: number
  total_rejected_minor: number
  total_rejected: number
  submission_status: string
  review_status: string
  settlement_status: string
  posting_status: string
  submitted_at: string | null
  reviewed_at: string | null
  closed_at: string | null
  lines: ExpenseLineRecord[]
}

export interface VendorRecord {
  id: string
  vendor_code: string
  legal_name?: string
  registered_name: string
  payee_type?: string
  tin: string
  bir_registration_status?: string
  default_tax_code_id?: string | null
  payment_terms_days?: number
  bank_name?: string | null
  bank_account_Masked?: string | null
  bank_account_masked?: string | null
  verification_status: string
  is_active?: boolean
  created_by_user_id?: string
  created_by_id?: string
  verified_by_user_id?: string | null
  verified_by_id?: string | null
}

export interface TaxCodeRecord {
  id?: string
  tax_code?: string
  code: string
  description: string
  tax_type: string
  rate_bps: number
  requires_bir_2307?: boolean
  cpa_approved?: boolean
  is_inclusive_default?: boolean
  bir_atc_code?: string
  is_active?: boolean
}

export interface PurchaseRequestRecord {
  id: string
  pr_number?: string
  request_number: string
  requester_user_id?: string
  requestor_id?: string
  department_id?: string
  cost_center_id?: string
  vendor_id?: string | null
  purpose?: string
  business_purpose?: string
  needed_by_date?: string
  requested_payment_date?: string
  requested_amount_minor?: number
  total_estimated_minor?: number
  total_estimated?: number
  currency?: string
  approval_status: string
  workflow_status?: string
  lines?: Array<{
    id: string
    line_number?: number
    item_description?: string
    description?: string
    quantity: number
    unit_cost_minor?: number
    unit_amount_minor?: number
    line_total_minor?: number
    total_amount_minor?: number
    expense_category?: string
  }>
}

export interface AccountPayableRecord {
  id: string
  ap_number?: string
  payable_number: string
  vendor_id: string
  purchase_request_id?: string | null
  invoice_number: string
  invoice_date: string
  due_date: string
  department_id?: string
  cost_center_id?: string
  original_payable_minor?: number
  gross_amount_minor?: number
  gross_amount?: number
  vat_amount_minor?: number
  ewt_amount_minor?: number
  net_payable_minor: number
  net_payable?: number
  paid_confirmed_minor?: number
  paid_amount_minor?: number
  paid_amount?: number
  remaining_balance_minor?: number
  balance_minor?: number
  balance?: number
  vat_code?: string | null
  ewt_code?: string | null
  tax_code_id?: string | null
  invoice_doc_ref_id?: string | null
  duplicate_flag?: boolean
  tax_review_status?: string
  status?: string
  payment_status?: string
}

export interface CentralizedTrackerRow {
  entity_type: string
  entity_id: string
  reference_number: string
  transaction_date: string
  employee_or_vendor: string
  department_id: string
  cost_center_id: string
  category: string
  gross_amount_minor: number
  gross_amount: number
  approved_net_amount_minor: number
  approved_net_amount: number
  outstanding_balance_minor: number
  outstanding_balance: number
  approval_status: string
  payment_status: string
  liquidation_or_review_status: string
  settlement_status: string
  posting_status: string
  policy_exception_flag: boolean
  document_Evidence_id: string | null
  bank_reference_number: string | null
}

export interface GlAccountRecord {
  accountCode: string
  accountName: string
  accountType: "Asset" | "Liability" | "Equity" | "Revenue" | "Expense" | string
  normalBalance?: string
  defaultTaxProfile?: string
  isActive?: boolean
  description?: string
}

export interface GlJournalLineDto {
  id?: string
  lineNumber?: number
  accountCode: string
  accountName?: string
  debitCentavos: number
  creditCentavos: number
  departmentId: string
  costCenterId?: string
  purposeCode?: string | null
  taxProfile?: string
  description?: string
}

export interface GlJournalBatchDto {
  journalId: string
  journalNumber: string
  sourceModule: "CashAdvance" | "ExpenseReport" | "AccountPayable" | "ManualAdjustment" | string
  sourceDocumentId?: string
  sourceDocumentNumber: string
  accountingPeriod?: string
  accountingDate: string
  postingStatus: "Validated" | "Approved" | "Posted" | "Rejected" | string
  policyVersionId?: string
  taxRuleVersion?: string
  totalDebitCentavos?: number
  totalCreditCentavos?: number
  preparedByActorId: string
  preparedByName: string
  verifiedByActorId?: string | null
  verifiedByName?: string | null
  postedByActorId?: string | null
  postedByName?: string | null
  auditHash: string
  memo: string
  lines: GlJournalLineDto[]
}

export interface GlTrialBalanceRowDto {
  accountCode: string
  accountName: string
  accountType: string
  normalBalance: string
  openingDebitCentavos: number
  openingCreditCentavos: number
  periodDebitCentavos: number
  periodCreditCentavos: number
  endingDebitCentavos: number
  endingCreditCentavos: number
}

export interface GlTrialBalanceSummaryDto {
  accountingPeriod: string
  isBalanced: boolean
  totalPeriodDebitCentavos: number
  totalPeriodCreditCentavos: number
  totalEndingDebitCentavos: number
  totalEndingCreditCentavos: number
  rows: GlTrialBalanceRowDto[]
}

export interface ApprovalMatrixRule {
  id: string
  workflow: string
  stage: string
  role: string
  department_scope: string
  min_amount_minor: number
  max_amount_minor: number
  currency: string
  is_active: boolean
}

function normalizeCashAdvanceRecord(raw: any): CashAdvanceRecord {
  if (!raw || typeof raw !== "object") return raw
  const adv = raw.advance ? raw.advance : raw
  const requestedMinor = Number(adv.requested_amount_minor ?? 0)
  const outstandingMinor =
    raw.outstanding_balance_minor ??
    adv.reconciliation?.outstanding_balance_minor ??
    adv.outstanding_balance_minor ??
    requestedMinor

  return {
    ...adv,
    employee_user_id: adv.employee_user_id ?? adv.employee_id ?? "",
    purpose: adv.purpose ?? adv.business_justification ?? adv.business_purpose ?? "",
    project_trip_ref: adv.project_trip_ref ?? adv.purpose_code ?? "",
    period_key: adv.period_key ?? String(adv.date_needed ?? "").slice(0, 7),
    needed_by_date: adv.needed_by_date ?? adv.date_needed ?? "",
    requested_amount_minor: requestedMinor,
    requested_amount: adv.requested_amount ?? requestedMinor / 100,
    approved_amount_minor: adv.approved_amount_minor ?? null,
    approved_amount:
      adv.approved_amount ?? (adv.approved_amount_minor != null ? adv.approved_amount_minor / 100 : null),
    created_at: adv.created_at ?? adv.created_at_utc ?? "",
    reconciliation: adv.reconciliation ?? {
      released_amount_minor: 0,
      approved_expenses_applied_minor: 0,
      cash_returned_minor: 0,
      reimbursement_paid_minor: 0,
      outstanding_balance_minor: Number(outstandingMinor),
      excess_reimbursable_minor: 0,
    },
  }
}

function normalizeDisbursementRecord(raw: any): DisbursementRecord {
  if (!raw || typeof raw !== "object") return raw
  return {
    ...raw,
    payment_amount_minor: Number(raw.payment_amount_minor ?? raw.amount_minor ?? 0),
    maker_user_id: raw.maker_user_id ?? raw.maker_id,
    verifier_user_id: raw.verifier_user_id ?? raw.verifier_id ?? null,
    bank_approver_user_id: raw.bank_approver_user_id ?? raw.bank_approver_id ?? null,
    executed_by_user_id: raw.executed_by_user_id ?? raw.executed_by_id ?? null,
    evidence_verified_by_user_id: raw.evidence_verified_by_user_id ?? raw.evidence_verifier_id ?? null,
    bank_reference_number: raw.bank_reference_number ?? raw.bank_reference ?? null,
  }
}

function normalizeExpenseReportRecord(raw: any): ExpenseReportRecord {
  if (!raw || typeof raw !== "object") return raw
  const claimedMinor = Number(raw.total_reported_minor ?? raw.total_claimed_amount_minor ?? 0)
  const approvedMinor = Number(raw.total_approved_minor ?? raw.total_approved_amount_minor ?? 0)
  const lines = Array.isArray(raw.lines)
    ? raw.lines.map((l: any, idx: number) => ({
        ...l,
        line_number: l.line_number ?? idx + 1,
        expense_date: l.expense_date ?? l.transaction_date ?? "",
        category: l.category ?? l.expense_category ?? "",
        merchant_name: l.merchant_name ?? l.merchant_or_payee ?? "",
        description: l.description ?? l.business_purpose ?? "",
        receipt_or_number: l.receipt_or_number ?? l.receipt_or_invoice_no ?? "",
        amount_minor: Number(l.amount_minor ?? l.claimed_amount_minor ?? 0),
        amount: l.amount ?? Number(l.amount_minor ?? l.claimed_amount_minor ?? 0) / 100,
        review_status: l.review_status ?? l.status ?? "pending",
      }))
    : []

  return {
    ...raw,
    employee_user_id: raw.employee_user_id ?? raw.employee_id ?? "",
    period_key: raw.period_key ?? raw.reporting_period ?? "",
    title: raw.title ?? raw.business_purpose_summary ?? "",
    submission_status: raw.submission_status ?? raw.review_status ?? "draft",
    review_status: raw.review_status ?? "draft",
    total_reported_minor: claimedMinor,
    total_reported: raw.total_reported ?? claimedMinor / 100,
    total_approved_minor: approvedMinor,
    total_approved: raw.total_approved ?? approvedMinor / 100,
    total_rejected_minor: Number(raw.total_rejected_minor ?? Math.max(0, claimedMinor - approvedMinor)),
    lines,
  }
}

function normalizeVendorRecord(raw: any): VendorRecord {
  if (!raw || typeof raw !== "object") return raw
  return {
    ...raw,
    legal_name: raw.legal_name ?? raw.registered_name ?? "",
    registered_name: raw.registered_name ?? raw.legal_name ?? "",
    bank_account_masked: raw.bank_account_masked ?? raw.masked_account_number ?? null,
    created_by_user_id: raw.created_by_user_id ?? raw.created_by_id,
    verified_by_user_id: raw.verified_by_user_id ?? raw.verified_by_id ?? null,
  }
}

function normalizePurchaseRequestRecord(raw: any): PurchaseRequestRecord {
  if (!raw || typeof raw !== "object") return raw
  const amountMinor = Number(raw.requested_amount_minor ?? raw.total_estimated_minor ?? 0)
  return {
    ...raw,
    pr_number: raw.pr_number ?? raw.request_number ?? "",
    request_number: raw.request_number ?? raw.pr_number ?? "",
    requester_user_id: raw.requester_user_id ?? raw.requestor_id ?? "",
    purpose: raw.purpose ?? raw.business_purpose ?? "",
    requested_amount_minor: amountMinor,
    total_estimated_minor: raw.total_estimated_minor ?? amountMinor,
    total_estimated: raw.total_estimated ?? amountMinor / 100,
  }
}

function normalizeAccountPayableRecord(raw: any): AccountPayableRecord {
  if (!raw || typeof raw !== "object") return raw
  const grossMinor = Number(raw.gross_amount_minor ?? raw.original_payable_minor ?? 0)
  const netMinor = Number(raw.net_payable_minor ?? grossMinor)
  const paidMinor = Number(raw.paid_amount_minor ?? raw.paid_confirmed_minor ?? 0)
  return {
    ...raw,
    ap_number: raw.ap_number ?? raw.payable_number ?? "",
    payable_number: raw.payable_number ?? raw.ap_number ?? "",
    gross_amount_minor: grossMinor,
    gross_amount: raw.gross_amount ?? grossMinor / 100,
    net_payable_minor: netMinor,
    net_payable: raw.net_payable ?? netMinor / 100,
    paid_amount_minor: paidMinor,
    payment_status: raw.payment_status ?? raw.status ?? "open",
  }
}

function normalizeTrackerRow(raw: any): CentralizedTrackerRow {
  if (!raw || typeof raw !== "object") return raw
  const amountMinor = Number(raw.gross_amount_minor ?? raw.amount_minor ?? 0)
  return {
    ...raw,
    entity_type: raw.entity_type ?? raw.record_type ?? "finance_record",
    entity_id: raw.entity_id ?? raw.id ?? raw.record_number ?? "",
    reference_number: raw.reference_number ?? raw.record_number ?? "",
    transaction_date: raw.transaction_date ?? String(raw.created_at_utc ?? "").slice(0, 10),
    employee_or_vendor: raw.employee_or_vendor ?? raw.employee_id ?? raw.vendor_name ?? raw.department_id ?? "N/A",
    category: raw.category ?? raw.stage ?? raw.record_type ?? "",
    gross_amount_minor: amountMinor,
    gross_amount: raw.gross_amount ?? amountMinor / 100,
    approved_net_amount_minor: raw.approved_net_amount_minor ?? amountMinor,
    approved_net_amount: raw.approved_net_amount ?? amountMinor / 100,
    outstanding_balance_minor: raw.outstanding_balance_minor ?? amountMinor,
    outstanding_balance: raw.outstanding_balance ?? amountMinor / 100,
    approval_status: raw.approval_status ?? raw.status ?? "open",
    payment_status: raw.payment_status ?? raw.stage ?? raw.status ?? "open",
  }
}

const API_BASE = ""

export function formatPhpMinor(minor: number | null | undefined): string {
  const val = (minor ?? 0) / 100
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(val)
}

function buildHeaders(actorUserId?: string, isJson = true): HeadersInit {
  const headers: Record<string, string> = {}
  if (actorUserId) {
    headers["X-User-Id"] = actorUserId
  }
  if (isJson) {
    headers["Content-Type"] = "application/json"
  }
  return headers
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    const errBody = await res.json().catch(() => ({}))
    const detail = errBody?.detail
    if (typeof detail === "object" && detail !== null) {
      throw new Error(detail.message || JSON.stringify(detail))
    }
    throw new Error(detail || `Request failed (${res.status})`)
  }
  return res.json()
}

export async function fetchFinanceMe(actorUserId: string): Promise<{
  actor: FinanceUser & { approval_limits: any[] }
  auth_mode: string
  users: FinanceUser[]
}> {
  const res = await fetch(`${API_BASE}/api/v1/finance/auth/me`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function fetchFinanceUsers(actorUserId?: string): Promise<FinanceUser[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/users`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse<FinanceUser[]>(res)
}

export async function switchDevActor(currentActorId: string, targetUserId: string, reason: string) {
  const res = await fetch(`${API_BASE}/api/v1/finance/auth/act-as`, {
    method: "POST",
    headers: buildHeaders(currentActorId),
    body: JSON.stringify({ target_user_id: targetUserId, reason }),
  })
  return handleResponse(res)
}

export async function fetchFinanceDashboard(actorUserId: string, periodKey?: string, departmentId?: string) {
  const params = new URLSearchParams()
  if (periodKey) params.set("period_key", periodKey)
  if (departmentId) params.set("department_id", departmentId)
  const qs = params.toString() ? `?${params.toString()}` : ""
  const res = await fetch(`${API_BASE}/api/v1/finance/dashboard/summary${qs}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any>(res)
  if (raw && !raw.kpis) {
    return {
      ...raw,
      kpis: {
        open_advances: {
          aggregate_balance_minor: raw.outstanding_advance_balance_minor ?? 0,
          released_total_minor: raw.total_advances_released_minor ?? 0,
          active_count: raw.open_expense_reports_count ?? 0,
          unliquidated_count: raw.pending_approvals_count ?? 0,
          overdue_balance_minor: 0,
          overdue_count: raw.overdue_advances_count ?? 0,
        },
        liquidations: {
          total_liquidated_expense_minor: raw.total_liquidated_expense_minor ?? 0,
          on_time_liquidation_rate_pct: raw.on_time_liquidation_rate_pct ?? 100,
        },
        accounts_payable: {
          total_net_payable_minor: 0,
          open_payables_count: 0,
          tax_review_required_count: 0,
        },
      },
    }
  }
  return raw
}

export async function fetchKpiDefinitions(actorUserId: string) {
  const primary = await fetch(`${API_BASE}/api/v1/finance/kpi-definitions`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  if (primary.ok) {
    return handleResponse<any>(primary)
  }
  const fallback = await fetch(`${API_BASE}/api/v1/finance/dashboard/kpi-definitions`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse<any>(fallback)
}

export async function fetchApprovalMatrix(actorUserId: string): Promise<ApprovalMatrixRule[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/approval-matrix`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  if (!Array.isArray(raw)) return []
  return raw.map((item: any) => ({
    id: item.id,
    workflow: item.workflow,
    stage: item.stage,
    role: item.role,
    department_scope: item.department_scope ?? "*",
    min_amount_minor: Number(item.min_amount?.amount_minor ?? item.min_amount_minor ?? 0),
    max_amount_minor: Number(item.max_amount?.amount_minor ?? item.max_amount_minor ?? 0),
    currency: item.min_amount?.currency_code ?? item.currency ?? "PHP",
    is_active: item.is_active ?? true,
  }))
}

export async function fetchBusinessConfirmations(actorUserId: string): Promise<any[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/business-confirmations`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse<any[]>(res)
}

export async function fetchPolicies(actorUserId: string): Promise<FinancialPolicyRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/policies`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  if (!Array.isArray(raw)) return []
  return raw.map((p: any) => ({
    ...p,
    versions: Array.isArray(p.versions) ? p.versions : [],
  }))
}

export async function createPolicyVersion(
  actorUserId: string,
  policyCode: string,
  payload: any
): Promise<PolicyVersionRecord> {
  const normalizedPayload = {
    ...payload,
    version_number: payload.version_number ?? Math.floor(Date.now() / 1000) % 100000,
    submission_deadline_day: payload.submission_deadline_day ?? payload.submission_deadline_day_of_next_month ?? 2,
  }
  const res = await fetch(`${API_BASE}/api/v1/finance/policies/${policyCode}/versions`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(normalizedPayload),
  })
  return handleResponse(res)
}

export async function submitPolicyVersion(actorUserId: string, versionId: string): Promise<any> {
  const res = await fetch(`${API_BASE}/api/v1/finance/policies/versions/${versionId}/submit`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
  })
  return handleResponse(res)
}

export async function approvePolicyVersion(
  actorUserId: string,
  versionId: string,
  comments = "Approved policy version"
): Promise<PolicyVersionRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/policies/versions/${versionId}/approve`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ comments }),
  })
  return handleResponse(res)
}

export async function retirePolicyVersion(
  actorUserId: string,
  versionId: string,
  comments = "Retiring superseded policy version"
): Promise<PolicyVersionRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/policies/versions/${versionId}/retire`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ comments, reason: comments }),
  })
  return handleResponse(res)
}

export async function fetchExceptionRequests(actorUserId: string, status?: string): Promise<ExceptionRequestRecord[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : ""
  const res = await fetch(`${API_BASE}/api/v1/finance/exception-requests${qs}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  if (!Array.isArray(raw)) return []
  return raw.map((e: any) => ({
    ...e,
    rule_code: e.rule_code ?? e.violated_rule_code ?? "",
    justification: e.justification ?? e.business_justification ?? "",
  }))
}

export async function createExceptionRequest(actorUserId: string, payload: any): Promise<ExceptionRequestRecord> {
  const normalizedPayload = {
    ...payload,
    employee_id: payload.employee_id ?? actorUserId,
    target_entity_type: payload.target_entity_type ?? "expense_report",
    policy_version_id: payload.policy_version_id ?? "POL-VER-DEFAULT",
  }
  const res = await fetch(`${API_BASE}/api/v1/finance/exception-requests`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(normalizedPayload),
  })
  return handleResponse(res)
}

export async function decideExceptionRequest(
  actorUserId: string,
  exceptionId: string,
  payload: { decision: "approved" | "rejected"; decision_reason: string; approved_max_amount_minor?: number }
): Promise<ExceptionRequestRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/exception-requests/${exceptionId}/decide`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      reason: payload.decision_reason,
    }),
  })
  return handleResponse(res)
}

export async function uploadDocumentJson(
  actorUserId: string,
  payload: {
    original_filename: string
    document_type: string
    content_base64: string
    department_id?: string
    entity_type?: string
    entity_id?: string
    is_restricted?: boolean
  }
) {
  if (typeof FormData !== "undefined" && typeof Blob !== "undefined") {
    const rawText =
      typeof atob !== "undefined" && payload.content_base64
        ? atob(payload.content_base64)
        : payload.content_base64 || "%PDF-1.4\n%%EOF\n"
    const bytes = new Uint8Array(rawText.length)
    for (let i = 0; i < rawText.length; i += 1) {
      bytes[i] = rawText.charCodeAt(i)
    }
    const formData = new FormData()
    formData.append("file", new Blob([bytes], { type: "application/pdf" }), payload.original_filename)
    formData.append("document_type", payload.document_type)
    const res = await fetch(`${API_BASE}/api/v1/finance/documents`, {
      method: "POST",
      headers: buildHeaders(actorUserId, false),
      body: formData,
    })
    return handleResponse<any>(res)
  }

  const res = await fetch(`${API_BASE}/api/v1/finance/documents`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse<any>(res)
}

export async function fetchCashAdvances(actorUserId: string): Promise<CashAdvanceRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  return Array.isArray(raw) ? raw.map(normalizeCashAdvanceRecord) : []
}

export async function getCashAdvanceDetail(actorUserId: string, advanceId: string): Promise<CashAdvanceRecord & { ledger_entries?: AdvanceLedgerEntryRecord[] }> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any>(res)
  const normalized = normalizeCashAdvanceRecord(raw)
  return {
    ...normalized,
    ledger_entries: Array.isArray(raw?.ledger_entries) ? raw.ledger_entries : [],
  }
}

export async function createCashAdvance(actorUserId: string, payload: any): Promise<CashAdvanceRecord> {
  const normalizedPayload = {
    ...payload,
    employee_id: payload.employee_id ?? actorUserId,
    department_id: payload.department_id ?? "manufacturing",
    cost_center_id: payload.cost_center_id ?? "CC-001",
    purpose_category: payload.purpose_category ?? payload.purpose_code ?? "field_travel_operations",
    business_purpose: payload.business_purpose ?? payload.business_justification ?? "",
    requested_disbursement_date: payload.requested_disbursement_date ?? payload.date_needed ?? "",
  }
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(normalizedPayload),
  })
  return normalizeCashAdvanceRecord(await handleResponse(res))
}

export async function submitCashAdvance(actorUserId: string, advanceId: string): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/submit`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
  })
  return normalizeCashAdvanceRecord(await handleResponse(res))
}

export async function decideCashAdvance(
  actorUserId: string,
  advanceId: string,
  payload: { decision: "approved" | "rejected" | "returned_for_correction" | "cancelled"; comments: string; approved_amount_minor?: number }
): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/decide`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      rejection_reason: payload.decision === "rejected" ? payload.comments : undefined,
      correction_notes: payload.decision === "returned_for_correction" ? payload.comments : undefined,
    }),
  })
  return normalizeCashAdvanceRecord(await handleResponse(res))
}

export async function recordCashReturn(
  actorUserId: string,
  advanceId: string,
  payload: { returned_amount_minor: number; bank_reference: string; evidence_document_id: string; notes?: string }
): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/cash-return`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      amount_minor: payload.returned_amount_minor,
      return_method: "bank_transfer",
      return_reference: payload.bank_reference,
      remittance_doc_id: payload.evidence_document_id,
    }),
  })
  return normalizeCashAdvanceRecord(await handleResponse(res))
}

export async function settleCashAdvanceReimbursement(
  actorUserId: string,
  advanceId: string,
  payload: { amount_minor: number; settlement_method?: string; bank_reference: string; remittance_doc_id?: string }
): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/reimbursement-settle`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      amount_minor: payload.amount_minor,
      settlement_method: payload.settlement_method ?? "bank_transfer",
      bank_reference: payload.bank_reference,
      remittance_doc_id: payload.remittance_doc_id,
    }),
  })
  return normalizeCashAdvanceRecord(await handleResponse(res))
}

export async function closeCashAdvance(
  actorUserId: string,
  advanceId: string,
  payload: { closure_reason: string; authorized_exception_id?: string }
): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/close`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      remarks: payload.closure_reason,
    }),
  })
  return normalizeCashAdvanceRecord(await handleResponse(res))
}

export async function createDisbursement(
  actorUserId: string,
  advanceId: string,
  payload: { payment_amount_minor: number; payment_method?: string; recipient_account_ref?: string }
): Promise<DisbursementRecord> {
  const method = payload.payment_method ?? "bank_transfer"
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/disbursements`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      target_payment_method: method,
      bank_account_number: payload.recipient_account_ref,
      bank_account_name: payload.recipient_account_ref,
    }),
  })
  return normalizeDisbursementRecord(await handleResponse(res))
}

export async function verifyDisbursement(actorUserId: string, disbursementId: string, notes = "Verified payee and details") {
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/verify`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return normalizeDisbursementRecord(await handleResponse<DisbursementRecord>(res))
}

export async function approveDisbursementBankRelease(actorUserId: string, disbursementId: string, notes = "Authorized release") {
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/bank-approve`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return normalizeDisbursementRecord(await handleResponse<DisbursementRecord>(res))
}

export async function executeDisbursement(
  actorUserId: string,
  disbursementId: string,
  payload: {
    bank_reference: string
    executed_amount_minor: number
    evidence_document_id: string
    execution_date?: string
    is_settled_immediately_by_bank?: boolean
  }
) {
  const normalizedPayload = {
    ...payload,
    execution_date: payload.execution_date ?? new Date().toISOString().slice(0, 10),
    remittance_doc_id: payload.evidence_document_id,
    is_settled_immediately_by_bank: payload.is_settled_immediately_by_bank ?? true,
  }
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/execute`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(normalizedPayload),
  })
  return normalizeDisbursementRecord(await handleResponse<DisbursementRecord>(res))
}

export async function verifyDisbursementEvidence(
  actorUserId: string,
  disbursementId: string,
  notes = "Remittance evidence verified",
  evidenceDocumentId?: string
) {
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/verify-evidence`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      notes,
      evidence_document_id: evidenceDocumentId ?? "DOC-VERIFIED",
    }),
  })
  return normalizeDisbursementRecord(await handleResponse<DisbursementRecord>(res))
}

export async function fetchExpenseReports(actorUserId: string): Promise<ExpenseReportRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  return Array.isArray(raw) ? raw.map(normalizeExpenseReportRecord) : []
}

export async function getExpenseReportDetail(actorUserId: string, reportId: string): Promise<ExpenseReportRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return normalizeExpenseReportRecord(await handleResponse(res))
}

export async function createExpenseReport(actorUserId: string, payload: any): Promise<ExpenseReportRecord> {
  const normalizedPayload = {
    ...payload,
    employee_id: payload.employee_id ?? actorUserId,
    department_id: payload.department_id ?? "manufacturing",
    cost_center_id: payload.cost_center_id ?? "CC-001",
  }
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(normalizedPayload),
  })
  return normalizeExpenseReportRecord(await handleResponse(res))
}

export async function addExpenseLine(actorUserId: string, reportId: string, payload: any): Promise<ExpenseReportRecord> {
  const bodyPayload = Array.isArray(payload?.lines)
    ? payload
    : { ...payload, lines: [payload] }
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}/lines`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(bodyPayload),
  })
  return normalizeExpenseReportRecord(await handleResponse(res))
}

export async function evaluateExpenseReport(actorUserId: string, reportId: string): Promise<PolicyEvaluationResult> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}/evaluate`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
  })
  const raw = await handleResponse<any>(res)
  if (raw && raw.overall_passed === undefined && raw.is_allowed !== undefined) {
    const violations = Array.isArray(raw.violations) ? raw.violations : []
    const warnings = Array.isArray(raw.warnings) ? raw.warnings : []
    return {
      overall_passed: Boolean(raw.is_allowed),
      policy_version_id: raw.policy_version_id ?? "ACTIVE-POL",
      period_key: raw.period_key ?? new Date().toISOString().slice(0, 7),
      total_amount_minor: raw.total_amount_minor ?? 0,
      covered_by_exception_ids: raw.covered_by_exception_ids ?? [],
      blocking_breaches: violations.map((v: string, idx: number) => ({
        rule_code: `RULE_${idx + 1}`,
        outcome: "hard_block",
        message: v,
        Blocking: true,
        blocking: true,
        exception_eligible: true,
        exception_id: null,
      })),
      warnings: warnings.map((w: string, idx: number) => ({
        rule_code: `WARN_${idx + 1}`,
        outcome: "soft_warn",
        message: w,
        Blocking: false,
        blocking: false,
        exception_eligible: false,
        exception_id: null,
      })),
      rule_evaluations: [
        ...violations.map((v: string, idx: number) => ({
          rule_code: `BREACH_${idx + 1}`,
          outcome: "hard_block" as const,
          message: v,
          Blocking: true,
          blocking: true,
          exception_eligible: true,
          exception_id: null,
        })),
        ...warnings.map((w: string, idx: number) => ({
          rule_code: `WARN_${idx + 1}`,
          outcome: "soft_warn" as const,
          message: w,
          Blocking: false,
          blocking: false,
          exception_eligible: false,
          exception_id: null,
        })),
      ],
    }
  }
  return raw
}

export async function submitExpenseReport(actorUserId: string, reportId: string, confirmed = true): Promise<ExpenseReportRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}/submit`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ employee_declaration_confirmed: confirmed }),
  })
  return normalizeExpenseReportRecord(await handleResponse(res))
}

export async function reviewExpenseReport(
  actorUserId: string,
  reportId: string,
  payload: {
    report_decision: "approved" | "partially_approved" | "returned_for_correction" | "rejected"
    review_notes: string
    line_decisions: Array<{
      line_id: string
      line_status: "approved" | "partially_approved" | "rejected"
      approved_amount_minor?: number
      rejection_reason?: string
    }>
  }
): Promise<ExpenseReportRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}/review`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      outcome: payload.report_decision,
      notes: payload.review_notes,
      rejection_reason: payload.report_decision === "rejected" ? payload.review_notes : undefined,
    }),
  })
  return normalizeExpenseReportRecord(await handleResponse(res))
}

export async function fetchVendors(actorUserId: string): Promise<VendorRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendors`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  return Array.isArray(raw) ? raw.map(normalizeVendorRecord) : []
}

export async function createVendor(actorUserId: string, payload: any): Promise<VendorRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendors`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return normalizeVendorRecord(await handleResponse(res))
}

export async function verifyVendor(actorUserId: string, vendorId: string, notes = "KYC verified"): Promise<VendorRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendors/${vendorId}/verify`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ decision: "verified", notes }),
  })
  return normalizeVendorRecord(await handleResponse(res))
}

export async function requestVendorBankChange(
  actorUserId: string,
  vendorId: string,
  payload: {
    new_bank_name: string
    raw_new_account_number: string
    new_bank_account_ref: string
    reason: string
    supporting_doc_id?: string
  }
): Promise<VendorBankChangeRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendors/${vendorId}/bank-changes`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function decideVendorBankChange(
  actorUserId: string,
  changeId: string,
  payload: { decision: "approved" | "rejected"; reason?: string }
): Promise<VendorBankChangeRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendor-bank-changes/${changeId}/decide`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function fetchTaxCodes(actorUserId: string): Promise<TaxCodeRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/tax-codes`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function createTaxCode(actorUserId: string, payload: any): Promise<TaxCodeRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/tax-codes`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      requires_bir2307: payload.requires_bir2307 ?? payload.requires_bir_2307 ?? false,
    }),
  })
  return handleResponse(res)
}

export async function fetchPurchaseRequests(actorUserId: string): Promise<PurchaseRequestRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/purchase-requests`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  return Array.isArray(raw) ? raw.map(normalizePurchaseRequestRecord) : []
}

export async function createPurchaseRequest(actorUserId: string, payload: any): Promise<PurchaseRequestRecord> {
  const normalizedPayload = {
    ...payload,
    requestor_id: payload.requestor_id ?? actorUserId,
    department_id: payload.department_id ?? "manufacturing",
    cost_center_id: payload.cost_center_id ?? "CC-001",
    is_restricted_category: payload.is_restricted_category ?? false,
  }
  const res = await fetch(`${API_BASE}/api/v1/finance/purchase-requests`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(normalizedPayload),
  })
  return normalizePurchaseRequestRecord(await handleResponse(res))
}

export async function submitPurchaseRequest(actorUserId: string, prId: string): Promise<PurchaseRequestRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/purchase-requests/${prId}/submit`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
  })
  return normalizePurchaseRequestRecord(await handleResponse(res))
}

export async function decidePurchaseRequest(
  actorUserId: string,
  prId: string,
  decision: "approved" | "rejected" | "returned_for_correction" | "cancelled" = "approved",
  comments = "Approved PR"
): Promise<PurchaseRequestRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/purchase-requests/${prId}/decide`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ decision, reason: comments, comments }),
  })
  return normalizePurchaseRequestRecord(await handleResponse(res))
}

export async function fetchAccountsPayable(actorUserId: string): Promise<AccountPayableRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payables`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  return Array.isArray(raw) ? raw.map(normalizeAccountPayableRecord) : []
}

export async function validateAccountPayable(actorUserId: string, payload: any): Promise<AccountPayableRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payables`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return normalizeAccountPayableRecord(await handleResponse(res))
}

export async function preparePayablePayment(actorUserId: string, payableId: string, payload: any): Promise<PayablePaymentRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payables/${payableId}/payments`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function verifyPayablePayment(actorUserId: string, paymentId: string, notes = "Verified payment details"): Promise<PayablePaymentRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payment-records/${paymentId}/verify`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return handleResponse(res)
}

export async function approvePayablePaymentBank(actorUserId: string, paymentId: string, notes = "Authorized payable payment"): Promise<PayablePaymentRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payment-records/${paymentId}/bank-approve`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return handleResponse(res)
}

export async function executePayablePayment(
  actorUserId: string,
  paymentId: string,
  payload: {
    bank_reference: string
    executed_amount_minor: number
    execution_date: string
    remittance_doc_id: string
  }
): Promise<PayablePaymentRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payment-records/${paymentId}/execute`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function reconcilePayablePayment(actorUserId: string, paymentId: string, notes = "Reconciled"): Promise<PayablePaymentRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payment-records/${paymentId}/reconcile`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ reconciliation_outcome: "matched", notes }),
  })
  return handleResponse(res)
}

export async function fetchCentralizedTracker(
  actorUserId: string,
  filters: { workflow?: string; department_id?: string; status?: string; search?: string } = {}
): Promise<CentralizedTrackerRow[]> {
  const params = new URLSearchParams()
  if (filters.workflow) params.set("workflow", filters.workflow)
  if (filters.department_id) params.set("department_id", filters.department_id)
  if (filters.status) params.set("status", filters.status)
  if (filters.search) params.set("search", filters.search)
  const qs = params.toString() ? `?${params.toString()}` : ""
  const res = await fetch(`${API_BASE}/api/v1/finance/tracker${qs}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  return Array.isArray(raw) ? raw.map(normalizeTrackerRow) : []
}

export async function downloadTrackerCsv(actorUserId: string): Promise<void> {
  const res = await fetch(`${API_BASE}/api/v1/finance/tracker/export.csv`, {
    headers: buildHeaders(actorUserId, false),
  })
  if (!res.ok) throw new Error("Failed to export tracker CSV")
  const blob = await res.blob()
  const url = window.URL.createObjectURL(blob)
  const a = document.createElement("a")
  a.href = url
  a.download = `finance_record_tracker_${new Date().toISOString().slice(0, 10)}.csv`
  document.body.appendChild(a)
  a.click()
  a.remove()
  window.URL.revokeObjectURL(url)
}

export async function fetchExceptionRegister(actorUserId: string, status?: string): Promise<any[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : ""
  const res = await fetch(`${API_BASE}/api/v1/finance/exception-register${qs}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function resolveExceptionRegisterItem(
  actorUserId: string,
  itemId: string,
  payload: { target_status: "resolved" | "under_review" | "reopened"; resolution_notes: string; evidence_doc_id?: string }
): Promise<any> {
  const res = await fetch(`${API_BASE}/api/v1/finance/exception-register/${itemId}/resolve`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      ...payload,
      resolution_status: payload.target_status,
      notes: payload.resolution_notes,
    }),
  })
  return handleResponse(res)
}

export async function fetchAuditEvents(actorUserId: string, entityType?: string): Promise<{
  chain_integrity: { is_valid: boolean; checked_count: number; broken_event_id: string | null }
  events: any[]
}> {
  const qs = entityType ? `?entity_type=${encodeURIComponent(entityType)}` : ""
  const res = await fetch(`${API_BASE}/api/v1/finance/audit-events${qs}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

// =========================================================================
// Official General Ledger (Subledger-to-GL) API Functions
// =========================================================================

export async function fetchGlAccounts(actorUserId: string): Promise<GlAccountRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/gl/accounts`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  if (!Array.isArray(raw)) return []
  return raw.map((a: any) => ({
    accountCode: a.accountCode ?? a.account_code ?? "",
    accountName: a.accountName ?? a.account_name ?? "",
    accountType: a.accountType ?? a.account_type ?? "Asset",
    normalBalance: a.normalBalance ?? a.normal_balance,
    defaultTaxProfile: a.defaultTaxProfile ?? a.default_tax_profile,
    isActive: a.isActive ?? a.is_active ?? true,
    description: a.description ?? "",
  }))
}

export async function fetchGlJournals(
  actorUserId: string,
  params: { period: string; status?: string; sourceModule?: string; limit?: number }
): Promise<GlJournalBatchDto[]> {
  const qs = new URLSearchParams({ period: params.period })
  if (params.status) qs.set("status", params.status)
  if (params.sourceModule && params.sourceModule !== "ALL") qs.set("source_module", params.sourceModule)
  if (params.limit) qs.set("limit", String(params.limit))
  const res = await fetch(`${API_BASE}/api/v1/finance/gl/journals?${qs.toString()}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any[]>(res)
  if (!Array.isArray(raw)) return []
  return raw.map((j: any) => ({
    journalId: j.journalId ?? j.journal_id ?? "",
    journalNumber: j.journalNumber ?? j.journal_number ?? "",
    sourceModule: j.sourceModule ?? j.source_module ?? "ManualAdjustment",
    sourceDocumentId: j.sourceDocumentId ?? j.source_document_id,
    sourceDocumentNumber: j.sourceDocumentNumber ?? j.source_document_number ?? "",
    accountingPeriod: j.accountingPeriod ?? j.accounting_period,
    accountingDate: j.accountingDate ?? j.accounting_date ?? "",
    postingStatus: j.postingStatus ?? j.posting_status ?? "Validated",
    policyVersionId: j.policyVersionId ?? j.policy_version_id,
    taxRuleVersion: j.taxRuleVersion ?? j.tax_rule_version,
    totalDebitCentavos: Number(j.totalDebitCentavos ?? j.total_debit_centavos ?? 0),
    totalCreditCentavos: Number(j.totalCreditCentavos ?? j.total_credit_centavos ?? 0),
    preparedByActorId: j.preparedByActorId ?? j.prepared_by_actor_id ?? "",
    preparedByName: j.preparedByName ?? j.prepared_by_name ?? "",
    verifiedByActorId: j.verifiedByActorId ?? j.verified_by_actor_id ?? null,
    verifiedByName: j.verifiedByName ?? j.verified_by_name ?? null,
    postedByActorId: j.postedByActorId ?? j.posted_by_actor_id ?? null,
    postedByName: j.postedByName ?? j.posted_by_name ?? null,
    auditHash: j.auditHash ?? j.audit_hash ?? "",
    memo: j.memo ?? "",
    lines: Array.isArray(j.lines)
      ? j.lines.map((l: any, idx: number) => ({
          id: l.id ?? `${j.journalId ?? j.journal_id}-${l.lineNumber ?? l.line_number ?? idx}`,
          lineNumber: l.lineNumber ?? l.line_number ?? idx + 1,
          accountCode: l.accountCode ?? l.account_code ?? "",
          accountName: l.accountName ?? l.account_name ?? "",
          debitCentavos: Number(l.debitCentavos ?? l.debit_centavos ?? 0),
          creditCentavos: Number(l.creditCentavos ?? l.credit_centavos ?? 0),
          departmentId: l.departmentId ?? l.department_id ?? "",
          costCenterId: l.costCenterId ?? l.cost_center_id,
          purposeCode: l.purposeCode ?? l.purpose_code ?? null,
          taxProfile: l.taxProfile ?? l.tax_profile,
          description: l.description ?? "",
        }))
      : [],
  }))
}

export async function fetchGlTrialBalance(
  actorUserId: string,
  params: { period: string; departmentId?: string }
): Promise<GlTrialBalanceSummaryDto> {
  const qs = new URLSearchParams({ period: params.period })
  if (params.departmentId && params.departmentId !== "ALL") qs.set("department_id", params.departmentId)
  const res = await fetch(`${API_BASE}/api/v1/finance/gl/trial-balance?${qs.toString()}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  const raw = await handleResponse<any>(res)
  const rows = Array.isArray(raw?.rows)
    ? raw.rows.map((r: any) => ({
        accountCode: r.accountCode ?? r.account_code ?? "",
        accountName: r.accountName ?? r.account_name ?? "",
        accountType: r.accountType ?? r.account_type ?? "Asset",
        normalBalance: r.normalBalance ?? r.normal_balance ?? "Debit",
        openingDebitCentavos: Number(r.openingDebitCentavos ?? r.opening_debit_centavos ?? 0),
        openingCreditCentavos: Number(r.openingCreditCentavos ?? r.opening_credit_centavos ?? 0),
        periodDebitCentavos: Number(r.periodDebitCentavos ?? r.period_debit_centavos ?? 0),
        periodCreditCentavos: Number(r.periodCreditCentavos ?? r.period_credit_centavos ?? 0),
        endingDebitCentavos: Number(r.endingDebitCentavos ?? r.ending_debit_centavos ?? 0),
        endingCreditCentavos: Number(r.endingCreditCentavos ?? r.ending_credit_centavos ?? 0),
      }))
    : []

  return {
    accountingPeriod: raw?.accountingPeriod ?? raw?.accounting_period ?? params.period,
    isBalanced: Boolean(raw?.isBalanced ?? raw?.is_balanced ?? true),
    totalPeriodDebitCentavos: Number(raw?.totalPeriodDebitCentavos ?? raw?.total_period_debit_centavos ?? 0),
    totalPeriodCreditCentavos: Number(raw?.totalPeriodCreditCentavos ?? raw?.total_period_credit_centavos ?? 0),
    totalEndingDebitCentavos: Number(raw?.totalEndingDebitCentavos ?? raw?.total_ending_debit_centavos ?? 0),
    totalEndingCreditCentavos: Number(raw?.totalEndingCreditCentavos ?? raw?.total_ending_credit_centavos ?? 0),
    rows,
  }
}

export async function createManualGlJournal(
  actorUserId: string,
  payload: {
    accountingDate: string
    memo: string
    lines: Array<{
      accountCode: string
      debitCentavos: number
      creditCentavos: number
      departmentId: string
      costCenterId?: string
      purposeCode?: string | null
      taxProfile?: string
      description?: string
    }>
  }
): Promise<{ journalId: string }> {
  const res = await fetch(`${API_BASE}/api/v1/finance/gl/journals`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({
      accounting_date: payload.accountingDate,
      accountingDate: payload.accountingDate,
      memo: payload.memo,
      lines: payload.lines.map((l) => ({
        account_code: l.accountCode,
        accountCode: l.accountCode,
        debit_centavos: l.debitCentavos,
        debitCentavos: l.debitCentavos,
        credit_centavos: l.creditCentavos,
        creditCentavos: l.creditCentavos,
        department_id: l.departmentId,
        departmentId: l.departmentId,
        cost_center_id: l.costCenterId ?? "CC-CORP-00",
        costCenterId: l.costCenterId ?? "CC-CORP-00",
        purpose_code: l.purposeCode ?? null,
        purposeCode: l.purposeCode ?? null,
        tax_profile: l.taxProfile ?? "NONE",
        taxProfile: l.taxProfile ?? "NONE",
        description: l.description ?? payload.memo,
      })),
    }),
  })
  const raw = await handleResponse<any>(res)
  return { journalId: raw?.journalId ?? raw?.journal_id ?? "" }
}

export async function decideGlJournal(
  actorUserId: string,
  journalId: string,
  preparedByActorId: string,
  payload: { decision: "Approved" | "Posted" | "Rejected"; reason: string }
): Promise<{ journalId: string; status: string }> {
  const qs = new URLSearchParams({ prepared_by_actor_id: preparedByActorId })
  const res = await fetch(`${API_BASE}/api/v1/finance/gl/journals/${journalId}/decide?${qs.toString()}`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  const raw = await handleResponse<any>(res)
  return {
    journalId: raw?.journalId ?? raw?.journal_id ?? journalId,
    status: raw?.status ?? payload.decision,
  }
}

// =========================================================================
// Multi-Tab Paginated Finance Endpoints & Keyset Audit
// =========================================================================

import { apiFetchWithMetrics } from "@/lib/api-client"
import {
  buildOrdersQueryString,
  normalizePagedResponse,
  PageRequestParams,
  PagedResultWithMetrics,
} from "@/lib/payment-api"
import {
  KeysetPagePayload,
  normalizeKeysetResponse,
} from "@/hooks/use-keyset-paginated-resource"

export type FinanceResourceKey =
  | "cash-advances"
  | "expense-reports"
  | "purchase-requests"
  | "payables"
  | "vendors"
  | "exceptions"
  | "policies"
  | "tracker"

const FINANCE_ENDPOINT_MAP: Record<FinanceResourceKey, string> = {
  "cash-advances": "/api/finance/cash-advances",
  "expense-reports": "/api/finance/expense-reports",
  "purchase-requests": "/api/finance/purchase-requests",
  "payables": "/api/finance/payables",
  "vendors": "/api/finance/vendors",
  "exceptions": "/api/finance/exceptions",
  "policies": "/api/finance/policies",
  "tracker": "/api/finance/reporting/tracker",
}

export async function listFinanceResourcePaginated<TItem>(
  resource: FinanceResourceKey,
  params: PageRequestParams = {},
  options: { signal?: AbortSignal } = {}
): Promise<PagedResultWithMetrics<TItem>> {
  const endpoint = FINANCE_ENDPOINT_MAP[resource]
  const qs = buildOrdersQueryString(params)
  const res = await apiFetchWithMetrics<unknown>(`${endpoint}?${qs}`, {
    signal: options.signal,
  })

  const page = Math.max(1, params.page ?? 1)
  const pageSize = Math.min(100, Math.max(1, params.pageSize ?? 20))

  return {
    data: normalizePagedResponse<TItem>(res.data, page, pageSize),
    metrics: {
      apiDurationMs: res.metrics.durationMs,
      payloadBytes: res.metrics.payloadBytes,
      serverTiming: res.metrics.serverTiming,
    },
  }
}

export async function listAuditEventsKeyset<TEvent>(
  params: { limit?: number; cursor?: string | null; action?: string; search?: string } = {},
  options: { signal?: AbortSignal } = {}
): Promise<{ data: KeysetPagePayload<TEvent>; durationMs: number }> {
  const limit = Math.min(100, Math.max(1, params.limit ?? 20))
  const qs = new URLSearchParams({ limit: String(limit) })
  if (params.cursor) qs.set("cursor", params.cursor)
  if (params.action && params.action !== "All") qs.set("action", params.action)
  if (params.search?.trim()) qs.set("search", params.search.trim())

  const res = await apiFetchWithMetrics<unknown>(`/api/audit/events?${qs.toString()}`, {
    signal: options.signal,
  })

  return {
    data: normalizeKeysetResponse<TEvent>(res.data, limit),
    durationMs: res.metrics.durationMs,
  }
}


// CPA-Audited Finance Operations API Client & Helpers

export interface FinanceUser {
  id: string
  employee_code: string
  full_name: string
  email: string
  department_id: string
  cost_center_id: string
  staff_type: "field" | "non_field"
  employment_status: string
  base_location: string
  is_active: boolean
  roles: string[]
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
  created_at?: string
  executed_at?: string | null
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

function buildHeaders(actorUserId: string, isJson = true): HeadersInit {
  const headers: Record<string, string> = {
    "X-User-Id": actorUserId,
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
  return handleResponse<any>(res)
}

export async function fetchKpiDefinitions(actorUserId: string) {
  const res = await fetch(`${API_BASE}/api/v1/finance/dashboard/kpi-definitions`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse<{ kpis: Record<string, any>; business_confirmations: any[] }>(res)
}

export async function fetchPolicies(actorUserId: string): Promise<FinancialPolicyRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/policies`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function createPolicyVersion(
  actorUserId: string,
  policyCode: string,
  payload: any
): Promise<PolicyVersionRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/policies/${policyCode}/versions`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
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
    body: JSON.stringify({ comments }),
  })
  return handleResponse(res)
}

export async function fetchExceptionRequests(actorUserId: string, status?: string): Promise<ExceptionRequestRecord[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : ""
  const res = await fetch(`${API_BASE}/api/v1/finance/exception-requests${qs}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function createExceptionRequest(actorUserId: string, payload: any): Promise<ExceptionRequestRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/exception-requests`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
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
    body: JSON.stringify(payload),
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
  return handleResponse(res)
}

export async function getCashAdvanceDetail(actorUserId: string, advanceId: string): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function createCashAdvance(actorUserId: string, payload: any): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function submitCashAdvance(actorUserId: string, advanceId: string): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/submit`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
  })
  return handleResponse(res)
}

export async function decideCashAdvance(
  actorUserId: string,
  advanceId: string,
  payload: { decision: "approved" | "rejected" | "returned_for_correction" | "cancelled"; comments: string; approved_amount_minor?: number }
): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/decide`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function recordCashReturn(
  actorUserId: string,
  advanceId: string,
  payload: { returned_amount_minor: number; bank_reference: string; evidence_document_id: string; notes?: string }
): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/cash-return`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function closeCashAdvance(
  actorUserId: string,
  advanceId: string,
  payload: { closure_reason: string; authorized_exception_id?: string }
): Promise<CashAdvanceRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/close`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function createDisbursement(
  actorUserId: string,
  advanceId: string,
  payload: { payment_amount_minor: number; payment_method?: string; recipient_account_ref?: string }
): Promise<DisbursementRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/cash-advances/${advanceId}/disbursements`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function verifyDisbursement(actorUserId: string, disbursementId: string, notes = "Verified payee and details") {
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/verify`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return handleResponse<DisbursementRecord>(res)
}

export async function approveDisbursementBankRelease(actorUserId: string, disbursementId: string, notes = "Authorized release") {
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/bank-approve`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return handleResponse<DisbursementRecord>(res)
}

export async function executeDisbursement(
  actorUserId: string,
  disbursementId: string,
  payload: {
    bank_reference: string
    executed_amount_minor: number
    evidence_document_id: string
  }
) {
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/execute`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse<DisbursementRecord>(res)
}

export async function verifyDisbursementEvidence(actorUserId: string, disbursementId: string, notes = "Remittance evidence verified") {
  const res = await fetch(`${API_BASE}/api/v1/finance/disbursements/${disbursementId}/verify-evidence`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return handleResponse<DisbursementRecord>(res)
}

export async function fetchExpenseReports(actorUserId: string): Promise<ExpenseReportRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function getExpenseReportDetail(actorUserId: string, reportId: string): Promise<ExpenseReportRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function createExpenseReport(actorUserId: string, payload: any): Promise<ExpenseReportRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function addExpenseLine(actorUserId: string, reportId: string, payload: any): Promise<ExpenseReportRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}/lines`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function evaluateExpenseReport(actorUserId: string, reportId: string): Promise<PolicyEvaluationResult> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}/evaluate`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
  })
  return handleResponse(res)
}

export async function submitExpenseReport(actorUserId: string, reportId: string, confirmed = true): Promise<ExpenseReportRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/expense-reports/${reportId}/submit`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ employee_declaration_confirmed: confirmed }),
  })
  return handleResponse(res)
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
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function fetchVendors(actorUserId: string): Promise<VendorRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendors`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function createVendor(actorUserId: string, payload: any): Promise<VendorRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendors`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function verifyVendor(actorUserId: string, vendorId: string, notes = "KYC verified"): Promise<VendorRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/vendors/${vendorId}/verify`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ decision: "verified", notes }),
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
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function fetchPurchaseRequests(actorUserId: string): Promise<PurchaseRequestRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/purchase-requests`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function createPurchaseRequest(actorUserId: string, payload: any): Promise<PurchaseRequestRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/purchase-requests`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function submitPurchaseRequest(actorUserId: string, prId: string): Promise<PurchaseRequestRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/purchase-requests/${prId}/submit`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
  })
  return handleResponse(res)
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
    body: JSON.stringify({ decision, comments }),
  })
  return handleResponse(res)
}

export async function fetchAccountsPayable(actorUserId: string): Promise<AccountPayableRecord[]> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payables`, {
    headers: buildHeaders(actorUserId, false),
    cache: "no-store",
  })
  return handleResponse(res)
}

export async function validateAccountPayable(actorUserId: string, payload: any): Promise<AccountPayableRecord> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payables`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function preparePayablePayment(actorUserId: string, payableId: string, payload: any): Promise<any> {
  const res = await fetch(`${API_BASE}/api/v1/finance/payables/${payableId}/payments`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function verifyPayablePayment(actorUserId: string, paymentId: string, notes = "Verified payment details") {
  const res = await fetch(`${API_BASE}/api/v1/finance/payment-records/${paymentId}/verify`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify({ notes }),
  })
  return handleResponse(res)
}

export async function approvePayablePaymentBank(actorUserId: string, paymentId: string, notes = "Authorized payable payment") {
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
) {
  const res = await fetch(`${API_BASE}/api/v1/finance/payment-records/${paymentId}/execute`, {
    method: "POST",
    headers: buildHeaders(actorUserId),
    body: JSON.stringify(payload),
  })
  return handleResponse(res)
}

export async function reconcilePayablePayment(actorUserId: string, paymentId: string, notes = "Reconciled") {
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
  return handleResponse(res)
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
    body: JSON.stringify(payload),
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


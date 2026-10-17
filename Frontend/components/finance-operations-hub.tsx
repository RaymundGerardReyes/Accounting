"use client"

import React, { useState, useEffect, useMemo } from "react"
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { CurrencyDisplay, FinancialStatusBadge } from "@/components/ui/financial-primitives"
import {
  Landmark,
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
  AlertTriangle,
  Download,
  Upload,
  UserCheck,
  Send,
  Eye,
  Check,
  ChevronRight,
  Receipt,
  Building2,
  FileSpreadsheet,
  FileCheck,
  Scale,
  Lock,
} from "lucide-react"

import {
  formatPhpMinor,
  FinanceUser,
  CashAdvanceRecord,
  ExpenseReportRecord,
  PolicyVersionRecord,
  FinancialPolicyRecord,
  ExceptionRequestRecord,
  VendorRecord,
  TaxCodeRecord,
  PurchaseRequestRecord,
  AccountPayableRecord,
  CentralizedTrackerRow,
  fetchFinanceMe,
  switchDevActor,
  fetchFinanceDashboard,
  fetchPolicies,
  createPolicyVersion,
  submitPolicyVersion,
  approvePolicyVersion,
  fetchExceptionRequests,
  createExceptionRequest,
  decideExceptionRequest,
  fetchCashAdvances,
  createCashAdvance,
  submitCashAdvance,
  decideCashAdvance,
  recordCashReturn,
  closeCashAdvance,
  createDisbursement,
  verifyDisbursement,
  approveDisbursementBankRelease,
  executeDisbursement,
  verifyDisbursementEvidence,
  fetchExpenseReports,
  createExpenseReport,
  addExpenseLine,
  evaluateExpenseReport,
  submitExpenseReport,
  reviewExpenseReport,
  fetchVendors,
  createVendor,
  verifyVendor,
  fetchTaxCodes,
  fetchPurchaseRequests,
  createPurchaseRequest,
  submitPurchaseRequest,
  decidePurchaseRequest,
  fetchAccountsPayable,
  validateAccountPayable,
  preparePayablePayment,
  verifyPayablePayment,
  approvePayablePaymentBank,
  executePayablePayment,
  reconcilePayablePayment,
  fetchCentralizedTracker,
  downloadTrackerCsv,
  fetchExceptionRegister,
  resolveExceptionRegisterItem,
  fetchAuditEvents,
  uploadDocumentJson,
} from "@/lib/finance-api"

export function FinanceOperationsHub() {
  // Navigation tabs
  const [activeTab, setActiveTab] = useState<
    "overview" | "advances" | "expenses" | "p2p" | "policies" | "tracker"
  >("overview")

  // Authentication & Actor Context
  const [currentActorId, setCurrentActorId] = useState<string>("emp_field_01")
  const [actorProfile, setActorProfile] = useState<any>(null)
  const [availableUsers, setAvailableUsers] = useState<FinanceUser[]>([])
  const [actAsReason, setActAsReason] = useState<string>("Interactive QA review")

  // Data states
  const [dashboardData, setDashboardData] = useState<any>(null)
  const [advances, setAdvances] = useState<CashAdvanceRecord[]>([])
  const [expenses, setExpenses] = useState<ExpenseReportRecord[]>([])
  const [vendors, setVendors] = useState<VendorRecord[]>([])
  const [taxCodes, setTaxCodes] = useState<TaxCodeRecord[]>([])
  const [purchaseRequests, setPurchaseRequests] = useState<PurchaseRequestRecord[]>([])
  const [payables, setPayables] = useState<AccountPayableRecord[]>([])
  const [policies, setPolicies] = useState<FinancialPolicyRecord[]>([])
  const [exceptions, setExceptions] = useState<ExceptionRequestRecord[]>([])
  const [trackerRows, setTrackerRows] = useState<CentralizedTrackerRow[]>([])
  const [exceptionRegister, setExceptionRegister] = useState<any[]>([])
  const [auditEventsData, setAuditEventsData] = useState<{ chain_integrity?: any; events: any[] }>({ events: [] })

  // UI state
  const [loading, setLoading] = useState<boolean>(true)
  const [actionLoading, setActionLoading] = useState<boolean>(false)
  const [feedback, setFeedback] = useState<{ type: "success" | "error" | "info"; message: string } | null>(null)
  const [trackerSearch, setTrackerSearch] = useState<string>("")
  const [trackerWorkflowFilter, setTrackerWorkflowFilter] = useState<string>("all")

  // Modals & Drawers
  const [showAdvanceModal, setShowAdvanceModal] = useState<boolean>(false)
  const [showDisburseModal, setShowDisburseModal] = useState<boolean>(false)
  const [selectedAdvanceForDisburse, setSelectedAdvanceForDisburse] = useState<CashAdvanceRecord | null>(null)
  const [showCashReturnModal, setShowCashReturnModal] = useState<boolean>(false)
  const [selectedAdvanceForReturn, setSelectedAdvanceForReturn] = useState<CashAdvanceRecord | null>(null)

  const [showExpenseModal, setShowExpenseModal] = useState<boolean>(false)
  const [selectedExpenseReport, setSelectedExpenseReport] = useState<ExpenseReportRecord | null>(null)
  const [showAddLineModal, setShowAddLineModal] = useState<boolean>(false)
  const [policyEvalResult, setPolicyEvalResult] = useState<any>(null)
  const [evaluatingReportId, setEvaluatingReportId] = useState<string | null>(null)

  const [showVendorModal, setShowVendorModal] = useState<boolean>(false)
  const [showPrModal, setShowPrModal] = useState<boolean>(false)
  const [showPayableModal, setShowPayableModal] = useState<boolean>(false)
  const [showPolicyModal, setShowPolicyModal] = useState<boolean>(false)
  const [showExceptionModal, setShowExceptionModal] = useState<boolean>(false)

  // Form states: New Cash Advance
  const [advPurpose, setAdvPurpose] = useState<string>("Field visit supplies & client travel")
  const [advAmountPhp, setAdvAmountPhp] = useState<string>("12000")
  const [advDateNeeded, setAdvDateNeeded] = useState<string>("2026-05-10")
  const [advSite, setAdvSite] = useState<string>("Laguna Industrial Zone")

  // Form states: Disbursement maker
  const [disbAmountPhp, setDisbAmountPhp] = useState<string>("")
  const [disbAccountRef, setDisbAccountRef] = useState<string>("BDO-SA-0089211029")
  const [disbBankRef, setDisbBankRef] = useState<string>("TXN-BNK-990214")

  // Form states: Cash Return
  const [returnAmountPhp, setReturnAmountPhp] = useState<string>("1500")
  const [returnBankRef, setReturnBankRef] = useState<string>("RET-DEP-77881")

  // Form states: New Expense Report
  const [expPeriod, setExpPeriod] = useState<string>("2026-05")
  const [expTitle, setExpTitle] = useState<string>("May 2026 Field Staff Operational Expenses")
  const [expOutOfBase, setExpOutOfBase] = useState<boolean>(true)
  const [expDeclaration, setExpDeclaration] = useState<boolean>(true)

  // Form states: Add Expense Line
  const [lineCategory, setLineCategory] = useState<string>("transportation")
  const [lineMerchant, setLineMerchant] = useState<string>("Victory Liner / Local Transit")
  const [linePurpose, setLinePurpose] = useState<string>("Travel to provincial manufacturing plant")
  const [lineAmountPhp, setLineAmountPhp] = useState<string>("3500")
  const [lineReceiptNo, setLineReceiptNo] = useState<string>("OR-88219")
  const [lineLocation, setLineLocation] = useState<string>("Laguna Plant Site")

  // Form states: Vendor
  const [vendorName, setVendorName] = useState<string>("")
  const [vendorType, setVendorType] = useState<string>("supplier")
  const [vendorTin, setVendorTin] = useState<string>("009-123-456-000")

  // Form states: PR
  const [prVendorId, setPrVendorId] = useState<string>("")
  const [prCategory, setPrCategory] = useState<string>("supplier_invoice")
  const [prPurpose, setPrPurpose] = useState<string>("Industrial maintenance supplies and safety equipment")
  const [prAmountPhp, setPrAmountPhp] = useState<string>("25000")

  // Form states: Payable
  const [apPrId, setApPrId] = useState<string>("")
  const [apInvoiceNo, setApInvoiceNo] = useState<string>("INV-2026-9011")
  const [apInvoiceDate, setApInvoiceDate] = useState<string>("2026-05-15")
  const [apDueDate, setApDueDate] = useState<string>("2026-05-30")
  const [apOriginalPhp, setApOriginalPhp] = useState<string>("25000")
  const [apVatCode, setApVatCode] = useState<string>("VAT12")
  const [apEwtCode, setApEwtCode] = useState<string>("EWT1")

  // Form states: Policy version
  const [polEffectiveFrom, setPolEffectiveFrom] = useState<string>("2026-06-01")
  const [polLimitPhp, setPolLimitPhp] = useState<string>("20000")
  const [polJustification, setPolJustification] = useState<string>("Quarterly inflation adjustment for provincial field travel")

  // Form states: Exception request
  const [excRuleCode, setExcRuleCode] = useState<string>("MONTHLY_AGGREGATE_CAP")
  const [excAmountPhp, setExcAmountPhp] = useState<string>("5000")
  const [excPeriod, setExcPeriod] = useState<string>("2026-05")
  const [excJustification, setExcJustification] = useState<string>("Emergency provincial travel exceeding monthly baseline threshold")

  // Load All Primary Data
  const loadData = async (actorId = currentActorId) => {
    setLoading(true)
    try {
      const [
        meRes,
        dashRes,
        advRes,
        expRes,
        venRes,
        taxRes,
        prRes,
        apRes,
        polRes,
        excRes,
        trackRes,
        excRegRes,
        auditRes,
      ] = await Promise.all([
        fetchFinanceMe(actorId).catch(() => null),
        fetchFinanceDashboard(actorId).catch(() => null),
        fetchCashAdvances(actorId).catch(() => []),
        fetchExpenseReports(actorId).catch(() => []),
        fetchVendors(actorId).catch(() => []),
        fetchTaxCodes(actorId).catch(() => []),
        fetchPurchaseRequests(actorId).catch(() => []),
        fetchAccountsPayable(actorId).catch(() => []),
        fetchPolicies(actorId).catch(() => []),
        fetchExceptionRequests(actorId).catch(() => []),
        fetchCentralizedTracker(actorId).catch(() => []),
        fetchExceptionRegister(actorId).catch(() => []),
        fetchAuditEvents(actorId).catch(() => ({ events: [] })),
      ])

      if (meRes) {
        setActorProfile(meRes.actor)
        if (meRes.users && meRes.users.length > 0) {
          setAvailableUsers(meRes.users)
        }
      }
      setDashboardData(dashRes)
      setAdvances(advRes)
      setExpenses(expRes)
      setVendors(venRes)
      if (venRes.length > 0 && !prVendorId) {
        setPrVendorId(venRes[0].id)
      }
      setTaxCodes(taxRes)
      setPurchaseRequests(prRes)
      if (prRes.length > 0 && !apPrId) {
        setApPrId(prRes[0].id)
      }
      setPayables(apRes)
      setPolicies(polRes)
      setExceptions(excRes)
      setTrackerRows(trackRes)
      setExceptionRegister(excRegRes)
      setAuditEventsData(auditRes)
    } catch (err: any) {
      console.error("Finance data load error:", err)
      setFeedback({ type: "error", message: err.message || "Failed to load finance data." })
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(currentActorId)
  }, [currentActorId])

  // Actor Role Switcher Handler
  const handleSwitchActor = async (newActorId: string) => {
    try {
      setActionLoading(true)
      await switchDevActor(currentActorId, newActorId, actAsReason)
      setCurrentActorId(newActorId)
      setFeedback({ type: "success", message: `Switched identity to ${newActorId} with full server-bound role resolution.` })
      await loadData(newActorId)
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message || "Failed to switch actor." })
    } finally {
      setActionLoading(false)
    }
  }

  // Upload Fake Base64 Document Evidence Helper
  const createMockDocument = async (docType: string, filename: string): Promise<string> => {
    const raw = `%PDF-1.4\n% CPA Evidence: ${filename} for ${currentActorId}\n1 0 obj <<>> endobj\n%%EOF\n`
    const b64 = typeof window !== "undefined" ? btoa(raw) : ""
    const doc = await uploadDocumentJson(currentActorId, {
      original_filename: filename,
      document_type: docType,
      content_base64: b64,
    })
    return doc.id
  }

  // Cash Advance Handlers
  const handleCreateAdvance = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      setActionLoading(true)
      const minor = Math.round(parseFloat(advAmountPhp) * 100)
      const created = await createCashAdvance(currentActorId, {
        purpose_code: "field_travel_operations",
        business_justification: advPurpose,
        requested_amount_minor: minor,
        date_needed: advDateNeeded,
        destination_or_site: advSite,
        submit_now: false,
      })
      setFeedback({ type: "success", message: `Cash Advance ${created.advance_number} created in draft status.` })
      setShowAdvanceModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleSubmitAdvance = async (advId: string) => {
    try {
      setActionLoading(true)
      const res = await submitCashAdvance(currentActorId, advId)
      setFeedback({ type: "success", message: `Cash advance ${res.advance_number} submitted for approval.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleDecideAdvance = async (advId: string, decision: "approved" | "rejected") => {
    try {
      setActionLoading(true)
      const res = await decideCashAdvance(currentActorId, advId, {
        decision,
        comments: decision === "approved" ? "Authorized within approval limit" : "Rejected",
      })
      setFeedback({ type: "success", message: `Cash advance ${res.advance_number} ${decision}.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  // 6-Person Disbursement Step Handlers
  const handleMakerPrepareDisbursement = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedAdvanceForDisburse) return
    try {
      setActionLoading(true)
      const minor = Math.round(parseFloat(disbAmountPhp || "0") * 100)
      const disb = await createDisbursement(currentActorId, selectedAdvanceForDisburse.id, {
        payment_amount_minor: minor,
        payment_method: "bank_transfer",
        recipient_account_ref: disbAccountRef,
      })
      setFeedback({ type: "success", message: `Disbursement ${disb.disbursement_number} prepared by maker.` })
      setShowDisburseModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleVerifyDisbursement = async (disbId: string) => {
    try {
      setActionLoading(true)
      const res = await verifyDisbursement(currentActorId, disbId, "Bank details & payee verified independently")
      setFeedback({ type: "success", message: `Disbursement ${res.disbursement_number} verified.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleBankApproveDisbursement = async (disbId: string) => {
    try {
      setActionLoading(true)
      const res = await approveDisbursementBankRelease(currentActorId, disbId, "Bank release authorized")
      setFeedback({ type: "success", message: `Disbursement ${res.disbursement_number} bank-approved.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleExecuteDisbursement = async (disbId: string, amountMinor: number) => {
    try {
      setActionLoading(true)
      const evidenceDocId = await createMockDocument("bank_remittance_advice", "bank_remittance.pdf")
      const res = await executeDisbursement(currentActorId, disbId, {
        bank_reference: disbBankRef || "REF-BNK-771120",
        executed_amount_minor: amountMinor,
        evidence_document_id: evidenceDocId,
      })
      setFeedback({ type: "success", message: `Disbursement ${res.disbursement_number} executed via bank transfer.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleVerifyEvidenceDisbursement = async (disbId: string) => {
    try {
      setActionLoading(true)
      const res = await verifyDisbursementEvidence(currentActorId, disbId, "Remittance PDF validated by 6th actor")
      setFeedback({ type: "success", message: `Disbursement evidence verified! Cash advance released.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleRecordCashReturn = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedAdvanceForReturn) return
    try {
      setActionLoading(true)
      const docId = await createMockDocument("cash_deposit_slip", "cash_return_slip.pdf")
      const minor = Math.round(parseFloat(returnAmountPhp) * 100)
      const res = await recordCashReturn(currentActorId, selectedAdvanceForReturn.id, {
        returned_amount_minor: minor,
        bank_reference: returnBankRef,
        evidence_document_id: docId,
        notes: "Excess advance cash returned to company bank account",
      })
      setFeedback({ type: "success", message: `Recorded cash return for ${res.advance_number}.` })
      setShowCashReturnModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleCloseAdvance = async (advId: string) => {
    try {
      setActionLoading(true)
      const res = await closeCashAdvance(currentActorId, advId, {
        closure_reason: "Reconciliation verified by Controller. Zero balance achieved.",
      })
      setFeedback({ type: "success", message: `Cash advance ${res.advance_number} successfully closed and archived.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  // Expense Report Handlers
  const handleCreateExpenseReport = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      setActionLoading(true)
      const orDocId = await createMockDocument("consolidated_receipt_pdf", "consolidated_official_receipts.pdf")
      const mfarDocId = expOutOfBase
        ? await createMockDocument("mfar", "monthly_field_activity_report.pdf")
        : undefined

      const rep = await createExpenseReport(currentActorId, {
        reporting_period: expPeriod,
        business_purpose_summary: expTitle,
        out_of_base_activity: expOutOfBase,
        consolidated_receipt_doc_id: orDocId,
        mfar_doc_id: mfarDocId,
        employee_declaration_confirmed: expDeclaration,
      })
      setFeedback({ type: "success", message: `Expense report ${rep.report_number} created with attached PDF evidence.` })
      setShowExpenseModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleAddExpenseLine = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedExpenseReport) return
    try {
      setActionLoading(true)
      const minor = Math.round(parseFloat(lineAmountPhp) * 100)
      const updated = await addExpenseLine(currentActorId, selectedExpenseReport.id, {
        transaction_date: `${expPeriod}-05`,
        expense_category: lineCategory,
        business_purpose: linePurpose,
        merchant_or_payee: lineMerchant,
        claimed_amount_minor: minor,
        receipt_or_invoice_no: lineReceiptNo,
        school_or_destination: lineLocation,
        out_of_base: true,
      })
      setFeedback({ type: "success", message: `Expense line added to ${updated.report_number}.` })
      setSelectedExpenseReport(updated)
      setShowAddLineModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleEvaluatePolicyPreview = async (reportId: string) => {
    try {
      setEvaluatingReportId(reportId)
      const evalResult = await evaluateExpenseReport(currentActorId, reportId)
      setPolicyEvalResult(evalResult)
      setFeedback({
        type: evalResult.overall_passed ? "success" : "info",
        message: `Policy evaluation complete: ${evalResult.overall_passed ? "All policy rules passed!" : "Breaches or warnings detected."}`,
      })
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setEvaluatingReportId(null)
    }
  }

  const handleSubmitExpenseReport = async (reportId: string) => {
    try {
      setActionLoading(true)
      const res = await submitExpenseReport(currentActorId, reportId, true)
      setFeedback({ type: "success", message: `Expense report ${res.report_number} submitted to Finance.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleReviewReport = async (report: ExpenseReportRecord, decision: "approved" | "partially_approved") => {
    try {
      setActionLoading(true)
      const lineDecisions = (report.lines || []).map((l, idx) => ({
        line_id: l.id,
        line_status: (decision === "partially_approved" && idx === 0 ? "partially_approved" : "approved") as
          | "approved"
          | "partially_approved"
          | "rejected",
        approved_amount_minor:
          decision === "partially_approved" && idx === 0
            ? Math.max(0, l.amount_minor - 150000)
            : l.amount_minor,
        rejection_reason:
          decision === "partially_approved" && idx === 0 ? "Non-compliant personal expense disallowed" : undefined,
      }))

      const reviewed = await reviewExpenseReport(currentActorId, report.id, {
        report_decision: decision,
        review_notes: `Reviewed and verified by Finance. Applied oldest-first to open advances.`,
        line_decisions: lineDecisions,
      })
      setFeedback({
        type: "success",
        message: `Expense report ${reviewed.report_number} reviewed and settled oldest-first against open advances!`,
      })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  // P2P: Vendor & PR & Payable Handlers
  const handleCreateVendor = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      setActionLoading(true)
      const v = await createVendor(currentActorId, {
        registered_name: vendorName,
        payee_type: vendorType,
        tin: vendorTin,
        payment_method: "bank_transfer",
      })
      setFeedback({ type: "success", message: `Vendor ${v.registered_name} registered (pending verification).` })
      setShowVendorModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleVerifyVendor = async (vendorId: string) => {
    try {
      setActionLoading(true)
      const v = await verifyVendor(currentActorId, vendorId, "TIN & KYC documentation verified")
      setFeedback({ type: "success", message: `Vendor ${v.registered_name} verified for payments.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleCreatePr = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      setActionLoading(true)
      const minor = Math.round(parseFloat(prAmountPhp) * 100)
      const pr = await createPurchaseRequest(currentActorId, {
        vendor_id: prVendorId,
        payment_category: prCategory,
        business_purpose: prPurpose,
        requested_amount_minor: minor,
        requested_payment_date: "2026-05-25",
        submit_now: true,
      })
      setFeedback({ type: "success", message: `Purchase Request ${pr.request_number} created and submitted.` })
      setShowPrModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleApprovePr = async (prId: string) => {
    try {
      setActionLoading(true)
      const res = await decidePurchaseRequest(currentActorId, prId, "approved", "Budget checked and authorized")
      setFeedback({ type: "success", message: `Purchase Request ${res.request_number} approved.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleValidatePayable = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      setActionLoading(true)
      const minor = Math.round(parseFloat(apOriginalPhp) * 100)
      const ap = await validateAccountPayable(currentActorId, {
        purchase_request_id: apPrId,
        invoice_number: apInvoiceNo,
        invoice_date: apInvoiceDate,
        due_date: apDueDate,
        original_payable_minor: minor,
        vat_code: apVatCode,
        ewt_code: apEwtCode,
        payment_terms: "Net 30",
      })
      setFeedback({ type: "success", message: `Account Payable ${ap.payable_number} validated with tax calculations.` })
      setShowPayableModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  // Policy & Exception Handlers
  const handleCreatePolicyVersion = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      setActionLoading(true)
      const minor = Math.round(parseFloat(polLimitPhp) * 100)
      const ver = await createPolicyVersion(currentActorId, "FIELD_LIQUIDATION_POLICY", {
        effective_from: polEffectiveFrom,
        aggregate_monthly_limit_minor: minor,
        max_reports_per_period: 1,
        submission_deadline_day_of_next_month: 2,
        finance_sla_days: 5,
        require_consolidated_receipt_pdf: true,
        require_mfar_for_out_of_base: true,
        allow_partial_line_approval: true,
        block_new_advance_when_overdue: true,
        change_justification: polJustification,
      })
      setFeedback({ type: "success", message: `Draft Policy Version v${ver.version_number} authored.` })
      setShowPolicyModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleApprovePolicyVersion = async (versionId: string) => {
    try {
      setActionLoading(true)
      await approvePolicyVersion(currentActorId, versionId, "Approved by Finance Controller")
      setFeedback({ type: "success", message: `Policy version activated! Author != Approver rule verified.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleCreateException = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      setActionLoading(true)
      const minor = Math.round(parseFloat(excAmountPhp) * 100)
      const exc = await createExceptionRequest(currentActorId, {
        reporting_period: excPeriod,
        violated_rule_code: excRuleCode,
        exception_type: "amount_cap_override",
        requested_amount_minor: minor,
        business_justification: excJustification,
        submit_immediately: true,
      })
      setFeedback({ type: "success", message: `Exception Request ${exc.exception_number} submitted to Controller.` })
      setShowExceptionModal(false)
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  const handleDecideException = async (excId: string, decision: "approved" | "rejected") => {
    try {
      setActionLoading(true)
      const res = await decideExceptionRequest(currentActorId, excId, {
        decision,
        decision_reason: decision === "approved" ? "Special provincial project authorized" : "Unjustified",
      })
      setFeedback({ type: "success", message: `Exception request ${res.exception_number} ${decision}.` })
      await loadData()
    } catch (err: any) {
      setFeedback({ type: "error", message: err.message })
    } finally {
      setActionLoading(false)
    }
  }

  // Filtered Centralized Tracker Rows
  const filteredTrackerRows = useMemo(() => {
    return trackerRows.filter((r) => {
      const matchWf = trackerWorkflowFilter === "all" || r.entity_type === trackerWorkflowFilter
      const matchSearch =
        !trackerSearch ||
        r.reference_number.toLowerCase().includes(trackerSearch.toLowerCase()) ||
        r.employee_or_vendor.toLowerCase().includes(trackerSearch.toLowerCase()) ||
        r.category.toLowerCase().includes(trackerSearch.toLowerCase())
      return matchWf && matchSearch
    })
  }, [trackerRows, trackerWorkflowFilter, trackerSearch])

  return (
    <div className="flex-1 space-y-6 p-8 pt-6 min-h-screen bg-background text-foreground">
      {/* Top Banner: Actor Identity & Dev Act-As Switcher */}
      <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4 p-4 rounded-xl bg-card border border-border shadow-xs">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-lg bg-primary text-primary-foreground flex items-center justify-center font-bold text-sm tracking-tight shadow-xs">
            {actorProfile?.roles?.[0]?.slice(0, 2).toUpperCase() || "AC"}
          </div>
          <div>
            <div className="flex items-center gap-2">
              <span className="font-semibold text-sm">{actorProfile?.full_name || currentActorId}</span>
              <Badge variant="outline" className="text-[10px] font-mono uppercase bg-muted/60">
                {actorProfile?.employee_code || "EMP"}
              </Badge>
              <Badge variant="secondary" className="text-[10px] uppercase font-semibold">
                {actorProfile?.primary_role || "employee"}
              </Badge>
            </div>
            <div className="text-xs text-muted-foreground flex items-center gap-2 mt-0.5">
              <span>Dept: {actorProfile?.department_id || "All"}</span>
              <span>•</span>
              <span>Scope: {actorProfile?.staff_type || "field"}</span>
              {actorProfile?.approval_limits?.length > 0 && (
                <>
                  <span>•</span>
                  <span className="text-accent font-medium">
                    Limit: {formatPhpMinor(actorProfile.approval_limits[0].max_amount_minor)}
                  </span>
                </>
              )}
            </div>
          </div>
        </div>

        {/* Server-bound Dev Role Switcher */}
        <div className="flex items-center gap-2 w-full md:w-auto">
          <div className="flex flex-col">
            <span className="text-[10px] uppercase font-semibold text-muted-foreground tracking-wider">
              Dev Mode Act-As Switcher
            </span>
            <div className="flex items-center gap-2 mt-1">
              <select
                value={currentActorId}
                onChange={(e) => handleSwitchActor(e.target.value)}
                disabled={actionLoading}
                className="h-8 text-xs rounded-md border border-input bg-background px-2.5 py-1 font-medium focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              >
                <option value="emp_field_01">Juan dela Cruz (Field Staff - Mfg)</option>
                <option value="emp_field_02">Maria Clara Santos (Field Staff - Log)</option>
                <option value="sup_approver_01">Roberto Reyes (Supervisor)</option>
                <option value="fin_reviewer_01">Elena Gomez (Finance Reviewer)</option>
                <option value="fin_maker_01">Carlos Mendoza (Disbursement Maker)</option>
                <option value="fin_verifier_01">Diana Lim (Disbursement Verifier)</option>
                <option value="bank_approver_01">Arthur Tan (Bank Approver)</option>
                <option value="evidence_verifier_01">Arthur Tan 2 (Evidence Verifier)</option>
                <option value="fin_controller_01">Victoria Sy, CPA (Finance Controller)</option>
                <option value="auditor_01">Gabriel Ramos, CIA (Auditor)</option>
                <option value="sys_admin_01">System Admin</option>
              </select>

              <Button
                variant="outline"
                size="sm"
                onClick={() => loadData()}
                disabled={loading || actionLoading}
                className="h-8 gap-1.5 text-xs"
              >
                <RefreshCw className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`} />
                Refresh
              </Button>
            </div>
          </div>
        </div>
      </div>

      {/* Feedback Alert Bar */}
      {feedback && (
        <div
          className={`flex items-center justify-between p-3 rounded-lg border text-xs font-medium ${
            feedback.type === "success"
              ? "bg-emerald-50 text-emerald-800 border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800"
              : feedback.type === "error"
              ? "bg-rose-50 text-rose-800 border-rose-200 dark:bg-rose-950/40 dark:text-rose-300 dark:border-rose-800"
              : "bg-blue-50 text-blue-800 border-blue-200 dark:bg-blue-950/40 dark:text-blue-300 dark:border-blue-800"
          }`}
        >
          <div className="flex items-center gap-2">
            {feedback.type === "success" && <CheckCircle2 className="w-4 h-4 shrink-0" />}
            {feedback.type === "error" && <AlertTriangle className="w-4 h-4 shrink-0" />}
            {feedback.type === "info" && <ShieldCheck className="w-4 h-4 shrink-0" />}
            <span>{feedback.message}</span>
          </div>
          <button
            onClick={() => setFeedback(null)}
            className="text-muted-foreground hover:text-foreground text-xs p-1"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>
      )}

      {/* Main Tab Navigation */}
      <div className="flex items-center justify-between border-b border-border pb-2">
        <div className="flex items-center gap-2">
          <Button
            variant={activeTab === "overview" ? "default" : "ghost"}
            size="sm"
            onClick={() => setActiveTab("overview")}
            className="text-xs h-8 gap-2"
          >
            <Activity className="w-3.5 h-3.5" />
            Dashboard & KPIs
          </Button>

          <Button
            variant={activeTab === "advances" ? "default" : "ghost"}
            size="sm"
            onClick={() => setActiveTab("advances")}
            className="text-xs h-8 gap-2"
          >
            <Landmark className="w-3.5 h-3.5" />
            Cash Advances & SoD ({advances.length})
          </Button>

          <Button
            variant={activeTab === "expenses" ? "default" : "ghost"}
            size="sm"
            onClick={() => setActiveTab("expenses")}
            className="text-xs h-8 gap-2"
          >
            <Receipt className="w-3.5 h-3.5" />
            Expense Reports ({expenses.length})
          </Button>

          <Button
            variant={activeTab === "p2p" ? "default" : "ghost"}
            size="sm"
            onClick={() => setActiveTab("p2p")}
            className="text-xs h-8 gap-2"
          >
            <Building2 className="w-3.5 h-3.5" />
            Purchase-to-Pay ({payables.length})
          </Button>

          <Button
            variant={activeTab === "policies" ? "default" : "ghost"}
            size="sm"
            onClick={() => setActiveTab("policies")}
            className="text-xs h-8 gap-2"
          >
            <Scale className="w-3.5 h-3.5" />
            Policies & Exceptions ({exceptions.length})
          </Button>

          <Button
            variant={activeTab === "tracker" ? "default" : "ghost"}
            size="sm"
            onClick={() => setActiveTab("tracker")}
            className="text-xs h-8 gap-2"
          >
            <FileSpreadsheet className="w-3.5 h-3.5" />
            Centralized Tracker & Audit
          </Button>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => downloadTrackerCsv(currentActorId)}
            className="text-xs h-8 gap-1.5"
          >
            <Download className="w-3.5 h-3.5" />
            Export Audit CSV (BOM)
          </Button>
        </div>
      </div>

      {/* TAB 1: OVERVIEW & KPIS */}
      {activeTab === "overview" && (
        <div className="space-y-6">
          {/* Executive KPI Cards */}
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
            <Card className="shadow-xs">
              <CardHeader className="pb-2">
                <CardDescription className="text-xs uppercase font-medium">Open Advances Balance</CardDescription>
                <CardTitle className="text-2xl font-bold tracking-tight">
                  {formatPhpMinor(dashboardData?.kpis?.open_advances?.aggregate_balance_minor || 0)}
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="text-xs text-muted-foreground flex items-center justify-between">
                  <span>Count: {dashboardData?.kpis?.open_advances?.active_count || advances.length}</span>
                  <Badge variant="outline" className="text-[10px]">
                    Unliquidated: {dashboardData?.kpis?.open_advances?.unliquidated_count || 0}
                  </Badge>
                </div>
              </CardContent>
            </Card>

            <Card className="shadow-xs">
              <CardHeader className="pb-2">
                <CardDescription className="text-xs uppercase font-medium">Overdue Advances (&gt;30 Days)</CardDescription>
                <CardTitle className="text-2xl font-bold tracking-tight text-rose-600 dark:text-rose-400">
                  {formatPhpMinor(dashboardData?.kpis?.open_advances?.overdue_balance_minor || 0)}
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="text-xs text-muted-foreground flex items-center justify-between">
                  <span>Overdue items: {dashboardData?.kpis?.open_advances?.overdue_count || 0}</span>
                  <span className="text-[10px] text-rose-500 font-medium">New Advance Blocked</span>
                </div>
              </CardContent>
            </Card>

            <Card className="shadow-xs">
              <CardHeader className="pb-2">
                <CardDescription className="text-xs uppercase font-medium">Accounts Payable Due</CardDescription>
                <CardTitle className="text-2xl font-bold tracking-tight">
                  {formatPhpMinor(dashboardData?.kpis?.accounts_payable?.total_net_payable_minor || 0)}
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="text-xs text-muted-foreground flex items-center justify-between">
                  <span>Open Payables: {dashboardData?.kpis?.accounts_payable?.open_payables_count || payables.length}</span>
                  <Badge variant="secondary" className="text-[10px]">
                    Tax Review: {dashboardData?.kpis?.accounts_payable?.tax_review_required_count || 0}
                  </Badge>
                </div>
              </CardContent>
            </Card>

            <Card className="shadow-xs">
              <CardHeader className="pb-2">
                <CardDescription className="text-xs uppercase font-medium">Audit Trail Security</CardDescription>
                <CardTitle className="text-lg font-bold flex items-center gap-2 text-emerald-600 dark:text-emerald-400">
                  <ShieldCheck className="w-5 h-5" />
                  {auditEventsData?.chain_integrity?.is_valid !== false ? "SHA-256 Valid" : "Tamper Alert"}
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="text-xs text-muted-foreground flex items-center justify-between">
                  <span>Events logged: {auditEventsData?.chain_integrity?.checked_count || auditEventsData?.events?.length || 0}</span>
                  <span className="text-[10px] font-mono text-muted-foreground">Append-Only</span>
                </div>
              </CardContent>
            </Card>
          </div>

          {/* Operational Exception Register */}
          <Card className="shadow-xs">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold flex items-center gap-2">
                  <AlertTriangle className="w-4 h-4 text-amber-500" />
                  Operational Exception Register & Shortfalls
                </CardTitle>
                <CardDescription className="text-xs mt-1">
                  CPA-tracked operational exceptions, disallowance shortfalls, and unverified vendor payments.
                </CardDescription>
              </div>
              <Badge variant="outline" className="text-xs font-mono">
                {exceptionRegister.length} Active Exceptions
              </Badge>
            </CardHeader>
            <CardContent>
              {exceptionRegister.length === 0 ? (
                <div className="text-center py-6 text-xs text-muted-foreground">
                  No active exceptions in the register. All operations within policy boundaries.
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs">
                    <thead>
                      <tr className="border-b border-border text-muted-foreground">
                        <th className="py-2 font-medium">Category</th>
                        <th className="py-2 font-medium">Entity Reference</th>
                        <th className="py-2 font-medium">Amount</th>
                        <th className="py-2 font-medium">Reason / Disallowance</th>
                        <th className="py-2 font-medium">Status</th>
                        <th className="py-2 font-medium text-right">Action</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border">
                      {exceptionRegister.map((exc: any) => (
                        <tr key={exc.id} className="hover:bg-muted/40">
                          <td className="py-2.5 font-mono font-medium">{exc.exception_category}</td>
                          <td className="py-2.5">{exc.entity_type} #{exc.entity_id?.slice(0, 8)}</td>
                          <td className="py-2.5">
                            <CurrencyDisplay amountMinor={exc.amount_minor} currency="PHP" size="sm" className="font-medium text-amber-600" />
                          </td>
                          <td className="py-2.5 text-muted-foreground max-w-xs truncate">{exc.notes || exc.reason}</td>
                          <td className="py-2.5">
                            <FinancialStatusBadge status={exc.status} />
                          </td>
                          <td className="py-2.5 text-right">
                            {exc.status !== "resolved" && (
                              <Button
                                size="sm"
                                variant="outline"
                                className="h-7 text-xs"
                                onClick={async () => {
                                  try {
                                    await resolveExceptionRegisterItem(currentActorId, exc.id, {
                                      target_status: "resolved",
                                      resolution_notes: "Settled and verified by Controller",
                                    })
                                    setFeedback({ type: "success", message: "Exception resolved." })
                                    await loadData()
                                  } catch (e: any) {
                                    setFeedback({ type: "error", message: e.message })
                                  }
                                }}
                              >
                                Resolve
                              </Button>
                            )}
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

      {/* TAB 2: CASH ADVANCES & 6-PERSON SOD DISBURSEMENT */}
      {activeTab === "advances" && (
        <div className="space-y-6">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-base font-semibold">Cash Advances & Controlled Disbursement</h2>
              <p className="text-xs text-muted-foreground">
                Multi-tier approval with 6 distinct actors: Maker → Verifier → Bank Approver → Evidence Verifier.
              </p>
            </div>
            <Button
              size="sm"
              onClick={() => setShowAdvanceModal(true)}
              className="h-8 text-xs gap-1.5"
            >
              <Plus className="w-3.5 h-3.5" />
              Request Cash Advance
            </Button>
          </div>

          <Card className="shadow-xs">
            <CardContent className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead>
                    <tr className="border-b border-border bg-muted/40 text-muted-foreground">
                      <th className="py-3 px-4 font-medium">Advance #</th>
                      <th className="py-3 px-4 font-medium">Employee</th>
                      <th className="py-3 px-4 font-medium">Purpose</th>
                      <th className="py-3 px-4 font-medium">Requested</th>
                      <th className="py-3 px-4 font-medium">Date Needed</th>
                      <th className="py-3 px-4 font-medium">Approval</th>
                      <th className="py-3 px-4 font-medium">Payment State</th>
                      <th className="py-3 px-4 font-medium">Settlement Status</th>
                      <th className="py-3 px-4 font-medium">Balance</th>
                      <th className="py-3 px-4 font-medium text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {advances.length === 0 ? (
                      <tr>
                        <td colSpan={10} className="py-8 text-center text-muted-foreground">
                          No cash advance requests found. Click "Request Cash Advance" to create one.
                        </td>
                      </tr>
                    ) : (
                      advances.map((adv) => (
                        <tr key={adv.id} className="hover:bg-muted/30">
                          <td className="py-3 px-4 font-mono font-medium text-primary">
                            {adv.advance_number}
                          </td>
                          <td className="py-3 px-4">{adv.employee_user_id || "Field Staff"}</td>
                          <td className="py-3 px-4 max-w-xs truncate">{adv.purpose}</td>
                          <td className="py-3 px-4">
                            <CurrencyDisplay amountMinor={adv.requested_amount_minor} currency="PHP" size="sm" className="font-semibold" />
                          </td>
                          <td className="py-3 px-4 text-muted-foreground">{adv.needed_by_date}</td>
                          <td className="py-3 px-4">
                            <FinancialStatusBadge status={adv.approval_status} />
                          </td>
                          <td className="py-3 px-4">
                            <FinancialStatusBadge status={adv.payment_status} />
                          </td>
                          <td className="py-3 px-4">
                            <FinancialStatusBadge status={adv.settlement_status} />
                          </td>
                          <td className="py-3 px-4 font-bold text-secondary">
                            <CurrencyDisplay
                              amountMinor={adv.reconciliation?.outstanding_balance_minor ?? adv.requested_amount_minor}
                              currency="PHP"
                              size="sm"
                              className="font-bold text-secondary"
                            />
                          </td>
                          <td className="py-3 px-4 text-right space-x-1.5 whitespace-nowrap">
                            {/* Submit */}
                            {adv.approval_status === "draft" && (
                              <Button
                                size="sm"
                                variant="outline"
                                className="h-7 text-xs"
                                onClick={() => handleSubmitAdvance(adv.id)}
                                disabled={actionLoading}
                              >
                                Submit
                              </Button>
                            )}

                            {/* Approve */}
                            {adv.approval_status === "submitted" && (
                              <Button
                                size="sm"
                                className="h-7 text-xs bg-emerald-600 hover:bg-emerald-700 text-white"
                                onClick={() => handleDecideAdvance(adv.id, "approved")}
                                disabled={actionLoading}
                              >
                                Approve
                              </Button>
                            )}

                            {/* Prepare / View Disbursement */}
                            {adv.approval_status === "approved" && adv.payment_status !== "released" && (
                              <Button
                                size="sm"
                                variant="outline"
                                className="h-7 text-xs"
                                onClick={() => {
                                  setSelectedAdvanceForDisburse(adv)
                                  setDisbAmountPhp((adv.requested_amount_minor / 100).toString())
                                  setShowDisburseModal(true)
                                }}
                              >
                                Disburse (SoD)
                              </Button>
                            )}

                            {/* Record Cash Return */}
                            {adv.payment_status === "released" &&
                              adv.settlement_status !== "settled_zero" && (
                                <Button
                                  size="sm"
                                  variant="outline"
                                  className="h-7 text-xs"
                                  onClick={() => {
                                    setSelectedAdvanceForReturn(adv)
                                    setShowCashReturnModal(true)
                                  }}
                                >
                                  Cash Return
                                </Button>
                              )}

                            {/* Close Advance (Controller only) */}
                            {adv.settlement_status === "settled_zero" && adv.approval_status !== "closed" && (
                              <Button
                                size="sm"
                                className="h-7 text-xs bg-blue-600 hover:bg-blue-700 text-white"
                                onClick={() => handleCloseAdvance(adv.id)}
                                disabled={actionLoading}
                              >
                                Close Advance
                              </Button>
                            )}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* TAB 3: EXPENSE REPORTS & DECOUPLED LIQUIDATION */}
      {activeTab === "expenses" && (
        <div className="space-y-6">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-base font-semibold">Monthly Field Expense Reports & Liquidation</h2>
              <p className="text-xs text-muted-foreground">
                Decoupled from advances; approved lines apply oldest-first to open advances automatically.
              </p>
            </div>
            <Button
              size="sm"
              onClick={() => setShowExpenseModal(true)}
              className="h-8 text-xs gap-1.5"
            >
              <Plus className="w-3.5 h-3.5" />
              New Expense Report
            </Button>
          </div>

          <Card className="shadow-xs">
            <CardContent className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead>
                    <tr className="border-b border-border bg-muted/40 text-muted-foreground">
                      <th className="py-3 px-4 font-medium">Report #</th>
                      <th className="py-3 px-4 font-medium">Employee</th>
                      <th className="py-3 px-4 font-medium">Period</th>
                      <th className="py-3 px-4 font-medium">Title / Purpose</th>
                      <th className="py-3 px-4 font-medium">Total Reported</th>
                      <th className="py-3 px-4 font-medium">Approved</th>
                      <th className="py-3 px-4 font-medium">Lines</th>
                      <th className="py-3 px-4 font-medium">Review Status</th>
                      <th className="py-3 px-4 font-medium">Settlement Status</th>
                      <th className="py-3 px-4 font-medium text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {expenses.length === 0 ? (
                      <tr>
                        <td colSpan={10} className="py-8 text-center text-muted-foreground">
                          No expense reports found. Click "New Expense Report" to create one.
                        </td>
                      </tr>
                    ) : (
                      expenses.map((exp) => (
                        <tr key={exp.id} className="hover:bg-muted/30">
                          <td className="py-3 px-4 font-mono font-medium text-primary">
                            {exp.report_number}
                          </td>
                          <td className="py-3 px-4">{exp.employee_user_id || "Field Staff"}</td>
                          <td className="py-3 px-4 font-mono">{exp.period_key}</td>
                          <td className="py-3 px-4 max-w-xs truncate">{exp.title}</td>
                          <td className="py-3 px-4">
                            <CurrencyDisplay amountMinor={exp.total_reported_minor} currency="PHP" size="sm" className="font-semibold" />
                          </td>
                          <td className="py-3 px-4 font-semibold text-emerald-600">
                            <CurrencyDisplay amountMinor={exp.total_approved_minor} currency="PHP" size="sm" className="font-semibold text-emerald-600" />
                          </td>
                          <td className="py-3 px-4">{exp.lines?.length || 0}</td>
                          <td className="py-3 px-4">
                            <FinancialStatusBadge status={exp.review_status} />
                          </td>
                          <td className="py-3 px-4">
                            <FinancialStatusBadge status={exp.settlement_status} />
                          </td>
                          <td className="py-3 px-4 text-right space-x-1.5 whitespace-nowrap">
                            {/* Manage Lines */}
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-7 text-xs"
                              onClick={() => {
                                setSelectedExpenseReport(exp)
                                setShowAddLineModal(true)
                              }}
                            >
                              Lines ({exp.lines?.length || 0})
                            </Button>

                            {/* Dry-run Policy Preview */}
                            <Button
                              size="sm"
                              variant="outline"
                              className="h-7 text-xs"
                              onClick={() => handleEvaluatePolicyPreview(exp.id)}
                              disabled={evaluatingReportId === exp.id}
                            >
                              {evaluatingReportId === exp.id ? (
                                <Loader2 className="w-3 h-3 animate-spin" />
                              ) : (
                                "Evaluate Policy"
                              )}
                            </Button>

                            {/* Submit */}
                            {exp.submission_status === "draft" && (
                              <Button
                                size="sm"
                                variant="outline"
                                className="h-7 text-xs"
                                onClick={() => handleSubmitExpenseReport(exp.id)}
                                disabled={actionLoading}
                              >
                                Submit
                              </Button>
                            )}

                            {/* Review (Finance Reviewer) */}
                            {exp.submission_status === "submitted" && exp.review_status === "under_review" && (
                              <Button
                                size="sm"
                                className="h-7 text-xs bg-emerald-600 hover:bg-emerald-700 text-white"
                                onClick={() => handleReviewReport(exp, "approved")}
                                disabled={actionLoading}
                              >
                                Approve Lines
                              </Button>
                            )}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>

          {/* Real-time Policy Evaluation Preview Box */}
          {policyEvalResult && (
            <Card className="border-border shadow-xs bg-muted/20">
              <CardHeader className="pb-2 flex flex-row items-center justify-between">
                <div>
                  <CardTitle className="text-sm font-semibold flex items-center gap-2">
                    <Scale className="w-4 h-4 text-primary" />
                    Live Policy Evaluation Engine Outcome
                  </CardTitle>
                  <CardDescription className="text-xs">
                    Evaluated against active policy version {policyEvalResult.policy_version_id?.slice(0, 8)}
                  </CardDescription>
                </div>
                <Badge
                  variant={policyEvalResult.overall_passed ? "default" : "destructive"}
                  className="text-xs font-mono"
                >
                  {policyEvalResult.overall_passed ? "PASSED ALL RULES" : "BLOCKED / BREACH"}
                </Badge>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-2 text-xs">
                  <div className="p-2.5 rounded bg-background border">
                    <span className="text-muted-foreground block text-[10px] uppercase">Total Claimed</span>
                    <span className="font-mono font-bold text-sm">
                      {formatPhpMinor(policyEvalResult.total_amount_minor)}
                    </span>
                  </div>
                  <div className="p-2.5 rounded bg-background border">
                    <span className="text-muted-foreground block text-[10px] uppercase">Period Cap (₱15,000)</span>
                    <span className="font-mono font-bold text-sm">
                      {policyEvalResult.total_amount_minor > 1500000 ? (
                        <span className="text-rose-600">Breached</span>
                      ) : (
                        <span className="text-emerald-600">Within Limit</span>
                      )}
                    </span>
                  </div>
                  <div className="p-2.5 rounded bg-background border">
                    <span className="text-muted-foreground block text-[10px] uppercase">Exceptions Covered</span>
                    <span className="font-mono font-bold text-sm">
                      {policyEvalResult.covered_by_exception_ids?.length || 0}
                    </span>
                  </div>
                </div>

                <div className="space-y-1">
                  <span className="text-[11px] font-semibold uppercase text-muted-foreground">Rule Breakdown:</span>
                  {(policyEvalResult.rule_evaluations || policyEvalResult.blocking_breaches || []).map(
                    (r: any, idx: number) => (
                      <div
                        key={idx}
                        className="flex items-center justify-between p-2 rounded bg-background border text-xs"
                      >
                        <span className="font-mono font-medium">{r.rule_code}</span>
                        <div className="flex items-center gap-2">
                          <span className="text-muted-foreground">{r.message}</span>
                          <Badge
                            variant={r.outcome === "pass" ? "outline" : "destructive"}
                            className="text-[10px] uppercase font-mono"
                          >
                            {r.outcome}
                          </Badge>
                        </div>
                      </div>
                    )
                  )}
                </div>
              </CardContent>
            </Card>
          )}
        </div>
      )}

      {/* TAB 4: PURCHASE-TO-PAY (P2P) */}
      {activeTab === "p2p" && (
        <div className="space-y-6">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-base font-semibold">Purchase-to-Pay (P2P) & Vendor Verification</h2>
              <p className="text-xs text-muted-foreground">
                Independent vendor verification, purchase request approval matrix, and accounts payable invoice checks.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => setShowVendorModal(true)}
                className="h-8 text-xs gap-1.5"
              >
                <Plus className="w-3.5 h-3.5" />
                Add Vendor
              </Button>
              <Button
                size="sm"
                variant="outline"
                onClick={() => setShowPrModal(true)}
                className="h-8 text-xs gap-1.5"
              >
                <Plus className="w-3.5 h-3.5" />
                New Purchase Request
              </Button>
              <Button
                size="sm"
                onClick={() => setShowPayableModal(true)}
                className="h-8 text-xs gap-1.5"
              >
                <Plus className="w-3.5 h-3.5" />
                Validate Payable
              </Button>
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            {/* Vendor Registry */}
            <Card className="shadow-xs lg:col-span-1">
              <CardHeader className="pb-3">
                <CardTitle className="text-sm font-semibold flex items-center justify-between">
                  <span>Vendors Registry</span>
                  <Badge variant="outline" className="text-xs font-mono">
                    {vendors.length}
                  </Badge>
                </CardTitle>
                <CardDescription className="text-xs">
                  Creator != Verifier enforcement. Pending verification blocks payables.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-2 p-3">
                {vendors.map((v) => (
                  <div key={v.id} className="p-2.5 rounded-lg border bg-background text-xs space-y-1.5">
                    <div className="flex items-center justify-between">
                      <span className="font-semibold text-primary">{v.registered_name || v.legal_name}</span>
                      <Badge
                        variant={v.verification_status === "verified" ? "default" : "outline"}
                        className="text-[10px] uppercase font-mono"
                      >
                        {v.verification_status}
                      </Badge>
                    </div>
                    <div className="text-muted-foreground flex items-center justify-between text-[11px]">
                      <span>TIN: {v.tin || "N/A"}</span>
                      <span>{v.payee_type || "supplier"}</span>
                    </div>
                    {v.verification_status !== "verified" && (
                      <Button
                        size="sm"
                        variant="outline"
                        className="w-full h-6 text-[10px] mt-1"
                        onClick={() => handleVerifyVendor(v.id)}
                        disabled={actionLoading}
                      >
                        Verify Vendor (Distinct CPA)
                      </Button>
                    )}
                  </div>
                ))}
              </CardContent>
            </Card>

            {/* Accounts Payable Registry */}
            <Card className="shadow-xs lg:col-span-2">
              <CardHeader className="pb-3">
                <CardTitle className="text-sm font-semibold flex items-center justify-between">
                  <span>Accounts Payable (AP)</span>
                  <Badge variant="outline" className="text-xs font-mono">
                    {payables.length}
                  </Badge>
                </CardTitle>
                <CardDescription className="text-xs">
                  Validated invoices with BIR tax codes (12% VAT, 1% / 2% EWT) and normalized duplicate checks.
                </CardDescription>
              </CardHeader>
              <CardContent className="p-0">
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs">
                    <thead>
                      <tr className="border-b border-border bg-muted/40 text-muted-foreground">
                        <th className="py-2.5 px-3 font-medium">AP #</th>
                        <th className="py-2.5 px-3 font-medium">Invoice #</th>
                        <th className="py-2.5 px-3 font-medium">Due Date</th>
                        <th className="py-2.5 px-3 font-medium">Gross</th>
                        <th className="py-2.5 px-3 font-medium">Net Payable</th>
                        <th className="py-2.5 px-3 font-medium">Paid</th>
                        <th className="py-2.5 px-3 font-medium">Status</th>
                        <th className="py-2.5 px-3 font-medium text-right">Action</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-border">
                      {payables.length === 0 ? (
                        <tr>
                          <td colSpan={8} className="py-6 text-center text-muted-foreground">
                            No accounts payable records found.
                          </td>
                        </tr>
                      ) : (
                        payables.map((ap) => (
                          <tr key={ap.id} className="hover:bg-muted/30">
                            <td className="py-2.5 px-3 font-mono font-medium text-primary">{ap.ap_number}</td>
                            <td className="py-2.5 px-3 font-mono">{ap.invoice_number}</td>
                            <td className="py-2.5 px-3 text-muted-foreground">{ap.due_date}</td>
                            <td className="py-2.5 px-3">
                              <CurrencyDisplay amountMinor={ap.gross_amount_minor} currency="PHP" size="sm" />
                            </td>
                            <td className="py-2.5 px-3 font-semibold text-secondary">
                              <CurrencyDisplay amountMinor={ap.net_payable_minor} currency="PHP" size="sm" className="font-semibold text-secondary" />
                            </td>
                            <td className="py-2.5 px-3">
                              <CurrencyDisplay amountMinor={ap.paid_amount_minor} currency="PHP" size="sm" className="text-emerald-600" />
                            </td>
                            <td className="py-2.5 px-3">
                              <FinancialStatusBadge status={ap.payment_status || "open"} />
                            </td>
                            <td className="py-2.5 px-3 text-right">
                              {(() => {
                                const apRemainingBalance =
                                  ap.remaining_balance_minor ??
                                  ap.balance_minor ??
                                  Math.max(0, (ap.net_payable_minor ?? 0) - (ap.paid_amount_minor ?? 0))
                                return (
                                  apRemainingBalance > 0 && (
                                    <Button
                                      size="sm"
                                      variant="outline"
                                      className="h-6 text-[10px]"
                                      onClick={async () => {
                                        try {
                                          const p = await preparePayablePayment(currentActorId, ap.id, {
                                            payment_amount_minor: apRemainingBalance,
                                            payment_method: "bank_transfer",
                                          })
                                          setFeedback({
                                            type: "success",
                                            message: `Prepared payment ${p.payment_number} for AP ${ap.ap_number}.`,
                                          })
                                          await loadData()
                                        } catch (e: any) {
                                          setFeedback({ type: "error", message: e.message })
                                        }
                                      }}
                                    >
                                      Disburse AP
                                    </Button>
                                  )
                                )
                              })()}
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </CardContent>
            </Card>
          </div>
        </div>
      )}

      {/* TAB 5: POLICIES & EXCEPTIONS */}
      {activeTab === "policies" && (
        <div className="space-y-6">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-base font-semibold">Configurable Financial Policies & Versioning</h2>
              <p className="text-xs text-muted-foreground">
                Author != Approver enforcement. Baseline PHP 15,000 monthly cap & single-use exception tracking.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => setShowExceptionModal(true)}
                className="h-8 text-xs gap-1.5"
              >
                <Plus className="w-3.5 h-3.5" />
                Request Policy Exception
              </Button>
              <Button
                size="sm"
                onClick={() => setShowPolicyModal(true)}
                className="h-8 text-xs gap-1.5"
              >
                <Plus className="w-3.5 h-3.5" />
                Author New Version
              </Button>
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {/* Versioned Policies */}
            <Card className="shadow-xs">
              <CardHeader className="pb-3">
                <CardTitle className="text-sm font-semibold">Active & Historic Policy Versions</CardTitle>
                <CardDescription className="text-xs">
                  All transactions pin their applicable policy version and outcome immutably.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {policies.flatMap((p) => p.versions || []).map((ver) => (
                  <div key={ver.id} className="p-3 rounded-lg border bg-background space-y-2 text-xs">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-primary">Version v{ver.version_number}</span>
                        <Badge
                          variant={ver.status === "active" ? "default" : "outline"}
                          className="text-[10px] uppercase font-mono"
                        >
                          {ver.status}
                        </Badge>
                      </div>
                      <span className="text-muted-foreground text-[11px]">Effective: {ver.effective_from}</span>
                    </div>

                    <div className="grid grid-cols-2 gap-2 text-[11px]">
                      <div>
                        <span className="text-muted-foreground">Monthly Cap:</span>{" "}
                        <span className="font-mono font-semibold">
                          {formatPhpMinor(ver.aggregate_monthly_limit_minor || ver.numeric_limit_minor || 1500000)}
                        </span>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Max Reports/Period:</span>{" "}
                        <span className="font-mono font-semibold">1 report/month</span>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Deadline:</span>{" "}
                        <span>Day 2 of following month</span>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Finance SLA:</span>{" "}
                        <span>5 business days</span>
                      </div>
                    </div>

                    <p className="text-[11px] text-muted-foreground italic border-t pt-1">
                      Reason: {ver.change_reason || "Initial baseline company policy"}
                    </p>

                    {ver.status === "draft" && (
                      <div className="flex items-center justify-end gap-2 pt-1">
                        <Button
                          size="sm"
                          variant="outline"
                          className="h-6 text-[10px]"
                          onClick={async () => {
                            try {
                              await submitPolicyVersion(currentActorId, ver.id)
                              setFeedback({ type: "success", message: `Version v${ver.version_number} submitted.` })
                              await loadData()
                            } catch (e: any) {
                              setFeedback({ type: "error", message: e.message })
                            }
                          }}
                        >
                          Submit Version
                        </Button>
                      </div>
                    )}

                    {ver.status === "pending_approval" && (
                      <div className="flex items-center justify-end gap-2 pt-1">
                        <Button
                          size="sm"
                          className="h-6 text-[10px] bg-emerald-600 hover:bg-emerald-700 text-white"
                          onClick={() => handleApprovePolicyVersion(ver.id)}
                          disabled={actionLoading}
                        >
                          Approve (Controller Only)
                        </Button>
                      </div>
                    )}
                  </div>
                ))}
              </CardContent>
            </Card>

            {/* Exception Requests */}
            <Card className="shadow-xs">
              <CardHeader className="pb-3">
                <CardTitle className="text-sm font-semibold">Policy Exception Requests</CardTitle>
                <CardDescription className="text-xs">
                  Scoped, single-use exceptions approved by Finance Controller.
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3">
                {exceptions.length === 0 ? (
                  <div className="text-center py-8 text-xs text-muted-foreground">
                    No policy exception requests recorded.
                  </div>
                ) : (
                  exceptions.map((exc) => (
                    <div key={exc.id} className="p-3 rounded-lg border bg-background space-y-2 text-xs">
                      <div className="flex items-center justify-between">
                        <span className="font-mono font-semibold text-primary">{exc.exception_number}</span>
                        <Badge
                          variant={exc.status === "approved" ? "default" : "outline"}
                          className="text-[10px] uppercase font-mono"
                        >
                          {exc.status}
                        </Badge>
                      </div>

                      <div className="grid grid-cols-2 gap-2 text-[11px]">
                        <div>
                          <span className="text-muted-foreground">Rule Violated:</span>{" "}
                          <span className="font-mono font-medium">{exc.rule_code}</span>
                        </div>
                        <div>
                          <span className="text-muted-foreground">Requested Limit:</span>{" "}
                          <span className="font-mono font-semibold text-accent">
                            {formatPhpMinor(exc.requested_amount_minor)}
                          </span>
                        </div>
                      </div>

                      <p className="text-[11px] text-muted-foreground">
                        Justification: {exc.justification}
                      </p>

                      {exc.status === "submitted" && (
                        <div className="flex items-center justify-end gap-2 pt-1">
                          <Button
                            size="sm"
                            className="h-6 text-[10px] bg-emerald-600 hover:bg-emerald-700 text-white"
                            onClick={() => handleDecideException(exc.id, "approved")}
                          >
                            Approve Exception
                          </Button>
                          <Button
                            size="sm"
                            variant="destructive"
                            className="h-6 text-[10px]"
                            onClick={() => handleDecideException(exc.id, "rejected")}
                          >
                            Reject
                          </Button>
                        </div>
                      )}
                    </div>
                  ))
                )}
              </CardContent>
            </Card>
          </div>
        </div>
      )}

      {/* TAB 6: CENTRALIZED TRACKER & AUDIT */}
      {activeTab === "tracker" && (
        <div className="space-y-6">
          <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
            <div>
              <h2 className="text-base font-semibold">Centralized Finance Record Tracker & Audit Chain</h2>
              <p className="text-xs text-muted-foreground">
                Read-model consolidating Advances, Reports, PRs, and Payables with formula-safe CSV export.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <div className="relative">
                <Search className="w-3.5 h-3.5 absolute left-2.5 top-2.5 text-muted-foreground" />
                <Input
                  placeholder="Search records..."
                  value={trackerSearch}
                  onChange={(e) => setTrackerSearch(e.target.value)}
                  className="h-8 pl-8 text-xs w-48"
                />
              </div>
              <select
                value={trackerWorkflowFilter}
                onChange={(e) => setTrackerWorkflowFilter(e.target.value)}
                className="h-8 text-xs rounded-md border border-input bg-background px-2.5 py-1"
              >
                <option value="all">All Workflows</option>
                <option value="cash_advance">Cash Advances</option>
                <option value="expense_report">Expense Reports</option>
                <option value="purchase_request">Purchase Requests</option>
                <option value="account_payable">Accounts Payable</option>
              </select>
            </div>
          </div>

          <Card className="shadow-xs">
            <CardContent className="p-0">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead>
                    <tr className="border-b border-border bg-muted/40 text-muted-foreground">
                      <th className="py-2.5 px-3 font-medium">Type</th>
                      <th className="py-2.5 px-3 font-medium">Ref #</th>
                      <th className="py-2.5 px-3 font-medium">Date</th>
                      <th className="py-2.5 px-3 font-medium">Payee / Employee</th>
                      <th className="py-2.5 px-3 font-medium">Category</th>
                      <th className="py-2.5 px-3 font-medium">Gross</th>
                      <th className="py-2.5 px-3 font-medium">Net / Balance</th>
                      <th className="py-2.5 px-3 font-medium">Approval</th>
                      <th className="py-2.5 px-3 font-medium">Payment State</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {filteredTrackerRows.length === 0 ? (
                      <tr>
                        <td colSpan={9} className="py-8 text-center text-muted-foreground">
                          No matching tracker records found.
                        </td>
                      </tr>
                    ) : (
                      filteredTrackerRows.map((r, i) => (
                        <tr key={i} className="hover:bg-muted/30">
                          <td className="py-2.5 px-3">
                            <Badge variant="outline" className="text-[10px] uppercase font-mono">
                              {r.entity_type}
                            </Badge>
                          </td>
                          <td className="py-2.5 px-3 font-mono font-medium text-primary">
                            {r.reference_number}
                          </td>
                          <td className="py-2.5 px-3 text-muted-foreground">{r.transaction_date}</td>
                          <td className="py-2.5 px-3">{r.employee_or_vendor}</td>
                          <td className="py-2.5 px-3 text-muted-foreground">{r.category}</td>
                          <td className="py-2.5 px-3">
                            <CurrencyDisplay amountMinor={r.gross_amount_minor} currency="PHP" size="sm" />
                          </td>
                          <td className="py-2.5 px-3 font-semibold text-secondary">
                            <CurrencyDisplay amountMinor={r.outstanding_balance_minor} currency="PHP" size="sm" className="font-semibold text-secondary" />
                          </td>
                          <td className="py-2.5 px-3">
                            <FinancialStatusBadge status={r.approval_status} />
                          </td>
                          <td className="py-2.5 px-3">
                            <FinancialStatusBadge status={r.payment_status} />
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* MODAL 1: REQUEST CASH ADVANCE */}
      {showAdvanceModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">Request Cash Advance</CardTitle>
                <CardDescription className="text-xs">
                  Subject to supervisor approval & 6-person disbursement segregation of duties.
                </CardDescription>
              </div>
              <button onClick={() => setShowAdvanceModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleCreateAdvance}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Business Purpose</label>
                  <Input
                    value={advPurpose}
                    onChange={(e) => setAdvPurpose(e.target.value)}
                    placeholder="e.g. Provincial school visit and plant materials"
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Requested Amount (PHP)</label>
                  <Input
                    type="number"
                    step="0.01"
                    value={advAmountPhp}
                    onChange={(e) => setAdvAmountPhp(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Date Needed</label>
                  <Input
                    type="date"
                    value={advDateNeeded}
                    onChange={(e) => setAdvDateNeeded(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Site / Destination</label>
                  <Input
                    value={advSite}
                    onChange={(e) => setAdvSite(e.target.value)}
                    placeholder="e.g. Laguna / Batangas Manufacturing Zone"
                    required
                    className="h-8 text-xs"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowAdvanceModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  {actionLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin mr-1" /> : null}
                  Create Advance
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 2: 6-PERSON SOD DISBURSEMENT DRAWER */}
      {showDisburseModal && selectedAdvanceForDisburse && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-lg shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold flex items-center gap-2">
                  <Landmark className="w-4 h-4 text-primary" />
                  Controlled Bank Disbursement (6-Person SoD)
                </CardTitle>
                <CardDescription className="text-xs">
                  Advance {selectedAdvanceForDisburse.advance_number} — Total PHP{" "}
                  {(selectedAdvanceForDisburse.requested_amount_minor / 100).toFixed(2)}
                </CardDescription>
              </div>
              <button onClick={() => setShowDisburseModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <CardContent className="space-y-4 text-xs">
              {/* SoD Pipeline Step Indicators */}
              <div className="grid grid-cols-4 gap-1 text-center font-mono text-[10px]">
                <div className="p-1.5 rounded bg-muted/50 border font-medium">1. Maker</div>
                <div className="p-1.5 rounded bg-muted/50 border font-medium">2. Verifier</div>
                <div className="p-1.5 rounded bg-muted/50 border font-medium">3. Bank Appr</div>
                <div className="p-1.5 rounded bg-muted/50 border font-medium">4. Evidence</div>
              </div>

              {/* Step 1: Maker Form */}
              <form onSubmit={handleMakerPrepareDisbursement} className="space-y-3 border-t pt-3">
                <span className="font-semibold text-primary block">Step 1: Maker Prepares Disbursement</span>
                <div>
                  <label className="block font-medium mb-1">Disbursement Amount (PHP)</label>
                  <Input
                    type="number"
                    step="0.01"
                    value={disbAmountPhp}
                    onChange={(e) => setDisbAmountPhp(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Recipient Account Reference</label>
                  <Input
                    value={disbAccountRef}
                    onChange={(e) => setDisbAccountRef(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <Button type="submit" size="sm" className="w-full text-xs" disabled={actionLoading}>
                  {actionLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin mr-1" /> : null}
                  Prepare Disbursement (Maker)
                </Button>
              </form>

              {/* Step 2-4: If disbursements already exist on advance */}
              {selectedAdvanceForDisburse.disbursements?.map((d) => (
                <div key={d.id} className="p-2.5 rounded border bg-muted/20 space-y-2">
                  <div className="flex items-center justify-between font-mono">
                    <span className="font-semibold">{d.disbursement_number}</span>
                    <Badge variant="outline" className="text-[10px] uppercase">
                      {d.status}
                    </Badge>
                  </div>

                  <div className="grid grid-cols-2 gap-1 text-[11px] text-muted-foreground">
                    <span>Maker: {d.maker_user_id}</span>
                    <span>Verifier: {d.verifier_user_id || "Pending"}</span>
                  </div>

                  <div className="flex items-center gap-2 pt-1">
                    {d.status === "prepared" && (
                      <Button
                        size="sm"
                        variant="outline"
                        className="w-full h-7 text-xs"
                        onClick={() => handleVerifyDisbursement(d.id)}
                      >
                        Verify (Distinct Verifier)
                      </Button>
                    )}

                    {d.status === "verified" && (
                      <Button
                        size="sm"
                        variant="outline"
                        className="w-full h-7 text-xs bg-emerald-50 text-emerald-800 border-emerald-300"
                        onClick={() => handleBankApproveDisbursement(d.id)}
                      >
                        Bank Approve (Bank Approver)
                      </Button>
                    )}

                    {d.status === "bank_approved" && (
                      <Button
                        size="sm"
                        className="w-full h-7 text-xs"
                        onClick={() => handleExecuteDisbursement(d.id, d.payment_amount_minor || d.amount_minor || 0)}
                      >
                        Record Bank Execution & Evidence
                      </Button>
                    )}

                    {d.status === "executed_unverified" && (
                      <Button
                        size="sm"
                        className="w-full h-7 text-xs bg-blue-600 hover:bg-blue-700 text-white"
                        onClick={() => handleVerifyEvidenceDisbursement(d.id)}
                      >
                        Confirm Evidence (6th Actor)
                      </Button>
                    )}
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        </div>
      )}

      {/* MODAL 3: NEW EXPENSE REPORT */}
      {showExpenseModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">New Monthly Expense Report</CardTitle>
                <CardDescription className="text-xs">
                  Decoupled monthly reporting with consolidated PDF evidence.
                </CardDescription>
              </div>
              <button onClick={() => setShowExpenseModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleCreateExpenseReport}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Reporting Period (YYYY-MM)</label>
                  <Input
                    value={expPeriod}
                    onChange={(e) => setExpPeriod(e.target.value)}
                    placeholder="2026-05"
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Title / Business Purpose</label>
                  <Input
                    value={expTitle}
                    onChange={(e) => setExpTitle(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div className="flex items-center gap-2 pt-1">
                  <input
                    type="checkbox"
                    id="outOfBaseCheck"
                    checked={expOutOfBase}
                    onChange={(e) => setExpOutOfBase(e.target.checked)}
                    className="rounded border-input text-primary focus:ring-ring"
                  />
                  <label htmlFor="outOfBaseCheck" className="text-xs font-medium cursor-pointer">
                    Out-of-Base Travel (Attaches required MFAR document)
                  </label>
                </div>
                <div className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    id="declarationCheck"
                    checked={expDeclaration}
                    onChange={(e) => setExpDeclaration(e.target.checked)}
                    required
                    className="rounded border-input text-primary focus:ring-ring"
                  />
                  <label htmlFor="declarationCheck" className="text-xs font-medium cursor-pointer">
                    I confirm all expenses were legitimately incurred for company business.
                  </label>
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowExpenseModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  {actionLoading ? <Loader2 className="w-3.5 h-3.5 animate-spin mr-1" /> : null}
                  Create Report
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 4: ADD EXPENSE LINE */}
      {showAddLineModal && selectedExpenseReport && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">Add Expense Line</CardTitle>
                <CardDescription className="text-xs font-mono">
                  Report #{selectedExpenseReport.report_number}
                </CardDescription>
              </div>
              <button onClick={() => setShowAddLineModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleAddExpenseLine}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Expense Category</label>
                  <select
                    value={lineCategory}
                    onChange={(e) => setLineCategory(e.target.value)}
                    className="w-full h-8 text-xs rounded-md border border-input bg-background px-2.5 py-1"
                  >
                    <option value="transportation">Transportation</option>
                    <option value="fuel">Fuel & Toll</option>
                    <option value="accommodation">Accommodation</option>
                    <option value="meals">Meals</option>
                    <option value="school_visit_materials">School Visit Materials</option>
                    <option value="communication">Communication</option>
                    <option value="representation">Representation</option>
                    <option value="other">Other Operational</option>
                  </select>
                </div>
                <div>
                  <label className="block font-medium mb-1">Merchant / Payee</label>
                  <Input
                    value={lineMerchant}
                    onChange={(e) => setLineMerchant(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Business Description</label>
                  <Input
                    value={linePurpose}
                    onChange={(e) => setLinePurpose(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Claimed Amount (PHP)</label>
                  <Input
                    type="number"
                    step="0.01"
                    value={lineAmountPhp}
                    onChange={(e) => setLineAmountPhp(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Receipt / OR Number</label>
                  <Input
                    value={lineReceiptNo}
                    onChange={(e) => setLineReceiptNo(e.target.value)}
                    placeholder="OR-99881"
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Site / Destination</label>
                  <Input
                    value={lineLocation}
                    onChange={(e) => setLineLocation(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowAddLineModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  Add Line
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 5: RECORD CASH RETURN */}
      {showCashReturnModal && selectedAdvanceForReturn && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">Record Employee Cash Return</CardTitle>
                <CardDescription className="text-xs">
                  Evidence-backed return of unused advance cash to company account.
                </CardDescription>
              </div>
              <button onClick={() => setShowCashReturnModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleRecordCashReturn}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Returned Cash Amount (PHP)</label>
                  <Input
                    type="number"
                    step="0.01"
                    value={returnAmountPhp}
                    onChange={(e) => setReturnAmountPhp(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Bank Deposit / Transfer Reference</label>
                  <Input
                    value={returnBankRef}
                    onChange={(e) => setReturnBankRef(e.target.value)}
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowCashReturnModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  Confirm Return
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 6: NEW VENDOR */}
      {showVendorModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">Register Vendor / Payee</CardTitle>
                <CardDescription className="text-xs">
                  Created as pending verification. Creator cannot verify.
                </CardDescription>
              </div>
              <button onClick={() => setShowVendorModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleCreateVendor}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Registered Legal Name</label>
                  <Input
                    value={vendorName}
                    onChange={(e) => setVendorName(e.target.value)}
                    placeholder="e.g. Acme Industrial Solutions Inc."
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Payee Type</label>
                  <select
                    value={vendorType}
                    onChange={(e) => setVendorType(e.target.value)}
                    className="w-full h-8 text-xs rounded-md border border-input bg-background px-2.5 py-1"
                  >
                    <option value="supplier">Supplier / Vendor</option>
                    <option value="utility">Utility Company</option>
                    <option value="statutory_agency">Statutory Agency</option>
                    <option value="contractor">Contractor / Honoraria</option>
                  </select>
                </div>
                <div>
                  <label className="block font-medium mb-1">Tax Identification Number (TIN)</label>
                  <Input
                    value={vendorTin}
                    onChange={(e) => setVendorTin(e.target.value)}
                    placeholder="000-000-000-000"
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowVendorModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  Save Vendor
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 7: NEW PURCHASE REQUEST */}
      {showPrModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">New Purchase Request</CardTitle>
                <CardDescription className="text-xs">
                  Initiate vendor procurement subject to matrix limits.
                </CardDescription>
              </div>
              <button onClick={() => setShowPrModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleCreatePr}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Vendor</label>
                  <select
                    value={prVendorId}
                    onChange={(e) => setPrVendorId(e.target.value)}
                    className="w-full h-8 text-xs rounded-md border border-input bg-background px-2.5 py-1"
                  >
                    {vendors.map((v) => (
                      <option key={v.id} value={v.id}>
                        {v.legal_name} ({v.verification_status})
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block font-medium mb-1">Business Purpose</label>
                  <Input
                    value={prPurpose}
                    onChange={(e) => setPrPurpose(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Requested Amount (PHP)</label>
                  <Input
                    type="number"
                    step="0.01"
                    value={prAmountPhp}
                    onChange={(e) => setPrAmountPhp(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowPrModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  Create & Submit PR
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 8: VALIDATE ACCOUNT PAYABLE */}
      {showPayableModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">Validate Account Payable</CardTitle>
                <CardDescription className="text-xs">
                  3-way match against approved PR with automated tax code calculation.
                </CardDescription>
              </div>
              <button onClick={() => setShowPayableModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleValidatePayable}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Approved Purchase Request</label>
                  <select
                    value={apPrId}
                    onChange={(e) => setApPrId(e.target.value)}
                    className="w-full h-8 text-xs rounded-md border border-input bg-background px-2.5 py-1"
                  >
                    {purchaseRequests.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.pr_number} — PHP {p.total_estimated?.toFixed(2)} ({p.approval_status})
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block font-medium mb-1">Vendor Invoice #</label>
                  <Input
                    value={apInvoiceNo}
                    onChange={(e) => setApInvoiceNo(e.target.value)}
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <label className="block font-medium mb-1">Invoice Date</label>
                    <Input
                      type="date"
                      value={apInvoiceDate}
                      onChange={(e) => setApInvoiceDate(e.target.value)}
                      required
                      className="h-8 text-xs"
                    />
                  </div>
                  <div>
                    <label className="block font-medium mb-1">Due Date</label>
                    <Input
                      type="date"
                      value={apDueDate}
                      onChange={(e) => setApDueDate(e.target.value)}
                      required
                      className="h-8 text-xs"
                    />
                  </div>
                </div>
                <div>
                  <label className="block font-medium mb-1">Gross Payable (PHP)</label>
                  <Input
                    type="number"
                    step="0.01"
                    value={apOriginalPhp}
                    onChange={(e) => setApOriginalPhp(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowPayableModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  Validate AP
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 9: AUTHOR POLICY VERSION */}
      {showPolicyModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">Author Policy Version</CardTitle>
                <CardDescription className="text-xs">
                  Create a versioned policy draft. Author cannot approve.
                </CardDescription>
              </div>
              <button onClick={() => setShowPolicyModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleCreatePolicyVersion}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Effective From Date</label>
                  <Input
                    type="date"
                    value={polEffectiveFrom}
                    onChange={(e) => setPolEffectiveFrom(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Aggregate Monthly Limit (PHP)</label>
                  <Input
                    type="number"
                    value={polLimitPhp}
                    onChange={(e) => setPolLimitPhp(e.target.value)}
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Change Justification</label>
                  <Input
                    value={polJustification}
                    onChange={(e) => setPolJustification(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowPolicyModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  Save Version Draft
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}

      {/* MODAL 10: REQUEST POLICY EXCEPTION */}
      {showExceptionModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4">
          <Card className="w-full max-w-md shadow-xl border-border animate-in fade-in zoom-in-95 duration-150">
            <CardHeader className="pb-3 flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-sm font-semibold">Request Policy Exception</CardTitle>
                <CardDescription className="text-xs">
                  Single-use transaction scope approved by Controller.
                </CardDescription>
              </div>
              <button onClick={() => setShowExceptionModal(false)} className="text-muted-foreground hover:text-foreground">
                <X className="w-4 h-4" />
              </button>
            </CardHeader>
            <form onSubmit={handleCreateException}>
              <CardContent className="space-y-3 text-xs">
                <div>
                  <label className="block font-medium mb-1">Violated Rule Code</label>
                  <select
                    value={excRuleCode}
                    onChange={(e) => setExcRuleCode(e.target.value)}
                    className="w-full h-8 text-xs rounded-md border border-input bg-background px-2.5 py-1"
                  >
                    <option value="MONTHLY_AGGREGATE_CAP">MONTHLY_AGGREGATE_CAP (PHP 15,000 Cap)</option>
                    <option value="MONTHLY_REPORT_FREQUENCY">MONTHLY_REPORT_FREQUENCY (1 Report/Mo)</option>
                    <option value="OUT_OF_BASE_MFAR">OUT_OF_BASE_MFAR (Missing MFAR)</option>
                  </select>
                </div>
                <div>
                  <label className="block font-medium mb-1">Target Reporting Period</label>
                  <Input
                    value={excPeriod}
                    onChange={(e) => setExcPeriod(e.target.value)}
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Requested Over-Cap Amount (PHP)</label>
                  <Input
                    type="number"
                    value={excAmountPhp}
                    onChange={(e) => setExcAmountPhp(e.target.value)}
                    required
                    className="h-8 text-xs font-mono"
                  />
                </div>
                <div>
                  <label className="block font-medium mb-1">Business Justification</label>
                  <Input
                    value={excJustification}
                    onChange={(e) => setExcJustification(e.target.value)}
                    required
                    className="h-8 text-xs"
                  />
                </div>
              </CardContent>
              <CardFooter className="flex justify-end gap-2 pt-2 border-t">
                <Button type="button" variant="outline" size="sm" onClick={() => setShowExceptionModal(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={actionLoading}>
                  Submit Exception
                </Button>
              </CardFooter>
            </form>
          </Card>
        </div>
      )}
    </div>
  )
}

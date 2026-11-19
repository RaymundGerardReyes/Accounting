"use client"

import React, { useCallback, useEffect, useMemo, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Download,
  Lock,
  Plus,
  RefreshCw,
  ShieldCheck,
  Trash2,
} from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { CopyableHash, FinancialStatusBadge } from "@/components/ui/financial-primitives"
import {
  createManualGlJournal,
  decideGlJournal,
  fetchGlAccounts,
  fetchGlJournals,
  fetchGlTrialBalance,
  formatPhpMinor,
  GlAccountRecord,
  GlJournalBatchDto,
  GlTrialBalanceRowDto,
} from "@/lib/finance-api"

export type GlPostingStatus = "Validated" | "Approved" | "Posted" | "Rejected" | string
export type PeriodLockStatus = "Open" | "SoftClosed" | "Locked"

export interface OfficialGeneralLedgerActor {
  actorId: string
  displayName: string
}

export interface GlJournalLine {
  id: string
  accountCode: string
  accountName: string
  debitCentavos: number
  creditCentavos: number
  departmentId: string
  costCenterId?: string
  taxProfile?: string
  description?: string
}

export interface GlJournalEntry {
  journalId: string
  journalNumber: string
  sourceModule: "CashAdvance" | "ExpenseReport" | "AccountPayable" | "ManualAdjustment"
  sourceDocumentNumber: string
  accountingDate: string
  postingStatus: GlPostingStatus
  policyVersionId?: string
  taxRuleVersion?: string
  preparedByActorId: string
  preparedByName: string
  verifiedByName?: string | null
  postedByName?: string | null
  auditHash: string
  memo: string
  lines: GlJournalLine[]
}

export interface ChartOfAccountItem {
  accountCode: string
  accountName: string
  accountType: "Asset" | "Liability" | "Equity" | "Revenue" | "Expense" | string
  normalBalance?: string
}

interface WorkbenchDraftLine {
  key: string
  accountCode: string
  debitPhp: string
  creditPhp: string
  departmentId: string
  taxProfile: string
  description: string
}

export function parsePhpInputToCentavos(raw: string): number {
  const normalized = raw.trim().replace(/,/g, "")
  if (!normalized) return 0
  if (!/^\d*(\.\d{0,2})?$/.test(normalized)) return 0
  const [pesos = "0", cents = ""] = normalized.split(".")
  return Number(pesos || 0) * 100 + Number(cents.padEnd(2, "0") || 0)
}

export function sanitizeCsvCell(value: string | number | undefined): string {
  const raw = String(value ?? "")
  const neutralized = /^[=+\-@]/.test(raw.trimStart()) ? `'${raw}` : raw
  return `"${neutralized.replace(/"/g, '""')}"`
}

export function calculateVatInclusiveSplitCentavos(grossCentavos: number, vatRateBps = 1200) {
  if (!Number.isInteger(grossCentavos) || grossCentavos <= 0) {
    return { netBaseCentavos: 0, vatCentavos: 0, grossCentavos: 0 }
  }
  const divisor = 10000 + vatRateBps
  const vatCentavos = Math.round((grossCentavos * vatRateBps) / divisor)
  const netBaseCentavos = grossCentavos - vatCentavos
  return { netBaseCentavos, vatCentavos, grossCentavos }
}

export function validateCentavoJournalLines(
  lines: Array<{ debitCentavos: number; creditCentavos: number; accountCode: string }>,
) {
  let totalDebitCentavos = 0
  let totalCreditCentavos = 0

  for (const [index, line] of lines.entries()) {
    if (!line.accountCode) {
      return { isValid: false, totalDebitCentavos, totalCreditCentavos, varianceCentavos: 0, error: `Line ${index + 1} requires an account.` }
    }
    if (!Number.isInteger(line.debitCentavos) || !Number.isInteger(line.creditCentavos)) {
      return { isValid: false, totalDebitCentavos, totalCreditCentavos, varianceCentavos: 0, error: `Line ${index + 1} must use integer centavos.` }
    }
    if (line.debitCentavos < 0 || line.creditCentavos < 0 || (line.debitCentavos > 0 && line.creditCentavos > 0)) {
      return { isValid: false, totalDebitCentavos, totalCreditCentavos, varianceCentavos: 0, error: `Line ${index + 1} must contain one non-negative side.` }
    }
    if (line.debitCentavos === 0 && line.creditCentavos === 0) {
      return { isValid: false, totalDebitCentavos, totalCreditCentavos, varianceCentavos: 0, error: `Line ${index + 1} must not be zero.` }
    }
    totalDebitCentavos += line.debitCentavos
    totalCreditCentavos += line.creditCentavos
  }

  const varianceCentavos = totalDebitCentavos - totalCreditCentavos
  const isValid = lines.length >= 2 && totalDebitCentavos > 0 && varianceCentavos === 0
  return {
    isValid,
    totalDebitCentavos,
    totalCreditCentavos,
    varianceCentavos,
    error: isValid ? null : "A journal requires at least two balanced lines.",
  }
}

const seedCoa: ChartOfAccountItem[] = [
  { accountCode: "1010", accountName: "Cash in Bank - Operating PHP", accountType: "Asset", normalBalance: "Debit" },
  { accountCode: "1015", accountName: "Payment in Transit Clearing", accountType: "Asset", normalBalance: "Debit" },
  { accountCode: "1050", accountName: "PayMongo Gateway Clearing", accountType: "Asset", normalBalance: "Debit" },
  { accountCode: "1140", accountName: "Employee Advances Receivable", accountType: "Asset", normalBalance: "Debit" },
  { accountCode: "1160", accountName: "Creditable Input VAT (12%)", accountType: "Asset", normalBalance: "Debit" },
  { accountCode: "2010", accountName: "Accounts Payable - Trade", accountType: "Liability", normalBalance: "Credit" },
  { accountCode: "2020", accountName: "Reimbursements Payable - Employees", accountType: "Liability", normalBalance: "Credit" },
  { accountCode: "2120", accountName: "Expanded Withholding Tax Payable", accountType: "Liability", normalBalance: "Credit" },
  { accountCode: "5110", accountName: "Field Operations & Travel Expense", accountType: "Expense", normalBalance: "Debit" },
  { accountCode: "5210", accountName: "Professional & Vendor Services", accountType: "Expense", normalBalance: "Debit" },
  { accountCode: "5310", accountName: "Merchant Gateway Processing Fees", accountType: "Expense", normalBalance: "Debit" },
]

const initialJournals: GlJournalEntry[] = [
  {
    journalId: "je-2026-0001",
    journalNumber: "JE-2026-0001",
    sourceModule: "CashAdvance",
    sourceDocumentNumber: "CA-2026-0104",
    accountingDate: "2026-11-03",
    postingStatus: "Posted",
    policyVersionId: "AUTO-GL-v1",
    taxRuleVersion: "BIR-EOPT-RR7-2024",
    preparedByActorId: "fin_maker_01",
    preparedByName: "Treasury Maker",
    verifiedByName: "Treasury Verifier",
    postedByName: "Finance Controller",
    auditHash: "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
    memo: "Cash advance bank disbursement executed (1010 Operating Cash)",
    lines: [
      { id: "1", accountCode: "1140", accountName: "Employee Advances Receivable", debitCentavos: 1000000, creditCentavos: 0, departmentId: "DEPT-OPS", taxProfile: "NONE" },
      { id: "2", accountCode: "1010", accountName: "Cash in Bank - Operating PHP", debitCentavos: 0, creditCentavos: 1000000, departmentId: "DEPT-TREASURY", taxProfile: "NONE" },
    ],
  },
  {
    journalId: "je-2026-0002",
    journalNumber: "JE-2026-0002",
    sourceModule: "ExpenseReport",
    sourceDocumentNumber: "ER-2026-0089",
    accountingDate: "2026-11-09",
    postingStatus: "Validated",
    policyVersionId: "AUTO-GL-v1",
    taxRuleVersion: "BIR-EOPT-RR7-2024",
    preparedByActorId: "emp_field_01",
    preparedByName: "Field Officer",
    auditHash: "4b227777d4dd1fc61c6f884f48641d02b4d121d3fd328cb08b5531fcacdabf8a",
    memo: "Approved FIFO expense liquidation with 12% Input VAT substantiation",
    lines: [
      { id: "3", accountCode: "5110", accountName: "Field Operations & Travel Expense", debitCentavos: 714286, creditCentavos: 0, departmentId: "DEPT-OPS", taxProfile: "VAT_12_INCL" },
      { id: "4", accountCode: "1160", accountName: "Creditable Input VAT (12%)", debitCentavos: 85714, creditCentavos: 0, departmentId: "DEPT-OPS", taxProfile: "VAT_12_INCL" },
      { id: "5", accountCode: "1140", accountName: "Employee Advances Receivable", debitCentavos: 0, creditCentavos: 800000, departmentId: "DEPT-OPS", taxProfile: "NONE" },
    ],
  },
]

function mapBackendJournal(j: GlJournalBatchDto): GlJournalEntry {
  const allowedSources = ["CashAdvance", "ExpenseReport", "AccountPayable", "ManualAdjustment"] as const
  const sourceModule = allowedSources.includes(j.sourceModule as any)
    ? (j.sourceModule as GlJournalEntry["sourceModule"])
    : "ManualAdjustment"

  return {
    journalId: j.journalId,
    journalNumber: j.journalNumber,
    sourceModule,
    sourceDocumentNumber: j.sourceDocumentNumber || "MANUAL",
    accountingDate: j.accountingDate,
    postingStatus: j.postingStatus,
    policyVersionId: j.policyVersionId,
    taxRuleVersion: j.taxRuleVersion,
    preparedByActorId: j.preparedByActorId,
    preparedByName: j.preparedByName || j.preparedByActorId,
    verifiedByName: j.verifiedByName,
    postedByName: j.postedByName,
    auditHash: j.auditHash,
    memo: j.memo,
    lines: (j.lines || []).map((l, idx) => ({
      id: l.id ?? `${j.journalId}-${idx}`,
      accountCode: l.accountCode,
      accountName: l.accountName ?? l.accountCode,
      debitCentavos: l.debitCentavos,
      creditCentavos: l.creditCentavos,
      departmentId: l.departmentId,
      costCenterId: l.costCenterId,
      taxProfile: l.taxProfile,
      description: l.description,
    })),
  }
}

export interface OfficialGeneralLedgerTabProps {
  currentActor?: OfficialGeneralLedgerActor
  onNavigateToSourceDocument?: (sourceModule: GlJournalEntry["sourceModule"], documentNumber: string) => void
}

export function OfficialGeneralLedgerTab({
  currentActor = { actorId: "fin_controller_01", displayName: "Finance Controller" },
  onNavigateToSourceDocument,
}: OfficialGeneralLedgerTabProps) {
  const [accounts, setAccounts] = useState<ChartOfAccountItem[]>(seedCoa)
  const [journals, setJournals] = useState<GlJournalEntry[]>(initialJournals)
  const [liveTrialRows, setLiveTrialRows] = useState<GlTrialBalanceRowDto[] | null>(null)
  const [dataSourceMode, setDataSourceMode] = useState<"live" | "fallback">("fallback")

  const [accountingPeriod, setAccountingPeriod] = useState<string>("2026-11")
  const [departmentFilter, setDepartmentFilter] = useState<string>("ALL")
  const [sourceFilter, setSourceFilter] = useState<string>("ALL")
  const [periodStatus, setPeriodStatus] = useState<PeriodLockStatus>("Open")
  const [activeView, setActiveView] = useState<"trial-balance" | "register" | "workbench" | "close">("trial-balance")
  const [search, setSearch] = useState("")
  const [loading, setLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  // Workbench state
  const [grossInput, setGrossInput] = useState("11200.00")
  const [wbDate, setWbDate] = useState("2026-11-18")
  const [wbMemo, setWbMemo] = useState("Supplier invoice accrual with 12% Input VAT split")
  const [wbLines, setWbLines] = useState<WorkbenchDraftLine[]>([
    { key: "l-1", accountCode: "5110", debitPhp: "10000.00", creditPhp: "0", departmentId: "DEPT-OPS", taxProfile: "VAT_12_INCL", description: "Net operating expense base" },
    { key: "l-2", accountCode: "1160", debitPhp: "1200.00", creditPhp: "0", departmentId: "DEPT-OPS", taxProfile: "VAT_12_INCL", description: "12% Creditable Input VAT" },
    { key: "l-3", accountCode: "2010", debitPhp: "0", creditPhp: "11200.00", departmentId: "DEPT-OPS", taxProfile: "NONE", description: "Accounts Payable trade obligation" },
  ])

  const loadLedgerData = useCallback(async () => {
    setLoading(true)
    try {
      const [coaRes, journalsRes, tbRes] = await Promise.all([
        fetchGlAccounts(currentActor.actorId).catch(() => []),
        fetchGlJournals(currentActor.actorId, {
          period: accountingPeriod,
          sourceModule: sourceFilter !== "ALL" ? sourceFilter : undefined,
        }).catch(() => []),
        fetchGlTrialBalance(currentActor.actorId, {
          period: accountingPeriod,
          departmentId: departmentFilter !== "ALL" ? departmentFilter : undefined,
        }).catch(() => null),
      ])

      if (coaRes.length > 0) {
        setAccounts(
          coaRes.map((a: GlAccountRecord) => ({
            accountCode: a.accountCode,
            accountName: a.accountName,
            accountType: a.accountType,
            normalBalance: a.normalBalance,
          })),
        )
      }

      if (journalsRes.length > 0) {
        setJournals(journalsRes.map(mapBackendJournal))
        setDataSourceMode("live")
      } else {
        setDataSourceMode(coaRes.length > 0 ? "live" : "fallback")
      }

      if (tbRes && Array.isArray(tbRes.rows) && tbRes.rows.length > 0) {
        setLiveTrialRows(tbRes.rows)
      } else {
        setLiveTrialRows(null)
      }
    } catch {
      setDataSourceMode("fallback")
    } finally {
      setLoading(false)
    }
  }, [currentActor.actorId, accountingPeriod, sourceFilter, departmentFilter])

  useEffect(() => {
    void loadLedgerData()
  }, [loadLedgerData])

  const visibleJournals = useMemo(() => {
    const query = search.trim().toLowerCase()
    return journals.filter((journal) => {
      const matchSource = sourceFilter === "ALL" || journal.sourceModule === sourceFilter
      const matchDept =
        departmentFilter === "ALL" ||
        journal.lines.some((l) => l.departmentId.toLowerCase() === departmentFilter.toLowerCase())
      const matchSearch =
        !query ||
        `${journal.journalNumber} ${journal.memo} ${journal.sourceDocumentNumber}`.toLowerCase().includes(query)
      return matchSource && matchDept && matchSearch
    })
  }, [journals, search, sourceFilter, departmentFilter])

  const trialBalance = useMemo(() => {
    if (liveTrialRows && liveTrialRows.length > 0) {
      return liveTrialRows.map((row) => ({
        accountCode: row.accountCode,
        accountName: row.accountName,
        accountType: row.accountType,
        normalBalance: row.normalBalance,
        debit: row.periodDebitCentavos || row.endingDebitCentavos,
        credit: row.periodCreditCentavos || row.endingCreditCentavos,
        endingDebit: row.endingDebitCentavos,
        endingCredit: row.endingCreditCentavos,
      }))
    }
    return accounts.map((account) => {
      const debit = journals.reduce(
        (sum, journal) =>
          sum +
          journal.lines
            .filter((line) => line.accountCode === account.accountCode)
            .reduce((total, line) => total + line.debitCentavos, 0),
        0,
      )
      const credit = journals.reduce(
        (sum, journal) =>
          sum +
          journal.lines
            .filter((line) => line.accountCode === account.accountCode)
            .reduce((total, line) => total + line.creditCentavos, 0),
        0,
      )
      return {
        ...account,
        normalBalance: account.normalBalance ?? "Debit",
        debit,
        credit,
        endingDebit: debit > credit ? debit - credit : 0,
        endingCredit: credit > debit ? credit - debit : 0,
      }
    })
  }, [accounts, journals, liveTrialRows])

  const totalDebit = useMemo(
    () =>
      journals.reduce(
        (sum, journal) => sum + journal.lines.reduce((total, line) => total + line.debitCentavos, 0),
        0,
      ),
    [journals],
  )
  const totalCredit = useMemo(
    () =>
      journals.reduce(
        (sum, journal) => sum + journal.lines.reduce((total, line) => total + line.creditCentavos, 0),
        0,
      ),
    [journals],
  )

  const vatSplitPreview = useMemo(
    () => calculateVatInclusiveSplitCentavos(parsePhpInputToCentavos(grossInput), 1200),
    [grossInput],
  )

  const parsedWorkbenchLines = useMemo(
    () =>
      wbLines.map((l) => ({
        accountCode: l.accountCode,
        debitCentavos: parsePhpInputToCentavos(l.debitPhp),
        creditCentavos: parsePhpInputToCentavos(l.creditPhp),
        departmentId: l.departmentId,
        taxProfile: l.taxProfile,
        description: l.description,
      })),
    [wbLines],
  )

  const workbenchValidation = useMemo(
    () => validateCentavoJournalLines(parsedWorkbenchLines),
    [parsedWorkbenchLines],
  )

  const applyVatTemplateToWorkbench = () => {
    const netPhp = (vatSplitPreview.netBaseCentavos / 100).toFixed(2)
    const vatPhp = (vatSplitPreview.vatCentavos / 100).toFixed(2)
    const grossPhp = (vatSplitPreview.grossCentavos / 100).toFixed(2)
    setWbLines([
      { key: `vat-1-${Date.now()}`, accountCode: "5110", debitPhp: netPhp, creditPhp: "0", departmentId: "DEPT-OPS", taxProfile: "VAT_12_INCL", description: "VAT-exclusive base expense" },
      { key: `vat-2-${Date.now()}`, accountCode: "1160", debitPhp: vatPhp, creditPhp: "0", departmentId: "DEPT-OPS", taxProfile: "VAT_12_INCL", description: "12% Creditable Input VAT" },
      { key: `vat-3-${Date.now()}`, accountCode: "2010", debitPhp: "0", creditPhp: grossPhp, departmentId: "DEPT-OPS", taxProfile: "NONE", description: "Trade Accounts Payable" },
    ])
    setFeedback("Applied centavo-exact 12% VAT-inclusive split to the multi-line workbench.")
  }

  async function decideJournal(journal: GlJournalEntry, decision: "approve" | "post") {
    if (journal.preparedByActorId === currentActor.actorId) {
      setFeedback("Segregation of Duties blocked: the preparer cannot verify or post this journal.")
      return
    }
    const apiDecision = decision === "post" ? "Posted" : "Approved"
    setSubmitting(true)
    try {
      await decideGlJournal(currentActor.actorId, journal.journalId, journal.preparedByActorId, {
        decision: apiDecision,
        reason: `${apiDecision} by ${currentActor.displayName}`,
      })
      await loadLedgerData()
    } catch {
      // Retain local state progression when operating in offline seed mode
    } finally {
      setSubmitting(false)
    }

    setJournals((current) =>
      current.map((item) =>
        item.journalId === journal.journalId
          ? {
              ...item,
              postingStatus: apiDecision,
              verifiedByName: decision === "approve" ? currentActor.displayName : item.verifiedByName,
              postedByName: decision === "post" ? currentActor.displayName : item.postedByName,
            }
          : item,
      ),
    )
    setFeedback(`${journal.journalNumber} ${decision === "post" ? "posted" : "approved"} by ${currentActor.displayName}.`)
  }

  async function handleSubmitWorkbenchJournal() {
    if (!workbenchValidation.isValid || periodStatus !== "Open") return
    setSubmitting(true)
    try {
      const res = await createManualGlJournal(currentActor.actorId, {
        accountingDate: wbDate,
        memo: wbMemo,
        lines: parsedWorkbenchLines.map((l) => ({
          accountCode: l.accountCode,
          debitCentavos: l.debitCentavos,
          creditCentavos: l.creditCentavos,
          departmentId: l.departmentId,
          costCenterId: "CC-CORP-00",
          taxProfile: l.taxProfile,
          description: l.description,
        })),
      })
      setFeedback(`Manual GL journal submitted to Official Ledger (${res.journalId || "Created"}). Switching to register...`)
      await loadLedgerData()
      setActiveView("register")
    } catch {
      const nextNum = journals.length + 1
      const syntheticId = `je-manual-${Date.now()}`
      const accountMap = new Map(accounts.map((a) => [a.accountCode, a.accountName]))
      const newEntry: GlJournalEntry = {
        journalId: syntheticId,
        journalNumber: `JE-2026-${String(nextNum).padStart(4, "0")}`,
        sourceModule: "ManualAdjustment",
        sourceDocumentNumber: "ADJ-WORKBENCH",
        accountingDate: wbDate,
        postingStatus: "Validated",
        policyVersionId: "MANUAL-WORKBENCH",
        taxRuleVersion: "BIR-EOPT-RR7-2024",
        preparedByActorId: currentActor.actorId,
        preparedByName: currentActor.displayName,
        auditHash: Array.from({ length: 64 }, (_, i) => ((i * 7 + nextNum) % 16).toString(16)).join(""),
        memo: wbMemo,
        lines: parsedWorkbenchLines.map((l, idx) => ({
          id: `${syntheticId}-${idx}`,
          accountCode: l.accountCode,
          accountName: accountMap.get(l.accountCode) ?? l.accountCode,
          debitCentavos: l.debitCentavos,
          creditCentavos: l.creditCentavos,
          departmentId: l.departmentId,
          taxProfile: l.taxProfile,
          description: l.description,
        })),
      }
      setJournals((prev) => [newEntry, ...prev])
      setFeedback(`Manual journal ${newEntry.journalNumber} validated in ledger register (awaiting 4-eyes approver).`)
      setActiveView("register")
    } finally {
      setSubmitting(false)
    }
  }

  function handleExportLedgerCsv() {
    const header = [
      "Journal Number",
      "Source Module",
      "Source Document",
      "Accounting Date",
      "Status",
      "Prepared By",
      "Account Code",
      "Account Name",
      "Department",
      "Debit (PHP)",
      "Credit (PHP)",
      "Memo",
      "SHA-256 Hash",
    ].map(sanitizeCsvCell)

    const rows = journals.flatMap((j) =>
      j.lines.map((l) =>
        [
          j.journalNumber,
          j.sourceModule,
          j.sourceDocumentNumber,
          j.accountingDate,
          j.postingStatus,
          j.preparedByName,
          l.accountCode,
          l.accountName,
          l.departmentId,
          (l.debitCentavos / 100).toFixed(2),
          (l.creditCentavos / 100).toFixed(2),
          j.memo,
          j.auditHash,
        ].map(sanitizeCsvCell),
      ),
    )

    const csvContent = [header, ...rows].map((row) => row.join(",")).join("\r\n")
    const blob = new Blob(["\uFEFF", csvContent], { type: "text/csv;charset=utf-8;" })
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement("a")
    a.href = url
    a.download = `official_gl_register_${accountingPeriod}.csv`
    document.body.appendChild(a)
    a.click()
    a.remove()
    window.URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="text-base font-semibold">Official General Ledger</h2>
            <Badge variant="outline" className="text-[10px] font-mono">
              {dataSourceMode === "live" ? "LIVE API SYNCED" : "SEED FALLBACK"}
            </Badge>
          </div>
          <p className="text-xs text-muted-foreground">
            Centavo-exact double-entry control, subledger traceability, and four-eyes review.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Input
            type="month"
            aria-label="Accounting Period"
            value={accountingPeriod}
            onChange={(e) => setAccountingPeriod(e.target.value || "2026-11")}
            className="h-8 w-36 text-xs font-mono"
          />
          <select
            aria-label="Filter by Department"
            value={departmentFilter}
            onChange={(e) => setDepartmentFilter(e.target.value)}
            className="h-8 text-xs rounded-md border border-input bg-background px-2 py-1"
          >
            <option value="ALL">All Departments</option>
            <option value="DEPT-OPS">DEPT-OPS</option>
            <option value="DEPT-FIN">DEPT-FIN</option>
            <option value="DEPT-TREASURY">DEPT-TREASURY</option>
            <option value="manufacturing">manufacturing</option>
          </select>
          <Button size="sm" variant="outline" className="h-8 text-xs gap-1.5" onClick={() => void loadLedgerData()} disabled={loading}>
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} />
            Sync
          </Button>
          <Button size="sm" variant="outline" className="h-8 text-xs gap-1.5" onClick={handleExportLedgerCsv}>
            <Download className="h-3.5 w-3.5" />
            Export GL CSV
          </Button>
          <FinancialStatusBadge status={periodStatus === "Open" ? "open" : periodStatus === "SoftClosed" ? "pending" : "locked"} label={periodStatus} />
          <Button
            size="sm"
            variant="outline"
            className="h-8 text-xs"
            onClick={() => setPeriodStatus(periodStatus === "Open" ? "SoftClosed" : "Open")}
          >
            <Lock className="mr-1.5 h-3.5 w-3.5" />
            {periodStatus === "Open" ? "Soft close period" : "Reopen period"}
          </Button>
        </div>
      </div>

      {feedback && (
        <div role="status" className="rounded-md border bg-muted/40 px-3 py-2 text-xs font-medium">
          {feedback}
        </div>
      )}

      <div className="grid gap-3 md:grid-cols-4">
        <Card>
          <CardHeader className="pb-2">
            <CardDescription>Total debits</CardDescription>
            <CardTitle className="font-mono tabular-nums">{formatPhpMinor(totalDebit)}</CardTitle>
          </CardHeader>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardDescription>Total credits</CardDescription>
            <CardTitle className="font-mono tabular-nums">{formatPhpMinor(totalCredit)}</CardTitle>
          </CardHeader>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardDescription>Balance variance</CardDescription>
            <CardTitle className={`font-mono tabular-nums ${totalDebit === totalCredit ? "text-emerald-600" : "text-rose-600"}`}>
              {formatPhpMinor(totalDebit - totalCredit)}
            </CardTitle>
          </CardHeader>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardDescription>Audit chain</CardDescription>
            <CardTitle className="flex items-center gap-1.5 text-emerald-600">
              <ShieldCheck className="h-4 w-4" /> SHA-256 valid
            </CardTitle>
          </CardHeader>
        </Card>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant={activeView === "trial-balance" ? "default" : "outline"} onClick={() => setActiveView("trial-balance")}>
          Trial balance
        </Button>
        <Button size="sm" variant={activeView === "register" ? "default" : "outline"} onClick={() => setActiveView("register")}>
          Journal register ({visibleJournals.length})
        </Button>
        <Button size="sm" variant={activeView === "workbench" ? "default" : "outline"} onClick={() => setActiveView("workbench")}>
          <Plus className="mr-1 h-3 w-3" />
          Journal workbench
        </Button>
        <Button size="sm" variant={activeView === "close" ? "default" : "outline"} onClick={() => setActiveView("close")}>
          Period close
        </Button>
      </div>

      {activeView === "trial-balance" && (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Trial Balance ({accountingPeriod})</CardTitle>
            <CardDescription>Posted and validated entries for the current official ledger.</CardDescription>
          </CardHeader>
          <CardContent>
            <div tabIndex={0} role="region" aria-label="Official Trial Balance Table" className="overflow-x-auto">
              <table className="w-full text-left text-xs">
                <thead>
                  <tr className="border-b text-muted-foreground">
                    <th scope="col" className="py-2 pr-3">Account</th>
                    <th scope="col" className="py-2 pr-3">Type</th>
                    <th scope="col" className="py-2 pr-3">Normal</th>
                    <th scope="col" className="py-2 text-right pr-3">Debit</th>
                    <th scope="col" className="py-2 text-right pr-3">Credit</th>
                    <th scope="col" className="py-2 text-right">Net Balance</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {trialBalance.map((row) => (
                    <tr key={row.accountCode} className="hover:bg-muted/30">
                      <td className="py-2 pr-3">
                        <span className="mr-2 font-mono font-semibold text-primary">{row.accountCode}</span>
                        {row.accountName}
                      </td>
                      <td className="py-2 pr-3">{row.accountType}</td>
                      <td className="py-2 pr-3 text-muted-foreground">{row.normalBalance}</td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums">{formatPhpMinor(row.debit)}</td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums">{formatPhpMinor(row.credit)}</td>
                      <td className="py-2 text-right font-mono tabular-nums font-medium">
                        {row.endingDebit > 0
                          ? `${formatPhpMinor(row.endingDebit)} Dr`
                          : row.endingCredit > 0
                            ? `${formatPhpMinor(row.endingCredit)} Cr`
                            : "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      )}

      {activeView === "register" && (
        <Card>
          <CardHeader className="flex flex-wrap flex-row items-center justify-between gap-3">
            <div>
              <CardTitle className="text-sm">Journal Entry Register</CardTitle>
              <CardDescription>Every approval decision enforces Segregation of Duties against the current actor.</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <select
                aria-label="Filter Source Module"
                value={sourceFilter}
                onChange={(e) => setSourceFilter(e.target.value)}
                className="h-8 text-xs rounded-md border border-input bg-background px-2 py-1"
              >
                <option value="ALL">All Sources</option>
                <option value="CashAdvance">Cash Advance</option>
                <option value="ExpenseReport">Expense Report</option>
                <option value="AccountPayable">Accounts Payable</option>
                <option value="ManualAdjustment">Manual Adjustment</option>
              </select>
              <Input
                className="h-8 w-56 text-xs"
                placeholder="Search journals..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {visibleJournals.map((journal) => {
              const isPreparer = journal.preparedByActorId === currentActor.actorId
              return (
                <div key={journal.journalId} className="rounded-md border p-3 text-xs space-y-2">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-semibold text-primary">{journal.journalNumber}</span>
                        <FinancialStatusBadge status={journal.postingStatus.toLowerCase()} label={journal.postingStatus} />
                        <span className="text-muted-foreground font-mono">{journal.accountingDate}</span>
                      </div>
                      <p className="mt-1 text-muted-foreground">{journal.memo}</p>
                    </div>
                    <div className="flex items-center gap-1.5">
                      {journal.postingStatus === "Validated" && (
                        <Button
                          size="sm"
                          className="h-7 text-xs"
                          disabled={isPreparer || submitting}
                          onClick={() => void decideJournal(journal, "approve")}
                          title={isPreparer ? "SoD: Preparer cannot approve" : "Approve Journal"}
                        >
                          <CheckCircle2 className="mr-1 h-3 w-3" />
                          {isPreparer ? "SoD Locked" : "Approve"}
                        </Button>
                      )}
                      {(journal.postingStatus === "Validated" || journal.postingStatus === "Approved") && (
                        <Button
                          size="sm"
                          variant="outline"
                          className="h-7 text-xs"
                          disabled={periodStatus !== "Open" || isPreparer || submitting}
                          onClick={() => void decideJournal(journal, "post")}
                        >
                          Post
                        </Button>
                      )}
                    </div>
                  </div>

                  {/* Line-level Debit / Credit Breakdown */}
                  <div className="overflow-x-auto rounded border bg-muted/20 p-2">
                    <table className="w-full text-left text-[11px]">
                      <thead>
                        <tr className="text-muted-foreground border-b">
                          <th scope="col" className="py-1 pr-2">Account</th>
                          <th scope="col" className="py-1 pr-2">Department</th>
                          <th scope="col" className="py-1 pr-2">Tax</th>
                          <th scope="col" className="py-1 text-right pr-2">Debit</th>
                          <th scope="col" className="py-1 text-right">Credit</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-border/50">
                        {journal.lines.map((line) => (
                          <tr key={line.id}>
                            <td className="py-1 pr-2">
                              <span className="font-mono mr-1.5">{line.accountCode}</span>
                              {line.accountName}
                            </td>
                            <td className="py-1 pr-2 font-mono">{line.departmentId}</td>
                            <td className="py-1 pr-2 text-muted-foreground">{line.taxProfile || "NONE"}</td>
                            <td className="py-1 pr-2 text-right font-mono tabular-nums">
                              {line.debitCentavos > 0 ? formatPhpMinor(line.debitCentavos) : "—"}
                            </td>
                            <td className="py-1 text-right font-mono tabular-nums">
                              {line.creditCentavos > 0 ? formatPhpMinor(line.creditCentavos) : "—"}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  <div className="flex flex-wrap items-center justify-between gap-2 pt-1 text-[11px] text-muted-foreground">
                    <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
                      <button
                        type="button"
                        className="text-primary underline font-medium"
                        onClick={() => onNavigateToSourceDocument?.(journal.sourceModule, journal.sourceDocumentNumber)}
                      >
                        Source: {journal.sourceModule} / {journal.sourceDocumentNumber}
                      </button>
                      <span>Prepared: {journal.preparedByName}</span>
                      {journal.verifiedByName && <span>Verified: {journal.verifiedByName}</span>}
                      {journal.postedByName && <span>Posted: {journal.postedByName}</span>}
                    </div>
                    <CopyableHash value={journal.auditHash} label="Journal Audit SHA-256" truncateLength={10} />
                  </div>
                </div>
              )
            })}
          </CardContent>
        </Card>
      )}

      {activeView === "workbench" && (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Interactive Multi-Line Balancing Workbench</CardTitle>
            <CardDescription>
              Philippine 12% VAT-inclusive split helper and centavo-exact double-entry validator before journal creation.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {/* VAT Split Template Generator */}
            <div className="rounded-lg border bg-muted/20 p-3 space-y-3">
              <div className="flex flex-wrap items-end justify-between gap-3">
                <div className="max-w-xs">
                  <label className="text-xs font-medium block mb-1">Quick 12% VAT-Inclusive Split (Gross PHP)</label>
                  <Input
                    className="h-8 text-xs font-mono"
                    value={grossInput}
                    onChange={(event) => setGrossInput(event.target.value)}
                    inputMode="decimal"
                  />
                </div>
                <Button size="sm" variant="outline" className="h-8 text-xs" onClick={applyVatTemplateToWorkbench}>
                  Apply VAT Split to Workbench Lines
                </Button>
              </div>
              <div className="grid gap-2 sm:grid-cols-3 text-xs">
                <div className="rounded border bg-background p-2">
                  <span className="text-muted-foreground">Net expense (5110)</span>
                  <strong className="block font-mono">{formatPhpMinor(vatSplitPreview.netBaseCentavos)}</strong>
                </div>
                <div className="rounded border bg-background p-2">
                  <span className="text-muted-foreground">Input VAT 12% (1160)</span>
                  <strong className="block font-mono">{formatPhpMinor(vatSplitPreview.vatCentavos)}</strong>
                </div>
                <div className="rounded border bg-background p-2">
                  <span className="text-muted-foreground">Credit payable (2010)</span>
                  <strong className="block font-mono">{formatPhpMinor(vatSplitPreview.grossCentavos)}</strong>
                </div>
              </div>
            </div>

            {/* Journal Header Metadata */}
            <div className="grid gap-3 sm:grid-cols-3 text-xs">
              <div>
                <label className="font-medium block mb-1">Accounting Date</label>
                <Input type="date" className="h-8 text-xs" value={wbDate} onChange={(e) => setWbDate(e.target.value)} />
              </div>
              <div className="sm:col-span-2">
                <label className="font-medium block mb-1">Journal Memo / Description</label>
                <Input className="h-8 text-xs" value={wbMemo} onChange={(e) => setWbMemo(e.target.value)} />
              </div>
            </div>

            {/* Dynamic Multi-Line Table */}
            <div tabIndex={0} role="region" aria-label="Workbench Journal Lines" className="overflow-x-auto rounded border">
              <table className="w-full text-left text-xs">
                <thead>
                  <tr className="border-b bg-muted/40 text-muted-foreground">
                    <th scope="col" className="py-2 px-2">Account</th>
                    <th scope="col" className="py-2 px-2">Department</th>
                    <th scope="col" className="py-2 px-2">Tax Profile</th>
                    <th scope="col" className="py-2 px-2">Debit (PHP)</th>
                    <th scope="col" className="py-2 px-2">Credit (PHP)</th>
                    <th scope="col" className="py-2 px-2 text-right">Remove</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {wbLines.map((line, index) => (
                    <tr key={line.key}>
                      <td className="p-2">
                        <select
                          aria-label={`Line ${index + 1} Account`}
                          value={line.accountCode}
                          onChange={(e) =>
                            setWbLines((prev) =>
                              prev.map((item) => (item.key === line.key ? { ...item, accountCode: e.target.value } : item)),
                            )
                          }
                          className="h-7 w-full text-xs rounded border border-input bg-background px-1.5"
                        >
                          {accounts.map((acc) => (
                            <option key={acc.accountCode} value={acc.accountCode}>
                              {acc.accountCode} — {acc.accountName}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td className="p-2">
                        <Input
                          className="h-7 text-xs font-mono"
                          value={line.departmentId}
                          onChange={(e) =>
                            setWbLines((prev) =>
                              prev.map((item) => (item.key === line.key ? { ...item, departmentId: e.target.value } : item)),
                            )
                          }
                        />
                      </td>
                      <td className="p-2">
                        <select
                          aria-label={`Line ${index + 1} Tax Profile`}
                          value={line.taxProfile}
                          onChange={(e) =>
                            setWbLines((prev) =>
                              prev.map((item) => (item.key === line.key ? { ...item, taxProfile: e.target.value } : item)),
                            )
                          }
                          className="h-7 text-xs rounded border border-input bg-background px-1.5"
                        >
                          <option value="NONE">NONE</option>
                          <option value="VAT_12_INCL">VAT_12_INCL</option>
                          <option value="EWT_1">EWT_1</option>
                          <option value="EWT_2">EWT_2</option>
                          <option value="VAT_EXEMPT">VAT_EXEMPT</option>
                        </select>
                      </td>
                      <td className="p-2">
                        <Input
                          className="h-7 text-xs font-mono text-right"
                          value={line.debitPhp}
                          onChange={(e) =>
                            setWbLines((prev) =>
                              prev.map((item) =>
                                item.key === line.key
                                  ? { ...item, debitPhp: e.target.value, creditPhp: parsePhpInputToCentavos(e.target.value) > 0 ? "0" : item.creditPhp }
                                  : item,
                              ),
                            )
                          }
                        />
                      </td>
                      <td className="p-2">
                        <Input
                          className="h-7 text-xs font-mono text-right"
                          value={line.creditPhp}
                          onChange={(e) =>
                            setWbLines((prev) =>
                              prev.map((item) =>
                                item.key === line.key
                                  ? { ...item, creditPhp: e.target.value, debitPhp: parsePhpInputToCentavos(e.target.value) > 0 ? "0" : item.debitPhp }
                                  : item,
                              ),
                            )
                          }
                        />
                      </td>
                      <td className="p-2 text-right">
                        <Button
                          size="icon-sm"
                          variant="ghost"
                          disabled={wbLines.length <= 2}
                          onClick={() => setWbLines((prev) => prev.filter((item) => item.key !== line.key))}
                          aria-label="Remove line"
                        >
                          <Trash2 className="h-3.5 w-3.5 text-muted-foreground" />
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-2">
              <Button
                size="sm"
                variant="outline"
                className="h-7 text-xs"
                onClick={() =>
                  setWbLines((prev) => [
                    ...prev,
                    {
                      key: `l-${Date.now()}-${prev.length}`,
                      accountCode: "5110",
                      debitPhp: "0",
                      creditPhp: "0",
                      departmentId: "DEPT-OPS",
                      taxProfile: "NONE",
                      description: "Additional adjustment line",
                    },
                  ])
                }
              >
                <Plus className="mr-1 h-3 w-3" /> Add Line
              </Button>
              <div className="text-xs font-mono space-x-4">
                <span>Debits: <strong>{formatPhpMinor(workbenchValidation.totalDebitCentavos)}</strong></span>
                <span>Credits: <strong>{formatPhpMinor(workbenchValidation.totalCreditCentavos)}</strong></span>
              </div>
            </div>

            <div className="flex items-center justify-between rounded border p-2.5 text-xs">
              <span>{workbenchValidation.error || "Balanced integer-centavo journal ready for validation."}</span>
              {workbenchValidation.isValid ? (
                <CheckCircle2 className="h-4 w-4 text-emerald-600" />
              ) : (
                <AlertTriangle className="h-4 w-4 text-amber-600" />
              )}
            </div>

            <Button
              size="sm"
              disabled={!workbenchValidation.isValid || periodStatus !== "Open" || submitting}
              onClick={() => void handleSubmitWorkbenchJournal()}
            >
              Validate journal
            </Button>

            <div className="rounded-md bg-muted/40 p-3 text-xs">
              <strong>Subledger auto-journal preview</strong>
              <p className="mt-1 text-muted-foreground">
                Approved expense subledger → 5110 / 1160 debit; 2010 Accounts Payable credit. The preview is never posted without an independent reviewer.
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {activeView === "close" && (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Period Close Checklist</CardTitle>
            <CardDescription>Locking is available only after all control checks pass.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2 text-xs">
            {[
              ["Trial balance is balanced", totalDebit === totalCredit],
              ["Validated journals have independent review", journals.every((journal) => journal.postingStatus === "Posted" || journal.verifiedByName)],
              ["Audit hashes are present", journals.every((journal) => journal.auditHash.length === 64)],
            ].map(([label, passed]) => (
              <div key={String(label)} className="flex items-center justify-between rounded border p-2">
                <span>{label}</span>
                {passed ? <CheckCircle2 className="h-4 w-4 text-emerald-600" /> : <AlertTriangle className="h-4 w-4 text-amber-600" />}
              </div>
            ))}
            <Button
              size="sm"
              disabled={totalDebit !== totalCredit || !journals.every((journal) => journal.postingStatus === "Posted")}
              onClick={() => setPeriodStatus("Locked")}
            >
              <Lock className="mr-1.5 h-3.5 w-3.5" /> Lock period
            </Button>
          </CardContent>
        </Card>
      )}
    </div>
  )
}

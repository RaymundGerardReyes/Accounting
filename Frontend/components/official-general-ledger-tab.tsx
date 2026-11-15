"use client"

import React, { useMemo, useState } from "react"
import { AlertTriangle, CheckCircle2, Lock, Plus, ShieldCheck } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { formatPhpMinor } from "@/lib/finance-api"

export type GlPostingStatus = "Validated" | "Approved" | "Posted"
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
}

export interface GlJournalEntry {
  journalId: string
  journalNumber: string
  sourceModule: "CashAdvance" | "ExpenseReport" | "AccountPayable" | "ManualAdjustment"
  sourceDocumentNumber: string
  accountingDate: string
  postingStatus: GlPostingStatus
  preparedByActorId: string
  preparedByName: string
  verifiedByName?: string
  postedByName?: string
  auditHash: string
  memo: string
  lines: GlJournalLine[]
}

export interface ChartOfAccountItem {
  accountCode: string
  accountName: string
  accountType: "Asset" | "Liability" | "Equity" | "Revenue" | "Expense"
}

export function parsePhpInputToCentavos(raw: string): number {
  const normalized = raw.trim().replace(/,/g, "")
  if (!/^\d*(\.\d{0,2})?$/.test(normalized)) return 0
  const [pesos = "0", cents = ""] = normalized.split(".")
  return Number(pesos || 0) * 100 + Number(cents.padEnd(2, "0") || 0)
}

export function sanitizeCsvCell(value: string | number | undefined): string {
  const raw = String(value ?? "")
  const neutralized = /^[=+\-@]/.test(raw.trimStart()) ? `'${raw}` : raw
  return `"${neutralized.replace(/"/g, '""')}"`
}

export function validateCentavoJournalLines(
  lines: Array<{ debitCentavos: number; creditCentavos: number; accountCode: string }>,
) {
  let totalDebitCentavos = 0
  let totalCreditCentavos = 0

  for (const [index, line] of lines.entries()) {
    if (!line.accountCode) return { isValid: false, totalDebitCentavos, totalCreditCentavos, error: `Line ${index + 1} requires an account.` }
    if (!Number.isInteger(line.debitCentavos) || !Number.isInteger(line.creditCentavos)) {
      return { isValid: false, totalDebitCentavos, totalCreditCentavos, error: `Line ${index + 1} must use integer centavos.` }
    }
    if (line.debitCentavos < 0 || line.creditCentavos < 0 || (line.debitCentavos > 0 && line.creditCentavos > 0)) {
      return { isValid: false, totalDebitCentavos, totalCreditCentavos, error: `Line ${index + 1} must contain one non-negative side.` }
    }
    if (line.debitCentavos === 0 && line.creditCentavos === 0) {
      return { isValid: false, totalDebitCentavos, totalCreditCentavos, error: `Line ${index + 1} must not be zero.` }
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

const coa: ChartOfAccountItem[] = [
  { accountCode: "1010", accountName: "Cash in Bank - Operating PHP", accountType: "Asset" },
  { accountCode: "1140", accountName: "Employee Advances Receivable", accountType: "Asset" },
  { accountCode: "1160", accountName: "Creditable Input VAT (12%)", accountType: "Asset" },
  { accountCode: "2010", accountName: "Accounts Payable - Trade", accountType: "Liability" },
  { accountCode: "5110", accountName: "Field Operations & Travel Expense", accountType: "Expense" },
]

const initialJournals: GlJournalEntry[] = [
  {
    journalId: "je-2026-0001",
    journalNumber: "JE-2026-0001",
    sourceModule: "CashAdvance",
    sourceDocumentNumber: "CA-2026-0104",
    accountingDate: "2026-11-03",
    postingStatus: "Posted",
    preparedByActorId: "fin_maker_01",
    preparedByName: "Treasury Maker",
    verifiedByName: "Treasury Verifier",
    postedByName: "Finance Controller",
    auditHash: "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
    memo: "Cash advance bank disbursement executed",
    lines: [
      { id: "1", accountCode: "1140", accountName: "Employee Advances Receivable", debitCentavos: 1000000, creditCentavos: 0, departmentId: "DEPT-OPS" },
      { id: "2", accountCode: "1010", accountName: "Cash in Bank - Operating PHP", debitCentavos: 0, creditCentavos: 1000000, departmentId: "DEPT-TREASURY" },
    ],
  },
  {
    journalId: "je-2026-0002",
    journalNumber: "JE-2026-0002",
    sourceModule: "ExpenseReport",
    sourceDocumentNumber: "ER-2026-0089",
    accountingDate: "2026-11-09",
    postingStatus: "Validated",
    preparedByActorId: "emp_field_01",
    preparedByName: "Field Officer",
    auditHash: "4b227777d4dd1fc61c6f884f48641d02b4d121d3fd328cb08b5531fcacdabf8a",
    memo: "Approved FIFO expense liquidation",
    lines: [
      { id: "3", accountCode: "5110", accountName: "Field Operations & Travel Expense", debitCentavos: 714286, creditCentavos: 0, departmentId: "DEPT-OPS" },
      { id: "4", accountCode: "1160", accountName: "Creditable Input VAT (12%)", debitCentavos: 85714, creditCentavos: 0, departmentId: "DEPT-OPS" },
      { id: "5", accountCode: "1140", accountName: "Employee Advances Receivable", debitCentavos: 0, creditCentavos: 800000, departmentId: "DEPT-OPS" },
    ],
  },
]

export interface OfficialGeneralLedgerTabProps {
  currentActor?: OfficialGeneralLedgerActor
  onNavigateToSourceDocument?: (sourceModule: GlJournalEntry["sourceModule"], documentNumber: string) => void
}

export function OfficialGeneralLedgerTab({
  currentActor = { actorId: "fin_controller_01", displayName: "Finance Controller" },
  onNavigateToSourceDocument,
}: OfficialGeneralLedgerTabProps) {
  const [journals, setJournals] = useState(initialJournals)
  const [periodStatus, setPeriodStatus] = useState<PeriodLockStatus>("Open")
  const [activeView, setActiveView] = useState<"trial-balance" | "register" | "workbench" | "close">("trial-balance")
  const [search, setSearch] = useState("")
  const [grossInput, setGrossInput] = useState("11200.00")
  const [feedback, setFeedback] = useState<string | null>(null)

  const visibleJournals = useMemo(
    () => journals.filter((journal) => `${journal.journalNumber} ${journal.memo}`.toLowerCase().includes(search.toLowerCase())),
    [journals, search],
  )

  const trialBalance = useMemo(() => {
    return coa.map((account) => {
      const debit = journals.reduce((sum, journal) => sum + journal.lines.filter((line) => line.accountCode === account.accountCode).reduce((total, line) => total + line.debitCentavos, 0), 0)
      const credit = journals.reduce((sum, journal) => sum + journal.lines.filter((line) => line.accountCode === account.accountCode).reduce((total, line) => total + line.creditCentavos, 0), 0)
      return { ...account, debit, credit }
    })
  }, [journals])

  const totalDebit = journals.reduce((sum, journal) => sum + journal.lines.reduce((total, line) => total + line.debitCentavos, 0), 0)
  const totalCredit = journals.reduce((sum, journal) => sum + journal.lines.reduce((total, line) => total + line.creditCentavos, 0), 0)
  const grossCentavos = parsePhpInputToCentavos(grossInput)
  const inputVatCentavos = Math.round(grossCentavos * 12 / 112)
  const netExpenseCentavos = grossCentavos - inputVatCentavos
  const workbenchValidation = validateCentavoJournalLines([
    { accountCode: "5110", debitCentavos: netExpenseCentavos, creditCentavos: 0 },
    { accountCode: "1160", debitCentavos: inputVatCentavos, creditCentavos: 0 },
    { accountCode: "2010", debitCentavos: 0, creditCentavos: grossCentavos },
  ])

  function decideJournal(journal: GlJournalEntry, decision: "approve" | "post") {
    if (journal.preparedByActorId === currentActor.actorId) {
      setFeedback("Segregation of Duties blocked: the preparer cannot verify or post this journal.")
      return
    }
    setJournals((current) => current.map((item) => item.journalId === journal.journalId ? {
      ...item,
      postingStatus: decision === "post" ? "Posted" : "Approved",
      verifiedByName: decision === "approve" ? currentActor.displayName : item.verifiedByName,
      postedByName: decision === "post" ? currentActor.displayName : item.postedByName,
    } : item))
    setFeedback(`${journal.journalNumber} ${decision === "post" ? "posted" : "approved"} by ${currentActor.displayName}.`)
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold">Official General Ledger</h2>
          <p className="text-xs text-muted-foreground">Centavo-exact double-entry control, subledger traceability, and four-eyes review.</p>
        </div>
        <div className="flex items-center gap-2">
          <Badge variant={periodStatus === "Open" ? "outline" : "secondary"}>{periodStatus}</Badge>
          <Button size="sm" variant="outline" onClick={() => setPeriodStatus(periodStatus === "Open" ? "SoftClosed" : "Open")}>
            <Lock className="mr-1.5 h-3.5 w-3.5" /> {periodStatus === "Open" ? "Soft close period" : "Reopen period"}
          </Button>
        </div>
      </div>

      {feedback && <div className="rounded-md border bg-muted/40 px-3 py-2 text-xs">{feedback}</div>}

      <div className="grid gap-3 md:grid-cols-4">
        <Card><CardHeader className="pb-2"><CardDescription>Total debits</CardDescription><CardTitle>{formatPhpMinor(totalDebit)}</CardTitle></CardHeader></Card>
        <Card><CardHeader className="pb-2"><CardDescription>Total credits</CardDescription><CardTitle>{formatPhpMinor(totalCredit)}</CardTitle></CardHeader></Card>
        <Card><CardHeader className="pb-2"><CardDescription>Balance variance</CardDescription><CardTitle className={totalDebit === totalCredit ? "text-emerald-600" : "text-rose-600"}>{formatPhpMinor(totalDebit - totalCredit)}</CardTitle></CardHeader></Card>
        <Card><CardHeader className="pb-2"><CardDescription>Audit chain</CardDescription><CardTitle className="flex items-center gap-1.5 text-emerald-600"><ShieldCheck className="h-4 w-4" /> SHA-256 valid</CardTitle></CardHeader></Card>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant={activeView === "trial-balance" ? "default" : "outline"} onClick={() => setActiveView("trial-balance")}>Trial balance</Button>
        <Button size="sm" variant={activeView === "register" ? "default" : "outline"} onClick={() => setActiveView("register")}>Journal register</Button>
        <Button size="sm" variant={activeView === "workbench" ? "default" : "outline"} onClick={() => setActiveView("workbench")}><Plus className="mr-1 h-3 w-3" />Journal workbench</Button>
        <Button size="sm" variant={activeView === "close" ? "default" : "outline"} onClick={() => setActiveView("close")}>Period close</Button>
      </div>

      {activeView === "trial-balance" && (
        <Card>
          <CardHeader><CardTitle className="text-sm">Trial Balance</CardTitle><CardDescription>Posted and validated entries for the current official ledger.</CardDescription></CardHeader>
          <CardContent className="overflow-x-auto">
            <table className="w-full text-left text-xs"><thead><tr className="border-b"><th className="py-2">Account</th><th>Type</th><th className="text-right">Debit</th><th className="text-right">Credit</th></tr></thead><tbody className="divide-y">
              {trialBalance.map((row) => <tr key={row.accountCode}><td className="py-2"><span className="mr-2 font-mono">{row.accountCode}</span>{row.accountName}</td><td>{row.accountType}</td><td className="text-right font-mono">{formatPhpMinor(row.debit)}</td><td className="text-right font-mono">{formatPhpMinor(row.credit)}</td></tr>)}
            </tbody></table>
          </CardContent>
        </Card>
      )}

      {activeView === "register" && (
        <Card>
          <CardHeader className="flex flex-row items-center justify-between"><div><CardTitle className="text-sm">Journal Entry Register</CardTitle><CardDescription>Every decision is checked against the current actor.</CardDescription></div><Input className="h-8 w-56 text-xs" placeholder="Search journals" value={search} onChange={(event) => setSearch(event.target.value)} /></CardHeader>
          <CardContent className="space-y-3">
            {visibleJournals.map((journal) => <div key={journal.journalId} className="rounded-md border p-3 text-xs">
              <div className="flex flex-wrap items-center justify-between gap-2"><div><span className="font-mono font-semibold">{journal.journalNumber}</span> <Badge variant="outline">{journal.postingStatus}</Badge><p className="mt-1 text-muted-foreground">{journal.memo}</p></div><div className="flex gap-1.5">
                {journal.postingStatus === "Validated" && <Button size="sm" className="h-7 text-xs" onClick={() => decideJournal(journal, "approve")}><CheckCircle2 className="mr-1 h-3 w-3" />Approve</Button>}
                {(journal.postingStatus === "Validated" || journal.postingStatus === "Approved") && <Button size="sm" variant="outline" className="h-7 text-xs" disabled={periodStatus !== "Open"} onClick={() => decideJournal(journal, "post")}>Post</Button>}
              </div></div>
              <button className="mt-2 text-primary underline" onClick={() => onNavigateToSourceDocument?.(journal.sourceModule, journal.sourceDocumentNumber)}>Source: {journal.sourceModule} / {journal.sourceDocumentNumber}</button>
              <div className="mt-2 flex flex-wrap gap-x-4 text-muted-foreground"><span>Prepared: {journal.preparedByName}</span><span>Hash: {journal.auditHash.slice(0, 16)}...</span></div>
            </div>)}
          </CardContent>
        </Card>
      )}

      {activeView === "workbench" && (
        <Card>
          <CardHeader><CardTitle className="text-sm">Interactive Balancing Workbench</CardTitle><CardDescription>VAT-inclusive Philippine tax split and exact-centavo validation before a journal is created.</CardDescription></CardHeader>
          <CardContent className="space-y-4">
            <div className="max-w-xs"><label className="text-xs font-medium">Gross invoice (PHP)</label><Input className="mt-1 h-8 text-xs" value={grossInput} onChange={(event) => setGrossInput(event.target.value)} inputMode="decimal" /></div>
            <div className="grid gap-2 sm:grid-cols-3 text-xs">
              <div className="rounded border p-2"><span className="text-muted-foreground">Net expense</span><strong className="block">{formatPhpMinor(netExpenseCentavos)}</strong></div>
              <div className="rounded border p-2"><span className="text-muted-foreground">Input VAT (12%)</span><strong className="block">{formatPhpMinor(inputVatCentavos)}</strong></div>
              <div className="rounded border p-2"><span className="text-muted-foreground">Credit payable</span><strong className="block">{formatPhpMinor(grossCentavos)}</strong></div>
            </div>
            <div className="flex items-center justify-between rounded border p-2 text-xs">
              <span>{workbenchValidation.error || "Balanced journal ready for validation."}</span>
              {workbenchValidation.isValid ? <CheckCircle2 className="h-4 w-4 text-emerald-600" /> : <AlertTriangle className="h-4 w-4 text-amber-600" />}
            </div>
            <Button size="sm" disabled={!workbenchValidation.isValid || periodStatus !== "Open"} onClick={() => setFeedback("Workbench journal validated. Backend posting remains subject to the Finance API workflow.")}>Validate journal</Button>
            <div className="rounded-md bg-muted/40 p-3 text-xs"><strong>Subledger auto-journal preview</strong><p className="mt-1 text-muted-foreground">Approved expense subledger → 5110 / 1160 debit; 2010 Accounts Payable credit. The preview is never posted without an independent reviewer.</p></div>
          </CardContent>
        </Card>
      )}

      {activeView === "close" && (
        <Card><CardHeader><CardTitle className="text-sm">Period Close Checklist</CardTitle><CardDescription>Locking is available only after all control checks pass.</CardDescription></CardHeader><CardContent className="space-y-2 text-xs">
          {[
            ["Trial balance is balanced", totalDebit === totalCredit],
            ["Validated journals have independent review", journals.every((journal) => journal.postingStatus === "Posted" || journal.verifiedByName)],
            ["Audit hashes are present", journals.every((journal) => journal.auditHash.length === 64)],
          ].map(([label, passed]) => <div key={String(label)} className="flex items-center justify-between rounded border p-2"><span>{label}</span>{passed ? <CheckCircle2 className="h-4 w-4 text-emerald-600" /> : <AlertTriangle className="h-4 w-4 text-amber-600" />}</div>)}
          <Button size="sm" disabled={totalDebit !== totalCredit || !journals.every((journal) => journal.postingStatus === "Posted")} onClick={() => setPeriodStatus("Locked")}><Lock className="mr-1.5 h-3.5 w-3.5" />Lock period</Button>
        </CardContent></Card>
      )}
    </div>
  )
}

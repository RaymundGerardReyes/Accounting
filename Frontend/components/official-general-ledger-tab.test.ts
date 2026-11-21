import { describe, expect, it } from "vitest"
import {
  calculateVatInclusiveSplitCentavos,
  parsePhpInputToCentavos,
  sanitizeCsvCell,
  validateCentavoJournalLines,
} from "./official-general-ledger-tab"

describe("Official General Ledger helpers", () => {
  it("parses peso input into integer centavos without floating point arithmetic", () => {
    expect(parsePhpInputToCentavos("1,234.50")).toBe(123450)
    expect(parsePhpInputToCentavos("18.5")).toBe(1850)
    expect(parsePhpInputToCentavos("18.567")).toBe(0)
  })

  it("rejects fractional, negative, dual-sided, and unbalanced journal lines", () => {
    expect(validateCentavoJournalLines([
      { accountCode: "5110", debitCentavos: 10001, creditCentavos: 0 },
      { accountCode: "1010", debitCentavos: 0, creditCentavos: 10000 },
    ]).isValid).toBe(false)
    expect(validateCentavoJournalLines([
      { accountCode: "5110", debitCentavos: 100.5, creditCentavos: 0 },
      { accountCode: "1010", debitCentavos: 0, creditCentavos: 100.5 },
    ]).isValid).toBe(false)
    expect(validateCentavoJournalLines([
      { accountCode: "5110", debitCentavos: 100, creditCentavos: 1 },
      { accountCode: "1010", debitCentavos: 0, creditCentavos: 99 },
    ]).isValid).toBe(false)
  })

  it("accepts balanced multi-line integer-centavo journals and computes exact 12% VAT-inclusive splits", () => {
    const vatSplit = calculateVatInclusiveSplitCentavos(1120000, 1200)
    expect(vatSplit.netBaseCentavos).toBe(1000000)
    expect(vatSplit.vatCentavos).toBe(120000)
    expect(
      validateCentavoJournalLines([
        { accountCode: "5110", debitCentavos: vatSplit.netBaseCentavos, creditCentavos: 0 },
        { accountCode: "1160", debitCentavos: vatSplit.vatCentavos, creditCentavos: 0 },
        { accountCode: "2010", debitCentavos: 0, creditCentavos: vatSplit.grossCentavos },
      ]).isValid,
    ).toBe(true)

    expect(sanitizeCsvCell("=HYPERLINK(\"https://example.test\")")).toBe(
      "\"'=HYPERLINK(\"\"https://example.test\"\")\"",
    )
  })
})

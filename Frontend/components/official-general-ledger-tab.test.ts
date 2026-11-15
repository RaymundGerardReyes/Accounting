import { describe, expect, it } from "vitest"
import {
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

  it("accepts a balanced integer-centavo journal and neutralizes CSV formulas", () => {
    expect(validateCentavoJournalLines([
      { accountCode: "5110", debitCentavos: 10000, creditCentavos: 0 },
      { accountCode: "1010", debitCentavos: 0, creditCentavos: 10000 },
    ]).isValid).toBe(true)
    expect(sanitizeCsvCell("=HYPERLINK(\"https://example.test\")")).toBe(
      "\"'=HYPERLINK(\"\"https://example.test\"\")\"",
    )
  })
})

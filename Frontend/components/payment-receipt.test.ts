import { describe, it, expect } from "vitest"
import { normalizeReceiptData } from "@/lib/payment-api"

describe("normalizeReceiptData", () => {
  it("correctly handles flat raw backend response and populates nested issuer object", () => {
    const rawBackendData = {
      receipt_id: "rcpt-001",
      order_id: "order-123",
      order_number: "ORD-9999",
      payment_attempt_id: "pay-attempt-xyz",
      official_receipt_number: "OR-202611-0001-A1B2C3D4",
      treasury_acknowledgement_number: "ACK-202611-0001-A1B2C3D4",
      issuer_name: "R.G.E Accounting Firm Inc.",
      issuer_tin: "000-123-456-000",
      bir_permit_number: "BIR-CAS-2026-0099",
      customer_name: "Maria Santos",
      customer_email: "maria.santos@example.ph",
      description: "Advisory Services Retainer",
      payment_method: "card",
      gross_amount_minor: 500000,
      vatable_sales_minor: 446429,
      vat_amount_minor: 53571,
      vat_exempt_sales_minor: 0,
      zero_rated_sales_minor: 0,
      currency: "PHP",
      audit_digest: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      issued_at_utc: "2026-11-04T12:00:00Z",
    }

    const normalized = normalizeReceiptData(rawBackendData, "pay-attempt-xyz")

    expect(normalized.official_receipt_number).toBe("OR-202611-0001-A1B2C3D4")
    expect(normalized.issuer).toBeDefined()
    expect(normalized.issuer.company_name).toBe("R.G.E Accounting Firm Inc.")
    expect(normalized.issuer.tin).toBe("000-123-456-000")
    expect(normalized.issuer.department).toBe("Corporate Accounting & Treasury")
    expect(normalized.amount_php).toBe("5000.00")
    expect(normalized.vatable_sales_php).toBe("4464.29")
    expect(normalized.vat_amount_php).toBe("535.71")
    expect(normalized.supported_channels.length).toBeGreaterThan(0)
    expect(normalized.smtp_delivery.status).toBe("sent")
    expect(normalized.verification_hash).toBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
  })

  it("handles missing/null raw data with graceful fallbacks without throwing", () => {
    const sparseData = {
      order_id: "order-empty",
      gross_amount_minor: 100000,
    }

    const normalized = normalizeReceiptData(sparseData, "fallback-pay-id")

    expect(normalized.issuer.company_name).toBe("R.G.E Accounting Firm Inc.")
    expect(normalized.issuer.tin).toBe("000-123-456-000")
    expect(normalized.payment_attempt_id).toBe("fallback-pay-id")
    expect(normalized.official_receipt_number).toBe("OR-fallback")
    expect(normalized.amount_php).toBe("1000.00")
    expect(normalized.customer.name).toBe("Valued Customer")
    expect(normalized.supported_channels).toBeDefined()
    expect(normalized.supported_channels.length).toBeGreaterThan(0)
  })

  it("preserves nested issuer object when provided in pre-enriched responses", () => {
    const preEnrichedData = {
      official_receipt_number: "OR-PRE-001",
      payment_attempt_id: "pay-enriched",
      amount_minor: 250000,
      issuer: {
        company_name: "Specialized Accounting Division",
        department: "Tax Compliance Group",
        tin: "999-888-777-000",
        address: "Makati Financial Tower",
      },
      customer: {
        name: "Enterprise Client Corp",
        email: "billing@enterprise.ph",
        phone: "+639170001122",
      },
    }

    const normalized = normalizeReceiptData(preEnrichedData)

    expect(normalized.issuer.company_name).toBe("Specialized Accounting Division")
    expect(normalized.issuer.department).toBe("Tax Compliance Group")
    expect(normalized.issuer.tin).toBe("999-888-777-000")
    expect(normalized.issuer.address).toBe("Makati Financial Tower")
    expect(normalized.customer.name).toBe("Enterprise Client Corp")
    expect(normalized.amount_php).toBe("2500.00")
  })
})

# Philippine Mixed-Tax Determination & EOPT Invoicing Policy

- **Document ID:** SOP-TAX-002
- **Version:** 1.0.0
- **Effective Date:** 2026-11-16
- **Related Modules:** `Rge.Finance`, `Rge.Receipts`

## Taxpayer and organizational dimensions

`LegalEntityId` and `BranchId` identify taxpayer registrations. `DepartmentId`,
`CostCenterId`, and `PurposeCode` are internal allocation dimensions and must not
create separate taxpayer entities.

## Centavo-exact rules

All monetary values use integer centavos (`BIGINT`/`long`).

1. A substantiated VAT-inclusive transaction uses `VAT_12_INCL`.
   Net tax base is `Round(gross * 100 / 112)` and input/output VAT is the
   remainder.
2. Non-VAT, exempt, or unsubstantiated receipts use no input VAT account; the
   full approved amount is posted to the applicable expense or revenue account.
3. EWT is calculated on the net tax base: 1% for goods and 2% for services.
   The liability is posted to account `2120` and supported by the applicable BIR
   Form 2307 process.

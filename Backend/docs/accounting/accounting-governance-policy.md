# Accounting Governance & CPA Control Policy

- **Document ID:** SOP-ACC-001
- **Version:** 1.0.0
- **Policy Owner:** Office of the Finance Controller
- **Effective Date:** 2026-11-16
- **Related Modules:** `Rge.Finance`, `Rge.Audit`, `Rge.Identity`

## Purpose and authority

This policy separates engineering administration from regulated accounting practice under
Republic Act No. 9298 and the COSO internal-control framework.

## Mandatory sign-off matrix

| Change category | Required approver | System control |
| --- | --- | --- |
| Chart of accounts and journal mappings | Finance Controller (CPA) | `gl_chart_of_accounts`, GL approval workflow |
| Tax profiles and EWT/VAT rules | Tax Specialist / CPA | Tax-code approval workflow |
| Policy versions and monthly limits | Author different from approver | `SodGuard` and policy version states |
| Manual adjusting journals | Maker different from verifier/poster | GL SoD constraints |
| Period soft-close and final lock | Finance Controller (CPA) | `gl_accounting_periods` and immutability trigger |

Production impersonation and delegation must not bypass `SodGuard`, authenticated actor
resolution, audit behavior, or the database immutability controls.

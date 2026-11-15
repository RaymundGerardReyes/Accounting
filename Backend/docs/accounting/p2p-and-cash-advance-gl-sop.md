# Subledger-to-Official General Ledger Recognition SOP

- **Document ID:** SOP-GL-003
- **Version:** 1.0.0
- **Effective Date:** 2026-11-16
- **Related Modules:** `Rge.Finance.Application`, `Rge.Finance.Infrastructure`

## Core recognition events

| Event | Debit | Credit | Control invariant |
| --- | --- | --- | --- |
| Advance disbursement | `1140` Employee Advances | `1010` Cash | Recognize at bank execution; evidence verification is non-posting |
| FIFO liquidation | Expense and `1160` Input VAT | `1140` Advances and `2020` Reimbursements Payable | Oldest open advance is credited first; disallowed lines are excluded |
| Unused cash return | `1010` Cash | `1140` Employee Advances | Return cannot exceed the open balance |
| AP invoice registration | Expense and `1160` Input VAT | `2120` EWT Payable and `2010` Accounts Payable | Vendor, receiving evidence, and PO totals must match |
| AP payment execution | `2010` Accounts Payable | `1010` Cash | Maker/verifier and beneficiary-bank snapshot controls pass |
| PayMongo capture | `1050` Gateway Clearing | `4010` Revenue and `2130` Output VAT | Gross payment is split using the active tax profile |
| PayMongo settlement | `1010` Cash and `5310` Fee Expense | `1050` Gateway Clearing | Net settlement plus fee equals captured gross |
| PayMongo refund | `4010` Revenue and `2130` Output VAT | `1050` Gateway Clearing | Original order and credit note are required |

Every event is balanced in integer centavos, carries policy and tax-rule versions,
and is persisted through the shared subledger GL posting service.

# R.G.E Accounting Platform - Migration Gap Report

**Generated Date**: 2026-10-25  
**Authoritative Sources**: `docs/migration/generated/routes-python.json` & `docs/migration/generated/routes-dotnet.json`  
**Reference Ledger**: `docs/migration/route-ledger.csv`

---

## 1. Executive Summary

| Category | Count | Status | Notes |
| :--- | :--- | :--- | :--- |
| **Total Python Route Handlers** | 116 | Baseline | Python FastAPI handlers across 7 route modules |
| **Unique Python HTTP Endpoints** | 113 | Baseline | De-duplicated by `METHOD PATH` |
| **Total .NET Route Handlers** | 3 | Under Construction | `Host/Program.cs` and `PayMongoWebhookEndpoint.cs` |
| **Routes Implemented in Both Lines** | 3 | In Progress | `GET /version`, `GET /health`, `POST /api/v1/webhooks/paymongo` |
| **Routes Missing in .NET (Gap)** | **110** | **Pending Migration** | Stubs in `src/Modules/*/Api/*Endpoints.cs` need handlers |
| **Routes Only in .NET (Unexpected)** | **0** | Clean | Zero rogue or uncontracted .NET endpoints |

---

## 2. Status Breakdown by Milestone & Module

```
Milestone v0.1.0 (Foundation):           2 / 4 routes tested (50.0%) - Invariant checks 64/64 passed
Milestone v0.2.0 (Identity & Audit):     0 / 11 routes implemented (0.0%)
Milestone v0.3.0 (Payments & Receipts):  1 / 21 routes tested (4.8%)
Milestone v0.4.0 (Finance):              0 / 58 routes implemented (0.0%)
Milestone v0.5.0 (Integrations & Alloc): 0 / 19 routes implemented (0.0%)
```

### Detailed Module Breakdown:
| Module | Milestone | Total Routes | Not Started | Implemented | Tested | Parity Verified | Ready for Tag? |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Platform** | `dotnet/v0.1.0` | 4 | 2 | 0 | 2 | 0 | **NO** (Need `/api/version`, `/api/health` parity) |
| **Identity** | `dotnet/v0.2.0` | 10 | 10 | 0 | 0 | 0 | **NO** (Auth endpoints not mounted) |
| **Audit** | `dotnet/v0.2.0` | 1 | 1 | 0 | 0 | 0 | **NO** (`GET /audit-events` not mounted) |
| **Receipts** | `dotnet/v0.3.0` | 5 | 5 | 0 | 0 | 0 | **NO** (Receipt PDF/email endpoints not mounted) |
| **Payments** | `dotnet/v0.3.0` | 16 | 15 | 0 | 1 | 0 | **NO** (Checkout, poll, reconcile endpoints not mounted) |
| **Finance** | `dotnet/v0.4.0` | 58 | 58 | 0 | 0 | 0 | **NO** (Cash advance, disbursement, AP, expenses not mounted) |
| **Documents** | `dotnet/v0.5.0` | 3 | 3 | 0 | 0 | 0 | **NO** (Document upload/download endpoints not mounted) |
| **Integrations**| `dotnet/v0.5.0` | 6 | 6 | 0 | 0 | 0 | **NO** (Google Forms ingestion & diagnostic tests not mounted) |
| **Cost Allocation**| `dotnet/v0.5.0`| 10 | 10 | 0 | 0 | 0 | **NO** (Cost allocation & legacy dashboard not mounted) |

---

## 3. Routes Missing in .NET (The 110-Route Gap List)

The following 110 endpoints exist in the legacy Python runtime but have no active Minimal API route registration in `Backend.DotNet/`:

### Platform (2 endpoints missing)
1. `GET /api/health` (`PLT-001`, `Backend/app.py:186`)
2. `GET /api/version` (`PLT-002`, `Backend/app.py:180`)

### Identity (10 endpoints missing)
3. `GET /api/auth/callback/{provider}` (`IDN-001`, `Backend/auth_routes.py:343`)
4. `GET /api/auth/csrf` (`IDN-002`, `Backend/auth_routes.py:133`)
5. `POST /api/auth/login` (`IDN-003`, `Backend/auth_routes.py:164`)
6. `POST /api/auth/logout` (`IDN-004`, `Backend/auth_routes.py:437`)
7. `GET /api/auth/oauth/{provider}` (`IDN-005`, `Backend/auth_routes.py:284`)
8. `GET /api/auth/session` (`IDN-006`, `Backend/auth_routes.py:149`)
9. `POST /api/auth/switch-user` (`IDN-007`, `Backend/auth_routes.py:407`)
10. `POST /api/v1/finance/auth/act-as` (`IDN-008`, `Backend/finance_routes.py:386`)
11. `GET /api/v1/finance/auth/me` (`IDN-009`, `Backend/finance_routes.py:359`)
12. `GET /api/v1/finance/users` (`IDN-010`, `Backend/finance_routes.py:364`)

### Audit (1 endpoint missing)
13. `GET /api/v1/finance/audit-events` (`AUD-001`, `Backend/finance_routes.py:1376`)

### Receipts (5 endpoints missing)
14. `GET /api/v1/orders/{order_id}/receipt` (`REC-001`, `Backend/app.py:750`)
15. `GET /api/v1/orders/{order_id}/receipt/pdf` (`REC-002`, `Backend/app.py:760`)
16. `GET /api/v1/payments/{payment_id}/receipt` (`REC-003`, `Backend/app.py:573`)
17. `POST /api/v1/payments/{payment_id}/receipt/email` (`REC-004`, `Backend/app.py:651`)
18. `GET /api/v1/payments/{payment_id}/receipt/pdf` (`REC-005`, `Backend/app.py:606`)

### Payments (15 endpoints missing)
19. `POST /api/v1/admin/fulfillment-outbox/process` (`PAY-001`, `Backend/app.py:832`)
20. `GET /api/v1/admin/fulfillment-outbox` (`PAY-002`, `Backend/app.py:822`)
21. `POST /api/v1/admin/payments/reconcile-all` (`PAY-003`, `Backend/app.py:804`)
22. `POST /api/v1/admin/payments/{payment_id}/reconcile` (`PAY-004`, `Backend/app.py:794`)
23. `GET /api/v1/admin/webhook-events` (`PAY-005`, `Backend/app.py:812`)
24. `POST /api/v1/orders` (`PAY-006`, `Backend/app.py:341`)
25. `GET /api/v1/orders` (`PAY-007`, `Backend/app.py:321`)
26. `POST /api/v1/orders/{order_id}/payment-attempts` (`PAY-008`, `Backend/app.py:382`)
27. `GET /api/v1/orders/{order_id}` (`PAY-009`, `Backend/app.py:366`)
28. `POST /api/v1/payments/attempts/{attempt_id}/simulate` (`PAY-010`, `Backend/app.py:945`)
29. `POST /api/v1/payments/{payment_id}/expire` (`PAY-011`, `Backend/app.py:953`)
30. `POST /api/v1/payments/{payment_id}/fail` (`PAY-012`, `Backend/app.py:976`)
31. `GET /api/v1/payments/{payment_id}` (`PAY-013`, `Backend/app.py:488`)
32. `POST /api/v1/sandbox/create-and-expire-test` (`PAY-014`, `Backend/app.py:1000`)
33. `POST /api/v1/sandbox/simulate-webhook` (`PAY-015`, `Backend/app.py:837`)

### Finance (58 endpoints missing)
34. `GET /api/v1/finance/approval-matrix` (`FIN-001`)
35. `GET /api/v1/finance/business-confirmations` (`FIN-002`)
36. `POST /api/v1/finance/cash-advances` (`FIN-003`)
37. `GET /api/v1/finance/cash-advances` (`FIN-004`)
38. `POST /api/v1/finance/cash-advances/{advance_id}/cash-return` (`FIN-005`)
39. `POST /api/v1/finance/cash-advances/{advance_id}/close` (`FIN-006`)
40. `POST /api/v1/finance/cash-advances/{advance_id}/decide` (`FIN-007`)
41. `POST /api/v1/finance/cash-advances/{advance_id}/disbursements` (`FIN-008`)
42. `POST /api/v1/finance/cash-advances/{advance_id}/reimbursement-settle` (`FIN-009`)
43. `POST /api/v1/finance/cash-advances/{advance_id}/submit` (`FIN-010`)
44. `GET /api/v1/finance/cash-advances/{advance_id}` (`FIN-011`)
45. `GET /api/v1/finance/dashboard/summary` (`FIN-012`)
46. `POST /api/v1/finance/disbursements/{disbursement_id}/bank-approve` (`FIN-013`)
47. `POST /api/v1/finance/disbursements/{disbursement_id}/exception-state` (`FIN-014`)
48. `POST /api/v1/finance/disbursements/{disbursement_id}/execute` (`FIN-015`)
49. `POST /api/v1/finance/disbursements/{disbursement_id}/verify` (`FIN-016`)
50. `POST /api/v1/finance/disbursements/{disbursement_id}/verify-evidence` (`FIN-017`)
51. `GET /api/v1/finance/exception-register` (`FIN-018`)
52. `POST /api/v1/finance/exception-register/{register_id}/resolve` (`FIN-019`)
53. `POST /api/v1/finance/exception-requests` (`FIN-020`)
54. `GET /api/v1/finance/exception-requests` (`FIN-021`)
55. `POST /api/v1/finance/exception-requests/{exception_id}/decide` (`FIN-022`)
56. `POST /api/v1/finance/expense-reports` (`FIN-023`)
57. `GET /api/v1/finance/expense-reports` (`FIN-024`)
58. `POST /api/v1/finance/expense-reports/{report_id}/evaluate` (`FIN-025`)
59. `POST /api/v1/finance/expense-reports/{report_id}/lines` (`FIN-026`)
60. `POST /api/v1/finance/expense-reports/{report_id}/review` (`FIN-027`)
61. `POST /api/v1/finance/expense-reports/{report_id}/submit` (`FIN-028`)
62. `PATCH /api/v1/finance/expense-reports/{report_id}` (`FIN-029`)
63. `GET /api/v1/finance/expense-reports/{report_id}` (`FIN-030`)
64. `GET /api/v1/finance/kpi-definitions` (`FIN-031`)
65. `POST /api/v1/finance/payables` (`FIN-032`)
66. `GET /api/v1/finance/payables` (`FIN-033`)
67. `POST /api/v1/finance/payables/{payable_id}/payments` (`FIN-034`)
68. `POST /api/v1/finance/payment-records/{payment_id}/bank-approve` (`FIN-035`)
69. `POST /api/v1/finance/payment-records/{payment_id}/exception-state` (`FIN-036`)
70. `POST /api/v1/finance/payment-records/{payment_id}/execute` (`FIN-037`)
71. `POST /api/v1/finance/payment-records/{payment_id}/reconcile` (`FIN-038`)
72. `POST /api/v1/finance/payment-records/{payment_id}/verify` (`FIN-039`)
73. `GET /api/v1/finance/policies` (`FIN-040`)
74. `POST /api/v1/finance/policies/{policy_code}/versions` (`FIN-041`)
75. `POST /api/v1/finance/policies/versions/{version_id}/approve` (`FIN-042`)
76. `POST /api/v1/finance/policies/versions/{version_id}/retire` (`FIN-043`)
77. `POST /api/v1/finance/policies/versions/{version_id}/submit` (`FIN-044`)
78. `POST /api/v1/finance/purchase-requests` (`FIN-045`)
79. `GET /api/v1/finance/purchase-requests` (`FIN-046`)
80. `POST /api/v1/finance/purchase-requests/{pr_id}/decide` (`FIN-047`)
81. `POST /api/v1/finance/purchase-requests/{pr_id}/submit` (`FIN-048`)
82. `GET /api/v1/finance/purchase-requests/{pr_id}` (`FIN-049`)
83. `POST /api/v1/finance/tax-codes` (`FIN-050`)
84. `GET /api/v1/finance/tax-codes` (`FIN-051`)
85. `GET /api/v1/finance/tracker` (`FIN-052`)
86. `GET /api/v1/finance/tracker/export.csv` (`FIN-053`)
87. `POST /api/v1/finance/vendor-bank-changes/{change_id}/decide` (`FIN-054`)
88. `POST /api/v1/finance/vendors` (`FIN-055`)
89. `GET /api/v1/finance/vendors` (`FIN-056`)
90. `POST /api/v1/finance/vendors/{vendor_id}/bank-changes` (`FIN-057`)
91. `POST /api/v1/finance/vendors/{vendor_id}/verify` (`FIN-058`)

### Documents (3 endpoints missing)
92. `POST /api/v1/finance/documents` (`DOC-001`)
93. `GET /api/v1/finance/documents` (`DOC-002`)
94. `GET /api/v1/finance/documents/{doc_id}/download` (`DOC-003`)

### Integrations (6 endpoints missing)
95. `POST /api/v1/integrations/configure` (`INT-001`)
96. `POST /api/v1/integrations/email/test` (`INT-002`)
97. `POST /api/v1/integrations/google-forms/submit` (`INT-003`)
98. `GET /api/v1/integrations/google-forms/status` (`INT-004`)
99. `POST /api/v1/integrations/paymongo/test` (`INT-005`)
100. `GET /api/v1/integrations/status` (`INT-006`)

### Cost Allocation & Legacy Dashboard (10 endpoints missing)
101. `GET /api/configuration` (`CST-001`)
102. `PUT /api/configuration` (`CST-002`)
103. `POST /api/configuration/departments` (`CST-003`)
104. `POST /api/configuration/users` (`CST-004`)
105. `GET /api/cost-allocation` (`CST-005`)
106. `POST /api/cost-allocation/department` (`CST-006`)
107. `GET /api/dashboard` (`CST-007`)
108. `GET /api/department/{department_name}` (`CST-008`)
109. `GET /api/products` (`CST-009`)
110. `GET /api/variance` (`CST-010`)

---

## 4. Key Takeaways & Recommendations

1. **Why `dotnet/v0.1.0-preview.4` Was Premature as a Release**:
   - The .NET backend has strong domain models, builders, and security utilities, but 110 of 113 HTTP endpoints have not been mounted yet.
   - Calling preview work "releases" misled human auditors regarding the state of migration parity.
2. **Path Forward**:
   - Work module by module following the milestone plan: `v0.1.0` (Foundation), `v0.2.0` (Identity/Audit), `v0.3.0` (Payments/Receipts), `v0.4.0` (Finance), `v0.5.0` (Integrations/Alloc/Docs).
   - Implement handlers in `Backend.DotNet/src/Modules/*/Api/`, wire integration tests, record parity artifacts under `docs/migration/evidence/<id>/`, and only propose milestone tags when 100% of that module's rows are verified.


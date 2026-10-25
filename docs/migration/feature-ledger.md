# R.G.E Accounting Platform - Feature Migration Ledger

This ledger tracks **non-route behaviors, domain invariants, background services, and security subsystems** that must achieve parity between the Python legacy line and the .NET 10 Modular Monolith line.

### Status State Machine:
`not-started` $\rightarrow$ `implemented` $\rightarrow$ `tested` $\rightarrow$ `parity-verified` $\rightarrow$ `accepted-difference`

---

## 1. Domain & Financial Invariants

| ID | Feature / Invariant | Python Implementation | .NET 10 Implementation | Status | Parity Test / Evidence | Milestone | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **FEAT-MON-001** | Integer Centavos Arithmetic (`Money` VO) | `Backend/models.py` | `src/BuildingBlocks/.../Money.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Exact 64-bit integer centavos; zero float conversion. |
| **FEAT-TAX-001** | Philippine VAT (1200 bps) & Centavo Conservation | `Backend/finance_models.py` | `src/BuildingBlocks/.../VatCalculator.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | `VatableSales + VatAmount == Gross`; odd centavo half-up rounding verified. |
| **FEAT-RND-001** | Midpoint Rounding (`DivideHalfUp`) | `Backend/finance_models.py` | `src/BuildingBlocks/.../Rounding.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Mathematical half-up division verified against positive, negative, and odd denominators. |
| **FEAT-ADV-001** | Cash Advance Balance & Settlement Calculator | `Backend/finance_models.py` | `src/BuildingBlocks/.../AdvanceBalanceCalculator.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Full liquidation, overspend negative balance, and reimbursement payable verified. |
| **FEAT-STM-001** | 5 Financial State Machines Transition Tables | `Backend/finance_models.py` | `src/BuildingBlocks/.../StateTransitionTables.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | PaymentStatus, LiquidationReview, Settlement, Posting, Approval transitions verified. |

---

## 2. Authentication, Authorization & CPA Controls

| ID | Feature / Invariant | Python Implementation | .NET 10 Implementation | Status | Parity Test / Evidence | Milestone | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **FEAT-SOD-001** | Four-Eyes Separation of Duties (Requestor != Approver) | `Backend/finance_routes.py` | `src/Modules/Finance/.../ApprovalRouter.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Self-approval bypass strictly rejected across all financial tiers. |
| **FEAT-SOD-002** | Four-Eyes Disbursement Roles (Maker != Verifier != Approver) | `Backend/finance_routes.py` | `src/Modules/Finance/.../ApprovalRouter.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Distinct 4-actor verification on disbursement execution passed. |
| **FEAT-SOD-003** | System Admin Financial Approval Restriction | `Backend/finance_routes.py` | `src/Modules/Finance/.../ApprovalRouter.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | System Admin without explicit finance role is rejected from financial approvals. |
| **FEAT-APP-001** | Configurable Approval Matrix & Role Thresholds | `Backend/finance_routes.py` | `src/Modules/Finance/.../ApprovalMatrix.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Supervisor ($\le 50k$), Finance Controller ($\le 500k$), CFO thresholds verified. |
| **FEAT-APP-002** | Immutable Approval Decision History Trail | `Backend/finance_routes.py` | `src/Modules/Finance/.../ApprovalHistoryEntry.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Decision logging with timestamp, actor, threshold, and audit reason verified. |
| **FEAT-AUT-001** | User Authentication via Database Stored Procedure | `Backend/auth_routes.py` | `src/Modules/Identity/.../LoginHandler.cs` | **tested** | `tests/Modules/Identity.Application.Tests` | `v0.2.0` | Direct invocation of `fn_authenticate_user(email, password)`. |
| **FEAT-AUT-002** | Session Token Generation & SHA-256 Hashing | `Backend/auth_routes.py` | `src/Modules/Identity/.../SessionTokenService.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.2.0` | CSPRNG 32-byte tokens with SHA-256 hex hashing verified. |
| **FEAT-AUT-003** | CSRF Double-Submit Cookie & Token Verification | `Backend/auth_routes.py` | `src/BuildingBlocks.Api/.../CsrfMiddleware.cs` | **implemented** | `tests/Security.Tests` | `v0.2.0` | Constant-time token verification on mutating requests. |

---

## 3. Cryptographic Audit Chain & Official Receipts

| ID | Feature / Invariant | Python Implementation | .NET 10 Implementation | Status | Parity Test / Evidence | Milestone | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **FEAT-AUD-001** | Hash-Chained Audit Append (`fn_append_audit_event`) | `Backend/database.py` | `src/Modules/Audit/.../AuditRepository.cs` | **implemented** | `tools/AuditChain.Verifier` | `v0.2.0` | Immutable linear SHA-256 chain: `SHA256(prev_hash + payload)`. |
| **FEAT-AUD-002** | Audit Chain Verifier & Integrity Validator | `scripts/verify_audit_chain.py` | `tools/AuditChain.Verifier/Program.cs` | **implemented** | `tools/AuditChain.Verifier` | `v0.2.0` | Mathematical verification of historical hash chains. |
| **FEAT-BIR-001** | Official Receipt Number Generation | `Backend/models.py` | `src/Modules/Receipts/.../ReceiptNumberGenerator.cs` | **implemented** | `tests/Modules/Receipts.Domain.Tests` | `v0.3.0` | Monotonic sequential BIR-compliant receipt numbering. |
| **FEAT-BIR-002** | Tamper-Evident Receipt Digest (`sha256:v2`) | `Backend/models.py` | `src/Modules/Receipts/.../ReceiptDigestService.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.3.0` | Deterministic Canonical JSON digest verified: starts with `sha256:v2:`. |
| **FEAT-BIR-003** | Official Receipt PDF Generation | `Backend/app.py` | `src/Modules/Receipts/.../ReceiptPdfRenderer.cs` | **implemented** | `tests/Modules/Receipts.Application.Tests` | `v0.3.0` | QuestPDF implementation matching legacy ReportLab layout. |

---

## 4. Payment Gateway & Webhook Orchestration

| ID | Feature / Invariant | Python Implementation | .NET 10 Implementation | Status | Parity Test / Evidence | Milestone | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **FEAT-PAY-001** | PayMongo Checkout Session Creation | `Backend/app.py` | `src/Modules/Payments/.../PayMongoGateway.cs` | **implemented** | `tests/Modules/Payments.Integration.Tests` | `v0.3.0` | HTTP Basic Auth (`sk_...:`) and centavo minor amounts. |
| **FEAT-PAY-002** | Raw-Body HMAC-SHA256 Webhook Verification | `Backend/webhook_orchestration.py` | `src/Modules/Payments/.../PayMongoSignatureVerifier.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.3.0` | Verifies `Paymongo-Signature` (`t=...,te=...`) within 300s clock skew. |
| **FEAT-PAY-003** | Fast Webhook Ingestion (< 50ms ACK) | `Backend/webhook_orchestration.py` | `src/Modules/Payments/.../PayMongoWebhookEndpoint.cs` | **tested** | `tools/ParityRunner/Program.cs` | `v0.3.0` | Immediate ACK after signature validation; async processing via outbox. |
| **FEAT-PAY-004** | Transactional Fulfillment Outbox | `Backend/webhook_orchestration.py` | `src/Modules/Payments/.../FulfillmentOutboxService.cs` | **implemented** | `tests/Modules/Payments.Application.Tests` | `v0.3.0` | Outbox table pattern preventing duplicate webhook fulfillment. |

---

## 5. Security Guardrails & Operational Infrastructure

| ID | Feature / Invariant | Python Implementation | .NET 10 Implementation | Status | Parity Test / Evidence | Milestone | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **FEAT-SEC-001** | AES-256-GCM Envelope Field Encryption | `Backend/database.py` | `src/BuildingBlocks.Infrastructure/.../FieldEncryptionService.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | `v1:base64(nonce+tag+cipher)` payload structure verified. |
| **FEAT-SEC-002** | Magic Byte File Upload Validation | `Backend/finance_routes.py` | `src/BuildingBlocks.Infrastructure/.../MagicByteValidator.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | PDF, JPEG, PNG binary magic byte verification and traversal protection. |
| **FEAT-SEC-003** | CSV Formula Injection Neutralization | `Backend/finance_routes.py` | `src/BuildingBlocks.Infrastructure/.../CsvInjectionSafeWriter.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Prepends `'` to leading `=`, `+`, `-`, `@` characters. |
| **FEAT-SEC-004** | RFC 9457 Problem Details Exception Factory | `Backend/app.py` | `src/BuildingBlocks.Api/.../ProblemDetailsFactory.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Maps validation (400), SoD (403), policy (422) with correlation ID. |
| **FEAT-SEC-005** | Idempotency Key Header Validation | `Backend/app.py` | `src/BuildingBlocks.Api/.../IdempotencyFilter.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Enforces safe ASCII and $\le 128$ characters on mutating endpoints. |
| **FEAT-SEC-006** | System-Wide snake_case JSON Serialization | FastAPI default | `src/BuildingBlocks.Api/.../JsonDefaults.cs` | **parity-verified** | `tools/ParityRunner/Program.cs` | `v0.1.0` | Consistent `snake_case` naming policy across all responses. |

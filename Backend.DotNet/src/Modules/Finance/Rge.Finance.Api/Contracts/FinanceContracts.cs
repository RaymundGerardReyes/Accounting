using System.Text.Json.Serialization;

namespace Rge.Finance.Api.Contracts;

// Cash Advances
public sealed record CreateCashAdvanceApiRequest(
    [property: JsonPropertyName("employee_id")] string EmployeeId,
    [property: JsonPropertyName("department_id")] string DepartmentId,
    [property: JsonPropertyName("cost_center_id")] string CostCenterId,
    [property: JsonPropertyName("requested_amount_minor")] long RequestedAmountMinor,
    [property: JsonPropertyName("purpose_category")] string PurposeCategory,
    [property: JsonPropertyName("business_purpose")] string BusinessPurpose,
    [property: JsonPropertyName("requested_disbursement_date")] string RequestedDisbursementDate,
    [property: JsonPropertyName("currency")] string? Currency = "PHP",
    [property: JsonPropertyName("remarks")] string? Remarks = null,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey = null);

public sealed record DecideCashAdvanceApiRequest(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("approved_amount_minor")] long? ApprovedAmountMinor = null,
    [property: JsonPropertyName("rejection_reason")] string? RejectionReason = null,
    [property: JsonPropertyName("correction_notes")] string? CorrectionNotes = null);

public sealed record PrepareDisbursementApiRequest(
    [property: JsonPropertyName("target_payment_method")] string TargetPaymentMethod,
    [property: JsonPropertyName("payment_amount_minor")] long? PaymentAmountMinor = null,
    [property: JsonPropertyName("bank_account_name")] string? BankAccountName = null,
    [property: JsonPropertyName("bank_account_number")] string? BankAccountNumber = null,
    [property: JsonPropertyName("bank_name")] string? BankName = null,
    [property: JsonPropertyName("remarks")] string? Remarks = null,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey = null);

public sealed record CashReturnApiRequest(
    [property: JsonPropertyName("amount_minor")] long AmountMinor,
    [property: JsonPropertyName("return_method")] string ReturnMethod,
    [property: JsonPropertyName("return_reference")] string ReturnReference,
    [property: JsonPropertyName("remittance_doc_id")] string? RemittanceDocId = null);

public sealed record ReimbursementSettleApiRequest(
    [property: JsonPropertyName("amount_minor")] long AmountMinor,
    [property: JsonPropertyName("settlement_method")] string SettlementMethod,
    [property: JsonPropertyName("bank_reference")] string BankReference,
    [property: JsonPropertyName("remittance_doc_id")] string? RemittanceDocId = null);

public sealed record CloseCashAdvanceApiRequest(
    [property: JsonPropertyName("remarks")] string? Remarks = null);

// Disbursements
public sealed record ExecuteDisbursementApiRequest(
    [property: JsonPropertyName("execution_date")] string ExecutionDate,
    [property: JsonPropertyName("bank_reference")] string BankReference,
    [property: JsonPropertyName("remittance_doc_id")] string? RemittanceDocId = null,
    [property: JsonPropertyName("executed_amount_minor")] long? ExecutedAmountMinor = null);

public sealed record VerifyEvidenceDisbursementApiRequest(
    [property: JsonPropertyName("evidence_document_id")] string? EvidenceDocumentId = null,
    [property: JsonPropertyName("notes")] string? Notes = null);

public sealed record SetDisbursementExceptionStateApiRequest(
    [property: JsonPropertyName("target_state")] string TargetState,
    [property: JsonPropertyName("reason")] string Reason);

// Expense Reports
public sealed record CreateExpenseReportApiRequest(
    [property: JsonPropertyName("employee_id")] string EmployeeId,
    [property: JsonPropertyName("department_id")] string DepartmentId,
    [property: JsonPropertyName("cost_center_id")] string CostCenterId,
    [property: JsonPropertyName("reporting_period")] string ReportingPeriod,
    [property: JsonPropertyName("business_purpose_summary")] string BusinessPurposeSummary,
    [property: JsonPropertyName("out_of_base_activity")] bool OutOfBaseActivity = false,
    [property: JsonPropertyName("consolidated_receipt_doc_id")] string? ConsolidatedReceiptDocId = null,
    [property: JsonPropertyName("mfar_doc_id")] string? MfarDocId = null,
    [property: JsonPropertyName("employee_declaration_confirmed")] bool EmployeeDeclarationConfirmed = true,
    [property: JsonPropertyName("remarks")] string? Remarks = null,
    [property: JsonPropertyName("currency")] string? Currency = "PHP",
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey = null);

public sealed record PatchExpenseReportApiRequest(
    [property: JsonPropertyName("business_purpose_summary")] string? BusinessPurposeSummary = null,
    [property: JsonPropertyName("remarks")] string? Remarks = null,
    [property: JsonPropertyName("consolidated_receipt_doc_id")] string? ConsolidatedReceiptDocId = null,
    [property: JsonPropertyName("mfar_doc_id")] string? MfarDocId = null);

public sealed record AddExpenseReportLineApiItem(
    [property: JsonPropertyName("transaction_date")] string TransactionDate,
    [property: JsonPropertyName("expense_category")] string ExpenseCategory,
    [property: JsonPropertyName("business_purpose")] string BusinessPurpose,
    [property: JsonPropertyName("merchant_or_payee")] string MerchantOrPayee,
    [property: JsonPropertyName("claimed_amount_minor")] long ClaimedAmountMinor,
    [property: JsonPropertyName("school_or_destination")] string? SchoolOrDestination = null,
    [property: JsonPropertyName("receipt_or_invoice_no")] string? ReceiptOrInvoiceNo = null,
    [property: JsonPropertyName("out_of_base")] bool OutOfBase = false,
    [property: JsonPropertyName("receipt_document_id")] string? ReceiptDocumentId = null,
    [property: JsonPropertyName("mfar_document_id")] string? MfarDocumentId = null,
    [property: JsonPropertyName("tax_code")] string? TaxCode = null,
    [property: JsonPropertyName("vat_amount_minor")] long? VatAmountMinor = null);

public sealed record AddExpenseReportLinesApiRequest(
    [property: JsonPropertyName("lines")] IReadOnlyList<AddExpenseReportLineApiItem> Lines);

public sealed record ReviewExpenseReportApiRequest(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("rejection_reason")] string? RejectionReason = null);

// Payables & Payments
public sealed record RegisterPayableApiRequest(
    [property: JsonPropertyName("purchase_request_id")] string PurchaseRequestId,
    [property: JsonPropertyName("vendor_id")] string VendorId,
    [property: JsonPropertyName("invoice_number")] string InvoiceNumber,
    [property: JsonPropertyName("invoice_date")] string InvoiceDate,
    [property: JsonPropertyName("due_date")] string DueDate,
    [property: JsonPropertyName("original_payable_minor")] long OriginalPayableMinor,
    [property: JsonPropertyName("currency")] string? Currency = "PHP",
    [property: JsonPropertyName("credit_adjustment_minor")] long? CreditAdjustmentMinor = null,
    [property: JsonPropertyName("vat_code")] string? VatCode = null,
    [property: JsonPropertyName("vat_amount_minor")] long? VatAmountMinor = null,
    [property: JsonPropertyName("ewt_code")] string? EwtCode = null,
    [property: JsonPropertyName("ewt_amount_minor")] long? EwtAmountMinor = null,
    [property: JsonPropertyName("payment_terms")] string? PaymentTerms = null,
    [property: JsonPropertyName("accounting_reference")] string? AccountingReference = null);

public sealed record PreparePayablePaymentApiRequest(
    [property: JsonPropertyName("payment_amount_minor")] long PaymentAmountMinor,
    [property: JsonPropertyName("payment_method")] string PaymentMethod,
    [property: JsonPropertyName("currency")] string? Currency = "PHP",
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey = null);

public sealed record ExecutePaymentRecordApiRequest(
    [property: JsonPropertyName("execution_date")] string ExecutionDate,
    [property: JsonPropertyName("bank_reference")] string BankReference,
    [property: JsonPropertyName("remittance_doc_id")] string? RemittanceDocId = null,
    [property: JsonPropertyName("executed_amount_minor")] long? ExecutedAmountMinor = null);

public sealed record ReconcilePaymentRecordApiRequest(
    [property: JsonPropertyName("notes")] string? Notes = null);

public sealed record SetPaymentRecordExceptionStateApiRequest(
    [property: JsonPropertyName("target_state")] string TargetState,
    [property: JsonPropertyName("reason")] string Reason);

// Purchase Requests
public sealed record CreatePurchaseRequestApiRequest(
    [property: JsonPropertyName("requestor_id")] string RequestorId,
    [property: JsonPropertyName("vendor_id")] string VendorId,
    [property: JsonPropertyName("payment_category")] string PaymentCategory,
    [property: JsonPropertyName("is_restricted_category")] bool IsRestrictedCategory,
    [property: JsonPropertyName("business_purpose")] string BusinessPurpose,
    [property: JsonPropertyName("department_id")] string DepartmentId,
    [property: JsonPropertyName("cost_center_id")] string CostCenterId,
    [property: JsonPropertyName("requested_amount_minor")] long RequestedAmountMinor,
    [property: JsonPropertyName("requested_payment_date")] string RequestedPaymentDate,
    [property: JsonPropertyName("currency")] string? Currency = "PHP",
    [property: JsonPropertyName("budget_reference")] string? BudgetReference = null,
    [property: JsonPropertyName("due_date")] string? DueDate = null,
    [property: JsonPropertyName("supporting_doc_ids")] IReadOnlyList<string>? SupportingDocIds = null,
    [property: JsonPropertyName("invoice_doc_id")] string? InvoiceDocId = null,
    [property: JsonPropertyName("remarks")] string? Remarks = null,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey = null);

public sealed record DecidePurchaseRequestApiRequest(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("approved_amount_minor")] long? ApprovedAmountMinor = null,
    [property: JsonPropertyName("reason")] string? Reason = null);

// Policies
public sealed record AuthorPolicyVersionApiRequest(
    [property: JsonPropertyName("version_number")] long VersionNumber,
    [property: JsonPropertyName("effective_from")] string EffectiveFrom,
    [property: JsonPropertyName("aggregate_monthly_limit_minor")] long AggregateMonthlyLimitMinor,
    [property: JsonPropertyName("currency")] string? Currency = "PHP",
    [property: JsonPropertyName("max_reports_per_period")] int MaxReportsPerPeriod = 1,
    [property: JsonPropertyName("change_justification")] string ChangeJustification = "Policy update",
    [property: JsonPropertyName("effective_until")] string? EffectiveUntil = null,
    [property: JsonPropertyName("period_type")] string PeriodType = "calendar_month",
    [property: JsonPropertyName("submission_deadline_day")] int SubmissionDeadlineDay = 2,
    [property: JsonPropertyName("deadline_interpretation")] string DeadlineInterpretation = "strict_next_business_day",
    [property: JsonPropertyName("finance_sla_days")] int FinanceSlaDays = 5,
    [property: JsonPropertyName("sla_day_type")] string SlaDayType = "business_days",
    [property: JsonPropertyName("require_consolidated_receipt_pdf")] bool RequireConsolidatedReceiptPdf = true,
    [property: JsonPropertyName("require_mfar_for_out_of_base")] bool RequireMfarForOutOfBase = true,
    [property: JsonPropertyName("allow_partial_line_approval")] bool AllowPartialLineApproval = true,
    [property: JsonPropertyName("block_new_advance_when_overdue")] bool BlockNewAdvanceWhenOverdue = true,
    [property: JsonPropertyName("max_advance_amount_minor")] long? MaxAdvanceAmountMinor = null);

public sealed record ApprovePolicyVersionApiRequest(
    [property: JsonPropertyName("comments")] string? Comments = null);

public sealed record RetirePolicyVersionApiRequest(
    [property: JsonPropertyName("reason")] string? Reason = null);

// Exceptions
public sealed record SubmitExceptionRequestApiRequest(
    [property: JsonPropertyName("employee_id")] string EmployeeId,
    [property: JsonPropertyName("target_entity_type")] string TargetEntityType,
    [property: JsonPropertyName("reporting_period")] string ReportingPeriod,
    [property: JsonPropertyName("policy_version_id")] string PolicyVersionId,
    [property: JsonPropertyName("violated_rule_code")] string ViolatedRuleCode,
    [property: JsonPropertyName("exception_type")] string ExceptionType,
    [property: JsonPropertyName("requested_amount_minor")] long RequestedAmountMinor,
    [property: JsonPropertyName("currency")] string? Currency = "PHP",
    [property: JsonPropertyName("business_justification")] string BusinessJustification = "Operational exception request",
    [property: JsonPropertyName("target_entity_id")] string? TargetEntityId = null,
    [property: JsonPropertyName("supporting_document_ids")] IReadOnlyList<string>? SupportingDocumentIds = null);

public sealed record DecideExceptionRequestApiRequest(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")] string? Reason = null);

public sealed record ResolveExceptionRegisterItemApiRequest(
    [property: JsonPropertyName("resolution_status")] string ResolutionStatus,
    [property: JsonPropertyName("notes")] string? Notes = null);

// Vendors & Tax Codes
public sealed record RegisterVendorApiRequest(
    [property: JsonPropertyName("registered_name")] string RegisteredName,
    [property: JsonPropertyName("payee_type")] string PayeeType,
    [property: JsonPropertyName("payment_method")] string PaymentMethod,
    [property: JsonPropertyName("tin")] string? Tin = null,
    [property: JsonPropertyName("address")] string? Address = null,
    [property: JsonPropertyName("contact_person")] string? ContactPerson = null,
    [property: JsonPropertyName("contact_email")] string? ContactEmail = null,
    [property: JsonPropertyName("bank_name")] string? BankName = null,
    [property: JsonPropertyName("raw_account_number")] string? RawAccountNumber = null,
    [property: JsonPropertyName("bank_account_ref")] string? BankAccountRef = null,
    [property: JsonPropertyName("kyc_document_id")] string? KycDocumentId = null);

public sealed record RequestBankChangeApiRequest(
    [property: JsonPropertyName("new_bank_name")] string NewBankName,
    [property: JsonPropertyName("raw_new_account_number")] string RawNewAccountNumber,
    [property: JsonPropertyName("new_bank_account_ref")] string NewBankAccountRef,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("supporting_doc_id")] string? SupportingDocId = null);

public sealed record DecideBankChangeApiRequest(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")] string? Reason = null);

public sealed record RegisterTaxCodeApiRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("tax_type")] string TaxType,
    [property: JsonPropertyName("rate_bps")] long RateBps,
    [property: JsonPropertyName("requires_bir2307")] bool RequiresBir2307 = false,
    [property: JsonPropertyName("cpa_approved")] bool CpaApproved = false);

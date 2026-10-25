using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Exceptions;
using Rge.Finance.Domain.Ledger;
using Rge.Finance.Domain.Liquidation;
using Rge.Finance.Domain.Policy;
using Rge.Finance.Domain.Procurement;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Application.Ports;

public interface ICashAdvanceRepository
{
    Task<long> NextAdvanceSequenceAsync(CancellationToken cancellationToken = default);
    Task<CashAdvance?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<CashAdvance?> GetByNumberAsync(string advanceNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CashAdvance>> ListAsync(string? departmentId, string? status, int limit, CancellationToken cancellationToken = default);
    Task SaveAsync(CashAdvance advance, CancellationToken cancellationToken = default);
}

public interface IDisbursementRepository
{
    Task<long> NextDisbursementSequenceAsync(CancellationToken cancellationToken = default);
    Task<Disbursement?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Disbursement>> GetByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default);
    Task SaveAsync(Disbursement disbursement, CancellationToken cancellationToken = default);
}

public interface IExpenseReportRepository
{
    Task<long> NextReportSequenceAsync(CancellationToken cancellationToken = default);
    Task<ExpenseReport?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpenseReport>> ListAsync(string? employeeId, string? status, int limit, CancellationToken cancellationToken = default);
    Task SaveAsync(ExpenseReport report, CancellationToken cancellationToken = default);
    Task SaveLineAsync(ExpenseLine line, CancellationToken cancellationToken = default);
}

public interface IPolicyRepository
{
    Task<FinancialPolicy?> GetByCodeAsync(string policyCode, CancellationToken cancellationToken = default);
    Task<PolicyVersion?> GetActiveVersionAsync(string policyCode, CancellationToken cancellationToken = default);
    Task<PolicyVersion?> GetVersionByIdAsync(string versionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinancialPolicy>> ListPoliciesAsync(CancellationToken cancellationToken = default);
    Task SavePolicyAsync(FinancialPolicy policy, CancellationToken cancellationToken = default);
    Task SaveVersionAsync(PolicyVersion version, CancellationToken cancellationToken = default);
    Task<PeriodCounter?> GetPeriodCounterAsync(string employeeId, string periodKey, string ruleCode, CancellationToken cancellationToken = default);
    Task SavePeriodCounterAsync(PeriodCounter counter, CancellationToken cancellationToken = default);
}

public interface IExceptionRequestRepository
{
    Task<long> NextExceptionSequenceAsync(CancellationToken cancellationToken = default);
    Task<ExceptionRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExceptionRequest>> ListAsync(string? employeeId, string? status, int limit, CancellationToken cancellationToken = default);
    Task SaveAsync(ExceptionRequest request, CancellationToken cancellationToken = default);
}

public interface IPurchaseRequestRepository
{
    Task<long> NextPrSequenceAsync(CancellationToken cancellationToken = default);
    Task<PurchaseRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PurchaseRequest>> ListAsync(string? departmentId, string? status, int limit, CancellationToken cancellationToken = default);
    Task SaveAsync(PurchaseRequest pr, CancellationToken cancellationToken = default);
}

public interface IAccountPayableRepository
{
    Task<long> NextPayableSequenceAsync(CancellationToken cancellationToken = default);
    Task<AccountPayable?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<AccountPayable?> GetByNormalizedInvoiceAsync(string vendorId, string normalizedInvoiceNo, string currency, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountPayable>> ListAsync(string? vendorId, string? status, int limit, CancellationToken cancellationToken = default);
    Task SaveAsync(AccountPayable payable, CancellationToken cancellationToken = default);
}

public interface IPaymentRecordRepository
{
    Task<long> NextPaymentRecordSequenceAsync(CancellationToken cancellationToken = default);
    Task<PaymentRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentRecord>> GetByPayableIdAsync(string payableId, CancellationToken cancellationToken = default);
    Task SaveAsync(PaymentRecord record, CancellationToken cancellationToken = default);
}

public interface IVendorRepository
{
    Task<long> NextVendorSequenceAsync(CancellationToken cancellationToken = default);
    Task<Vendor?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Vendor?> GetByCodeAsync(string vendorCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vendor>> ListAsync(string? status, int limit, CancellationToken cancellationToken = default);
    Task SaveAsync(Vendor vendor, CancellationToken cancellationToken = default);
    Task<VendorBankChange?> GetBankChangeByIdAsync(string id, CancellationToken cancellationToken = default);
    Task SaveBankChangeAsync(VendorBankChange change, CancellationToken cancellationToken = default);
}

public interface ITaxCodeRepository
{
    Task<TaxCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaxCode>> ListAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TaxCode taxCode, CancellationToken cancellationToken = default);
}

public interface IExceptionRegisterRepository
{
    Task<long> NextRegisterSequenceAsync(CancellationToken cancellationToken = default);
    Task<ExceptionRegisterItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExceptionRegisterItem>> ListAsync(string? status, int limit, CancellationToken cancellationToken = default);
    Task SaveAsync(ExceptionRegisterItem item, CancellationToken cancellationToken = default);
}

public interface IAdvanceLedgerStore
{
    Task SaveEntryAsync(AdvanceLedgerEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdvanceLedgerEntry>> GetEntriesByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default);
    Task SaveSnapshotAsync(ClosingSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<ClosingSnapshot?> GetSnapshotByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default);
}

public interface ISettlementApplicationStore
{
    Task SaveApplicationAsync(SettlementApplication app, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SettlementApplication>> GetByReportIdAsync(string reportId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SettlementApplication>> GetByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default);
}

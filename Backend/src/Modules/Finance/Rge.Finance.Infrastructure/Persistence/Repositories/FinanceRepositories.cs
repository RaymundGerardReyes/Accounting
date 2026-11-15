using System.Collections.Concurrent;
using System.Data.Common;
using Dapper;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.Finance.Application.Ports;
using Rge.Finance.Contracts;
using Rge.Finance.Domain.CashAdvances;
using Rge.Finance.Domain.Disbursements;
using Rge.Finance.Domain.Exceptions;
using Rge.Finance.Domain.Ledger;
using Rge.Finance.Domain.Liquidation;
using Rge.Finance.Domain.Policy;
using Rge.Finance.Domain.Procurement;
using Rge.Finance.Domain.Vendors;

namespace Rge.Finance.Infrastructure.Persistence.Repositories;

public sealed class CashAdvanceRepository : ICashAdvanceRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, CashAdvance> MemoryById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CashAdvance> MemoryByNo = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConnectionScope? _scope;

    public CashAdvanceRepository(ConnectionScope? scope = null) => _scope = scope;

    public Task<long> NextAdvanceSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<CashAdvance?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<CashAdvance?>(null);
        MemoryById.TryGetValue(id.Trim(), out CashAdvance? advance);
        return Task.FromResult(advance);
    }

    public Task<CashAdvance?> GetByNumberAsync(string advanceNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(advanceNumber)) return Task.FromResult<CashAdvance?>(null);
        MemoryByNo.TryGetValue(advanceNumber.Trim(), out CashAdvance? advance);
        return Task.FromResult(advance);
    }

    public Task<IReadOnlyList<CashAdvance>> ListAsync(string? departmentId, string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = MemoryById.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(departmentId))
            query = query.Where(a => string.Equals(a.DepartmentId, departmentId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => string.Equals(a.ApprovalStatus.ToString(), status, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a.PaymentStatus.ToString(), status, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(a.SettlementStatus.ToString(), status, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<CashAdvance>>(query.OrderByDescending(a => a.CreatedAtUtc).Take(limit).ToList());
    }

    public Task SaveAsync(CashAdvance advance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(advance);
        MemoryById[advance.Id] = advance;
        MemoryByNo[advance.AdvanceNumber.Value] = advance;
        return Task.CompletedTask;
    }
}

public sealed class DisbursementRepository : IDisbursementRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, Disbursement> MemoryById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConnectionScope? _scope;

    public DisbursementRepository(ConnectionScope? scope = null) => _scope = scope;

    public Task<long> NextDisbursementSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<Disbursement?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Disbursement?>(null);
        MemoryById.TryGetValue(id.Trim(), out Disbursement? d);
        return Task.FromResult(d);
    }

    public Task<IReadOnlyList<Disbursement>> GetByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(advanceId)) return Task.FromResult<IReadOnlyList<Disbursement>>([]);
        var list = MemoryById.Values.Where(d => string.Equals(d.AdvanceId, advanceId, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<Disbursement>>(list);
    }

    public Task SaveAsync(Disbursement disbursement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(disbursement);
        MemoryById[disbursement.Id] = disbursement;
        return Task.CompletedTask;
    }
}

public sealed class ExpenseReportRepository : IExpenseReportRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, ExpenseReport> MemoryById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConnectionScope? _scope;

    public ExpenseReportRepository(ConnectionScope? scope = null) => _scope = scope;

    public Task<long> NextReportSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<ExpenseReport?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<ExpenseReport?>(null);
        MemoryById.TryGetValue(id.Trim(), out ExpenseReport? r);
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<ExpenseReport>> ListAsync(string? employeeId, string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = MemoryById.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(employeeId))
            query = query.Where(r => string.Equals(r.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => string.Equals(r.ReviewStatus.ToString(), status, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<ExpenseReport>>(query.OrderByDescending(r => r.CreatedAtUtc).Take(limit).ToList());
    }

    public Task SaveAsync(ExpenseReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        MemoryById[report.Id] = report;
        return Task.CompletedTask;
    }

    public Task SaveLineAsync(ExpenseLine line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (MemoryById.TryGetValue(line.ExpenseReportId, out ExpenseReport? report))
        {
            if (!report.Lines.Any(l => string.Equals(l.Id, line.Id, StringComparison.OrdinalIgnoreCase)))
            {
                report.AddLine(line);
            }
        }
        return Task.CompletedTask;
    }
}

public sealed class PolicyRepository : IPolicyRepository
{
    private static readonly ConcurrentDictionary<string, FinancialPolicy> Policies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, PolicyVersion> Versions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, PeriodCounter> Counters = new(StringComparer.OrdinalIgnoreCase);

    public Task<FinancialPolicy?> GetByCodeAsync(string policyCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(policyCode)) return Task.FromResult<FinancialPolicy?>(null);
        FinancialPolicy? policy = Policies.Values.FirstOrDefault(p => string.Equals(p.PolicyCode, policyCode, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(policy);
    }

    public Task<PolicyVersion?> GetActiveVersionAsync(string policyCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(policyCode)) return Task.FromResult<PolicyVersion?>(null);
        FinancialPolicy? policy = Policies.Values.FirstOrDefault(p => string.Equals(p.PolicyCode, policyCode, StringComparison.OrdinalIgnoreCase));
        if (policy?.CurrentVersionId is null) return Task.FromResult<PolicyVersion?>(null);
        Versions.TryGetValue(policy.CurrentVersionId, out PolicyVersion? version);
        return Task.FromResult(version);
    }

    public Task<PolicyVersion?> GetVersionByIdAsync(string versionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId)) return Task.FromResult<PolicyVersion?>(null);
        Versions.TryGetValue(versionId.Trim(), out PolicyVersion? v);
        return Task.FromResult(v);
    }

    public Task<IReadOnlyList<FinancialPolicy>> ListPoliciesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<FinancialPolicy>>(Policies.Values.ToList());
    }

    public Task SavePolicyAsync(FinancialPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Policies[policy.Id] = policy;
        return Task.CompletedTask;
    }

    public Task SaveVersionAsync(PolicyVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        Versions[version.Id] = version;
        return Task.CompletedTask;
    }

    public Task<PeriodCounter?> GetPeriodCounterAsync(string employeeId, string periodKey, string ruleCode, CancellationToken cancellationToken = default)
    {
        string key = $"{employeeId}:{periodKey}:{ruleCode}";
        Counters.TryGetValue(key, out PeriodCounter? counter);
        return Task.FromResult(counter);
    }

    public Task SavePeriodCounterAsync(PeriodCounter counter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(counter);
        string key = $"{counter.EmployeeId}:{counter.PeriodKey}:{counter.RuleCode}";
        Counters[key] = counter;
        return Task.CompletedTask;
    }
}

public sealed class ExceptionRequestRepository : IExceptionRequestRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, ExceptionRequest> MemoryById = new(StringComparer.OrdinalIgnoreCase);

    public Task<long> NextExceptionSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<ExceptionRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<ExceptionRequest?>(null);
        MemoryById.TryGetValue(id.Trim(), out ExceptionRequest? e);
        return Task.FromResult(e);
    }

    public Task<IReadOnlyList<ExceptionRequest>> ListAsync(string? employeeId, string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = MemoryById.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(employeeId))
            query = query.Where(e => string.Equals(e.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(e => string.Equals(e.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<ExceptionRequest>>(query.OrderByDescending(e => e.CreatedAtUtc).Take(limit).ToList());
    }

    public Task SaveAsync(ExceptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MemoryById[request.Id] = request;
        return Task.CompletedTask;
    }
}

public sealed class PurchaseRequestRepository : IPurchaseRequestRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, PurchaseRequest> MemoryById = new(StringComparer.OrdinalIgnoreCase);

    public Task<long> NextPrSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<PurchaseRequest?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<PurchaseRequest?>(null);
        MemoryById.TryGetValue(id.Trim(), out PurchaseRequest? pr);
        return Task.FromResult(pr);
    }

    public Task<IReadOnlyList<PurchaseRequest>> ListAsync(string? departmentId, string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = MemoryById.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(departmentId))
            query = query.Where(p => string.Equals(p.DepartmentId, departmentId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(p => string.Equals(p.ApprovalStatus.ToString(), status, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(p.WorkflowStatus, status, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<PurchaseRequest>>(query.OrderByDescending(p => p.CreatedAtUtc).Take(limit).ToList());
    }

    public Task SaveAsync(PurchaseRequest pr, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pr);
        MemoryById[pr.Id] = pr;
        return Task.CompletedTask;
    }
}

public sealed class AccountPayableRepository : IAccountPayableRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, AccountPayable> MemoryById = new(StringComparer.OrdinalIgnoreCase);

    public Task<long> NextPayableSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<AccountPayable?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<AccountPayable?>(null);
        MemoryById.TryGetValue(id.Trim(), out AccountPayable? ap);
        return Task.FromResult(ap);
    }

    public Task<AccountPayable?> GetByNormalizedInvoiceAsync(string vendorId, string normalizedInvoiceNo, string currency, CancellationToken cancellationToken = default)
    {
        AccountPayable? match = MemoryById.Values.FirstOrDefault(ap =>
            string.Equals(ap.VendorId, vendorId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ap.NormalizedInvoiceNo.NormalizedValue, normalizedInvoiceNo, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ap.OriginalPayable.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<AccountPayable?>(match);
    }

    public Task<IReadOnlyList<AccountPayable>> ListAsync(string? vendorId, string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = MemoryById.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(vendorId))
            query = query.Where(ap => string.Equals(ap.VendorId, vendorId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(ap => string.Equals(ap.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<AccountPayable>>(query.OrderByDescending(ap => ap.CreatedAtUtc).Take(limit).ToList());
    }

    public Task SaveAsync(AccountPayable payable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payable);
        MemoryById[payable.Id] = payable;
        return Task.CompletedTask;
    }
}

public sealed class PaymentRecordRepository : IPaymentRecordRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, PaymentRecord> MemoryById = new(StringComparer.OrdinalIgnoreCase);

    public Task<long> NextPaymentRecordSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<PaymentRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<PaymentRecord?>(null);
        MemoryById.TryGetValue(id.Trim(), out PaymentRecord? pay);
        return Task.FromResult(pay);
    }

    public Task<IReadOnlyList<PaymentRecord>> GetByPayableIdAsync(string payableId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payableId)) return Task.FromResult<IReadOnlyList<PaymentRecord>>([]);
        var list = MemoryById.Values.Where(p => string.Equals(p.PayableId, payableId, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<PaymentRecord>>(list);
    }

    public Task SaveAsync(PaymentRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        MemoryById[record.Id] = record;
        return Task.CompletedTask;
    }
}

public sealed class VendorRepository : IVendorRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, Vendor> MemoryById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Vendor> MemoryByCode = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, VendorBankChange> ChangesById = new(StringComparer.OrdinalIgnoreCase);

    public Task<long> NextVendorSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<Vendor?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<Vendor?>(null);
        MemoryById.TryGetValue(id.Trim(), out Vendor? v);
        return Task.FromResult(v);
    }

    public Task<Vendor?> GetByCodeAsync(string vendorCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vendorCode)) return Task.FromResult<Vendor?>(null);
        MemoryByCode.TryGetValue(vendorCode.Trim(), out Vendor? v);
        return Task.FromResult(v);
    }

    public Task<IReadOnlyList<Vendor>> ListAsync(string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = MemoryById.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(v => string.Equals(v.VerificationStatus.ToString(), status, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<Vendor>>(query.OrderByDescending(v => v.CreatedAtUtc).Take(limit).ToList());
    }

    public Task SaveAsync(Vendor vendor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        MemoryById[vendor.Id] = vendor;
        MemoryByCode[vendor.VendorCode] = vendor;
        return Task.CompletedTask;
    }

    public Task<VendorBankChange?> GetBankChangeByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<VendorBankChange?>(null);
        ChangesById.TryGetValue(id.Trim(), out VendorBankChange? change);
        return Task.FromResult(change);
    }

    public Task SaveBankChangeAsync(VendorBankChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ChangesById[change.Id] = change;
        return Task.CompletedTask;
    }
}

public sealed class TaxCodeRepository : ITaxCodeRepository
{
    private static readonly ConcurrentDictionary<string, TaxCode> MemoryByCode = new(StringComparer.OrdinalIgnoreCase);

    static TaxCodeRepository()
    {
        // Default Philippine standard tax codes
        var now = DateTimeOffset.UtcNow;
        MemoryByCode["VAT12"] = new TaxCode("VAT12", "Standard Value-Added Tax 12%", "VAT", 1200, false, true, "system", now);
        MemoryByCode["WVAT5"] = new TaxCode("WVAT5", "Withholding VAT 5% (Government)", "WITHHOLDING_VAT", 500, true, true, "system", now);
        MemoryByCode["EWT1"] = new TaxCode("EWT1", "Expanded Withholding Tax 1% (Goods)", "EWT", 100, true, true, "system", now);
        MemoryByCode["EWT2"] = new TaxCode("EWT2", "Expanded Withholding Tax 2% (Services)", "EWT", 200, true, true, "system", now);
        MemoryByCode["EXEMPT"] = new TaxCode("EXEMPT", "Tax-Exempt", "EXEMPT", 0, false, true, "system", now);
    }

    public Task<TaxCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult<TaxCode?>(null);
        MemoryByCode.TryGetValue(code.Trim(), out TaxCode? tax);
        return Task.FromResult(tax);
    }

    public Task<IReadOnlyList<TaxCode>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TaxCode>>(MemoryByCode.Values.ToList());
    }

    public Task SaveAsync(TaxCode taxCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taxCode);
        MemoryByCode[taxCode.Code] = taxCode;
        return Task.CompletedTask;
    }
}

public sealed class ExceptionRegisterRepository : IExceptionRegisterRepository
{
    private static long _sequence;
    private static readonly ConcurrentDictionary<string, ExceptionRegisterItem> MemoryById = new(StringComparer.OrdinalIgnoreCase);

    public Task<long> NextRegisterSequenceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _sequence));
    }

    public Task<ExceptionRegisterItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult<ExceptionRegisterItem?>(null);
        MemoryById.TryGetValue(id.Trim(), out ExceptionRegisterItem? item);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<ExceptionRegisterItem>> ListAsync(string? status, int limit, CancellationToken cancellationToken = default)
    {
        var query = MemoryById.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => string.Equals(i.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<ExceptionRegisterItem>>(query.OrderByDescending(i => i.DetectedAtUtc).Take(limit).ToList());
    }

    public Task SaveAsync(ExceptionRegisterItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        MemoryById[item.Id] = item;
        return Task.CompletedTask;
    }
}

public sealed class AdvanceLedgerStore : IAdvanceLedgerStore
{
    private static readonly ConcurrentDictionary<string, List<AdvanceLedgerEntry>> EntriesByAdvance = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ClosingSnapshot> SnapshotsByAdvance = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveEntryAsync(AdvanceLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var list = EntriesByAdvance.GetOrAdd(entry.AdvanceId, _ => []);
        lock (list)
        {
            list.Add(entry);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AdvanceLedgerEntry>> GetEntriesByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(advanceId)) return Task.FromResult<IReadOnlyList<AdvanceLedgerEntry>>([]);
        if (EntriesByAdvance.TryGetValue(advanceId.Trim(), out List<AdvanceLedgerEntry>? list))
        {
            lock (list)
            {
                return Task.FromResult<IReadOnlyList<AdvanceLedgerEntry>>(list.ToList());
            }
        }
        return Task.FromResult<IReadOnlyList<AdvanceLedgerEntry>>([]);
    }

    public Task SaveSnapshotAsync(ClosingSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SnapshotsByAdvance[snapshot.AdvanceId] = snapshot;
        return Task.CompletedTask;
    }

    public Task<ClosingSnapshot?> GetSnapshotByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(advanceId)) return Task.FromResult<ClosingSnapshot?>(null);
        SnapshotsByAdvance.TryGetValue(advanceId.Trim(), out ClosingSnapshot? snapshot);
        return Task.FromResult(snapshot);
    }
}

public sealed class SettlementApplicationStore : ISettlementApplicationStore
{
    private static readonly ConcurrentDictionary<string, List<SettlementApplication>> ByReport = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, List<SettlementApplication>> ByAdvance = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveApplicationAsync(SettlementApplication app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!string.IsNullOrWhiteSpace(app.ExpenseReportId))
        {
            var repList = ByReport.GetOrAdd(app.ExpenseReportId, _ => []);
            lock (repList) { repList.Add(app); }
        }
        if (!string.IsNullOrWhiteSpace(app.AdvanceId))
        {
            var advList = ByAdvance.GetOrAdd(app.AdvanceId, _ => []);
            lock (advList) { advList.Add(app); }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SettlementApplication>> GetByReportIdAsync(string reportId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reportId)) return Task.FromResult<IReadOnlyList<SettlementApplication>>([]);
        if (ByReport.TryGetValue(reportId.Trim(), out List<SettlementApplication>? list))
        {
            lock (list) { return Task.FromResult<IReadOnlyList<SettlementApplication>>(list.ToList()); }
        }
        return Task.FromResult<IReadOnlyList<SettlementApplication>>([]);
    }

    public Task<IReadOnlyList<SettlementApplication>> GetByAdvanceIdAsync(string advanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(advanceId)) return Task.FromResult<IReadOnlyList<SettlementApplication>>([]);
        if (ByAdvance.TryGetValue(advanceId.Trim(), out List<SettlementApplication>? list))
        {
            lock (list) { return Task.FromResult<IReadOnlyList<SettlementApplication>>(list.ToList()); }
        }
        return Task.FromResult<IReadOnlyList<SettlementApplication>>([]);
    }
}

public sealed class AdvanceBalanceQuery : IAdvanceBalanceQuery
{
    private readonly ICashAdvanceRepository _advanceRepo;
    private readonly IAdvanceLedgerStore _ledgerStore;

    public AdvanceBalanceQuery(ICashAdvanceRepository advanceRepo, IAdvanceLedgerStore ledgerStore)
    {
        ArgumentNullException.ThrowIfNull(advanceRepo);
        ArgumentNullException.ThrowIfNull(ledgerStore);
        _advanceRepo = advanceRepo;
        _ledgerStore = ledgerStore;
    }

#pragma warning disable CA1034, CA1812
    public sealed class GeneralLedgerRepository(ConnectionScope connectionScope) : IGeneralLedgerRepository
    {
        private sealed record JournalHeader(
            Guid JournalId,
            string JournalNumber,
            string SourceModule,
            string SourceDocumentId,
            string SourceDocumentNumber,
            string AccountingPeriod,
            DateOnly AccountingDate,
            string PostingStatus,
            string PolicyVersionId,
            string TaxRuleVersion,
            long TotalDebitCentavos,
            long TotalCreditCentavos,
            string PreparedByActorId,
            string PreparedByName,
            string? VerifiedByActorId,
            string? VerifiedByName,
            string? PostedByActorId,
            string? PostedByName,
            string AuditHash,
            string Memo);

        public async Task<IReadOnlyList<GlChartOfAccountRecord>> GetActiveChartOfAccountsAsync(CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT account_code AS AccountCode, account_name AS AccountName,
                       account_type AS AccountType, normal_balance AS NormalBalance,
                       default_tax_profile AS DefaultTaxProfile,
                       opening_debit_centavos AS OpeningDebitCentavos,
                       opening_credit_centavos AS OpeningCreditCentavos,
                       is_active AS IsActive, description AS Description
                FROM gl_chart_of_accounts
                WHERE is_active = TRUE
                ORDER BY account_code;
                """;
            var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var command = new CommandDefinition(sql, transaction: connectionScope.CurrentTransaction,
                commandTimeout: SqlTimeouts.StandardCommandSeconds, cancellationToken: cancellationToken);
            return (await connection.QueryAsync<GlChartOfAccountRecord>(command).ConfigureAwait(false)).AsList();
        }

        public async Task<string> NextJournalNumberAsync(int fiscalYear, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT fn_next_sequence_number('JE', @FiscalYear);";
            var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var command = new CommandDefinition(sql, new { FiscalYear = fiscalYear },
                transaction: connectionScope.CurrentTransaction,
                commandTimeout: SqlTimeouts.FastReadSeconds, cancellationToken: cancellationToken);
            string? next = await connection.ExecuteScalarAsync<string>(command).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(next) ? $"JE-{fiscalYear}-{Guid.NewGuid():N}"[..15].ToUpperInvariant() : next;
        }

        public async Task InsertJournalBatchAsync(GlJournalBatchRecord batch, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(batch);
            const string headerSql = """
                INSERT INTO gl_journal_batches (
                    journal_id, journal_number, source_module, source_document_id,
                    source_document_number, accounting_period, accounting_date,
                    posting_status, policy_version_id, tax_rule_version,
                    total_debit_centavos, total_credit_centavos, prepared_by_actor_id,
                    prepared_by_name, verified_by_actor_id, verified_by_name,
                    posted_by_actor_id, posted_by_name, audit_hash, memo,
                    created_at_utc, updated_at_utc)
                VALUES (
                    @JournalId, @JournalNumber, @SourceModule, @SourceDocumentId,
                    @SourceDocumentNumber, @AccountingPeriod, @AccountingDate,
                    @PostingStatus, @PolicyVersionId, @TaxRuleVersion,
                    @TotalDebitCentavos, @TotalCreditCentavos, @PreparedByActorId,
                    @PreparedByName, @VerifiedByActorId, @VerifiedByName,
                    @PostedByActorId, @PostedByName, @AuditHash, @Memo, NOW(), NOW());
                """;
            const string lineSql = """
                INSERT INTO gl_journal_lines (
                    line_id, journal_id, line_number, account_code, debit_centavos,
                    credit_centavos, department_id, cost_center_id, purpose_code,
                    tax_profile, description)
                VALUES (
                    @LineId, @JournalId, @LineNumber, @AccountCode, @DebitCentavos,
                    @CreditCentavos, @DepartmentId, @CostCenterId, @PurposeCode,
                    @TaxProfile, @Description);
                """;
            var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var transaction = connectionScope.CurrentTransaction;
            await connection.ExecuteAsync(new CommandDefinition(headerSql, batch, transaction,
                SqlTimeouts.StandardCommandSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false);
            foreach (GlJournalLineRecord line in batch.Lines)
            {
                await connection.ExecuteAsync(new CommandDefinition(lineSql, line, transaction,
                    SqlTimeouts.StandardCommandSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT fn_gl_verify_journal_balance(@JournalId);",
                new { batch.JournalId }, transaction, SqlTimeouts.StandardCommandSeconds,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public async Task<GlJournalBatchRecord?> GetJournalByIdAsync(Guid journalId, CancellationToken cancellationToken = default)
        {
            const string headerSql = """
                SELECT journal_id AS JournalId, journal_number AS JournalNumber,
                       source_module AS SourceModule, source_document_id AS SourceDocumentId,
                       source_document_number AS SourceDocumentNumber,
                       accounting_period AS AccountingPeriod, accounting_date AS AccountingDate,
                       posting_status AS PostingStatus, policy_version_id AS PolicyVersionId,
                       tax_rule_version AS TaxRuleVersion, total_debit_centavos AS TotalDebitCentavos,
                       total_credit_centavos AS TotalCreditCentavos,
                       prepared_by_actor_id AS PreparedByActorId, prepared_by_name AS PreparedByName,
                       verified_by_actor_id AS VerifiedByActorId, verified_by_name AS VerifiedByName,
                       posted_by_actor_id AS PostedByActorId, posted_by_name AS PostedByName,
                       audit_hash AS AuditHash, memo AS Memo
                FROM gl_journal_batches WHERE journal_id = @JournalId;
                """;
            var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            JournalHeader? header = await connection.QuerySingleOrDefaultAsync<JournalHeader>(
                new CommandDefinition(headerSql, new { JournalId = journalId },
                    connectionScope.CurrentTransaction, SqlTimeouts.FastReadSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return header is null ? null : await HydrateAsync(header, connection, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<GlJournalBatchRecord>> ListJournalBatchesByPeriodAsync(
            string accountingPeriod, string? postingStatus = null, string? sourceModule = null,
            int limit = 100, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT journal_id AS JournalId, journal_number AS JournalNumber,
                       source_module AS SourceModule, source_document_id AS SourceDocumentId,
                       source_document_number AS SourceDocumentNumber,
                       accounting_period AS AccountingPeriod, accounting_date AS AccountingDate,
                       posting_status AS PostingStatus, policy_version_id AS PolicyVersionId,
                       tax_rule_version AS TaxRuleVersion, total_debit_centavos AS TotalDebitCentavos,
                       total_credit_centavos AS TotalCreditCentavos,
                       prepared_by_actor_id AS PreparedByActorId, prepared_by_name AS PreparedByName,
                       verified_by_actor_id AS VerifiedByActorId, verified_by_name AS VerifiedByName,
                       posted_by_actor_id AS PostedByActorId, posted_by_name AS PostedByName,
                       audit_hash AS AuditHash, memo AS Memo
                FROM gl_journal_batches
                WHERE accounting_period = @AccountingPeriod
                  AND (@PostingStatus IS NULL OR @PostingStatus = 'ALL' OR posting_status = @PostingStatus)
                  AND (@SourceModule IS NULL OR @SourceModule = 'ALL' OR source_module = @SourceModule)
                ORDER BY accounting_date DESC, journal_number DESC
                LIMIT @Limit;
                """;
            var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var headers = (await connection.QueryAsync<JournalHeader>(new CommandDefinition(sql,
                new { AccountingPeriod = accountingPeriod, PostingStatus = postingStatus, SourceModule = sourceModule, Limit = Math.Clamp(limit, 1, 500) },
                connectionScope.CurrentTransaction, SqlTimeouts.FastReadSeconds,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
            var result = new List<GlJournalBatchRecord>(headers.Count);
            foreach (JournalHeader header in headers)
                result.Add(await HydrateAsync(header, connection, cancellationToken).ConfigureAwait(false));
            return result;
        }

        public async Task UpdateJournalDecisionAsync(
            Guid journalId, string newPostingStatus, string actorId, string actorDisplayName,
            string auditHash, CancellationToken cancellationToken = default)
        {
            const string sql = """
                UPDATE gl_journal_batches
                SET posting_status = @NewPostingStatus,
                    verified_by_actor_id = COALESCE(verified_by_actor_id, @ActorId),
                    verified_by_name = COALESCE(verified_by_name, @ActorDisplayName),
                    posted_by_actor_id = CASE WHEN @NewPostingStatus = 'Posted' THEN @ActorId ELSE posted_by_actor_id END,
                    posted_by_name = CASE WHEN @NewPostingStatus = 'Posted' THEN @ActorDisplayName ELSE posted_by_name END,
                    audit_hash = @AuditHash, updated_at_utc = NOW()
                WHERE journal_id = @JournalId AND posting_status <> 'Posted';
                """;
            var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                JournalId = journalId, NewPostingStatus = newPostingStatus, ActorId = actorId,
                ActorDisplayName = actorDisplayName, AuditHash = auditHash
            }, connectionScope.CurrentTransaction, SqlTimeouts.StandardCommandSeconds,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<GlTrialBalanceRowRecord>> GetTrialBalanceAsync(
            string accountingPeriod, string? departmentId = null, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT account_code AS AccountCode, account_name AS AccountName,
                       account_type AS AccountType, normal_balance AS NormalBalance,
                       opening_debit_centavos AS OpeningDebitCentavos,
                       opening_credit_centavos AS OpeningCreditCentavos,
                       period_debit_centavos AS PeriodDebitCentavos,
                       period_credit_centavos AS PeriodCreditCentavos,
                       ending_debit_centavos AS EndingDebitCentavos,
                       ending_credit_centavos AS EndingCreditCentavos
                FROM fn_gl_compute_trial_balance(@AccountingPeriod, @DepartmentId);
                """;
            var connection = await connectionScope.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return (await connection.QueryAsync<GlTrialBalanceRowRecord>(new CommandDefinition(sql,
                new { AccountingPeriod = accountingPeriod, DepartmentId = departmentId },
                connectionScope.CurrentTransaction, SqlTimeouts.FastReadSeconds,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        }

        private async Task<GlJournalBatchRecord> HydrateAsync(JournalHeader header, DbConnection connection, CancellationToken cancellationToken)
        {
            const string sql = """
                SELECT l.line_id AS LineId, l.journal_id AS JournalId, l.line_number AS LineNumber,
                       l.account_code AS AccountCode, c.account_name AS AccountName,
                       l.debit_centavos AS DebitCentavos, l.credit_centavos AS CreditCentavos,
                       l.department_id AS DepartmentId, l.cost_center_id AS CostCenterId,
                       l.purpose_code AS PurposeCode, l.tax_profile AS TaxProfile,
                       l.description AS Description
                FROM gl_journal_lines l
                INNER JOIN gl_chart_of_accounts c ON c.account_code = l.account_code
                WHERE l.journal_id = @JournalId ORDER BY l.line_number;
                """;
            var lines = (await connection.QueryAsync<GlJournalLineRecord>(new CommandDefinition(sql,
                new { header.JournalId }, connectionScope.CurrentTransaction,
                SqlTimeouts.FastReadSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
            return new GlJournalBatchRecord(
                header.JournalId, header.JournalNumber, header.SourceModule, header.SourceDocumentId,
                header.SourceDocumentNumber, header.AccountingPeriod, header.AccountingDate,
                header.PostingStatus, header.PolicyVersionId, header.TaxRuleVersion,
                header.TotalDebitCentavos, header.TotalCreditCentavos, header.PreparedByActorId,
                header.PreparedByName, header.VerifiedByActorId, header.VerifiedByName,
                header.PostedByActorId, header.PostedByName, header.AuditHash, header.Memo, lines);
        }
    }
#pragma warning restore CA1034, CA1812

    public async Task<AdvanceBalanceDto> GetOutstandingBalanceAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return new AdvanceBalanceDto(string.Empty, 0, 0, "PHP");
        }

        IReadOnlyList<CashAdvance> advances = await _advanceRepo.ListAsync(null, null, 100, cancellationToken).ConfigureAwait(false);
        var employeeAdvances = advances.Where(a => string.Equals(a.EmployeeId, employeeId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        long totalOutstanding = 0;
        int activeCount = 0;

        foreach (CashAdvance adv in employeeAdvances)
        {
            if (adv.PaymentStatus == Domain.CashAdvances.StateMachines.PaymentStatus.EvidenceVerified &&
                adv.SettlementStatus != Domain.CashAdvances.StateMachines.SettlementStatus.Closed)
            {
                IReadOnlyList<AdvanceLedgerEntry> entries = await _ledgerStore.GetEntriesByAdvanceIdAsync(adv.Id, cancellationToken).ConfigureAwait(false);
                var bal = AdvanceBalanceCalculator.CalculateOutstandingBalance(entries, adv.RequestedAmount.CurrencyCode);
                if (bal.AmountMinor > 0)
                {
                    totalOutstanding += bal.AmountMinor;
                    activeCount++;
                }
            }
        }

        return new AdvanceBalanceDto(employeeId, totalOutstanding, activeCount, "PHP");
    }
}

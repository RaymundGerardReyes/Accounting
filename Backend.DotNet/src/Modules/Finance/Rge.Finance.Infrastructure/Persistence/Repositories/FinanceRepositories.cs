using System.Collections.Concurrent;
using System.Data.Common;
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

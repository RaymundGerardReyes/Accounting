using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Finance.Application.GeneralLedger;
using Rge.Finance.Application.GeneralLedger.PayMongoSettlement;
using Rge.Finance.Application.Payables;
using Rge.Finance.Application.Ports;
using Rge.Finance.Application.SecurityGuards;
using Xunit;

namespace Rge.Finance.Application.Tests;

public sealed class OfficialGlLifecycleTests
{
    [Fact]
    public async Task PayMongoCaptureAndSettlementReconcileClearingAndFee()
    {
        var repository = new InMemoryGlRepository();
        var handler = new ReconcilePayMongoSettlementHandler(
            new SubledgerGlPostingService(repository, new TestActor("payment-worker"), new TestClock()));

        Result<Guid> capture = await handler.HandleAsync(new RecordCustomerPaymentCaptureCommand(
            Guid.NewGuid(), "ORD-100", "pay-100", 1_000_000, true,
            new DateOnly(2026, 11, 15), "DEPT-OPS", "CC-SALES", "POL-1"));
        Result<Guid> settlement = await handler.HandleAsync(new ReconcilePayMongoBankSettlementCommand(
            Guid.NewGuid(), "pay-100", "BANK-100", 1_000_000, 30_000, 970_000,
            new DateOnly(2026, 11, 16), "DEPT-OPS", "CC-SALES", "POL-1"));

        Assert.True(capture.IsSuccess);
        Assert.True(settlement.IsSuccess);
        Assert.Equal(2, repository.Batches.Count);
        Assert.Equal(892_857, repository.Batches[0].Lines.Single(x => x.AccountCode == "4010").CreditCentavos);
        Assert.Equal(30_000, repository.Batches[1].Lines.Single(x => x.AccountCode == "5310").DebitCentavos);
    }

    [Fact]
    public async Task PayMongoSettlementRejectsGrossToNetMismatch()
    {
        var repository = new InMemoryGlRepository();
        var handler = new ReconcilePayMongoSettlementHandler(
            new SubledgerGlPostingService(repository, new TestActor("payment-worker"), new TestClock()));

        Result<Guid> result = await handler.HandleAsync(new ReconcilePayMongoBankSettlementCommand(
            Guid.NewGuid(), "pay-101", "BANK-101", 1_000_000, 25_000, 970_000,
            new DateOnly(2026, 11, 16), "DEPT-OPS", "CC-SALES", "POL-1"));

        Assert.True(result.IsFailure);
        Assert.Equal("PayMongo.GrossToNetMismatch", result.Error.Code);
        Assert.Empty(repository.Batches);
    }

    [Fact]
    public async Task OfficialPayableEnforcesVatEwtAndBankSnapshotControls()
    {
        var repository = new InMemoryGlRepository();
        var actor = new TestActor("controller");
        var posting = new SubledgerGlPostingService(repository, actor, new TestClock());
        var register = new RegisterOfficialPayableHandler(posting);

        Result<Guid> payable = await register.HandleAsync(new RegisterOfficialPayableCommand(
            Guid.NewGuid(), Guid.NewGuid(), true, false, " SI-100 ", new DateOnly(2026, 11, 12),
            Guid.NewGuid(), "GRN-100", 1_120_000, 1_120_000, 1_120_000,
            "5210", "Services", true, true, 200, "DEPT-OPS", "CC-OPS", "POL-1"));

        Assert.True(payable.IsSuccess);
        Assert.Equal(1_000_000, repository.Batches[0].Lines.Single(x => x.AccountCode == "5210").DebitCentavos);
        Assert.Equal(20_000, repository.Batches[0].Lines.Single(x => x.AccountCode == "2120").CreditCentavos);

        var snapshot = FrozenBeneficiaryBankSnapshot.Capture(Guid.NewGuid(), "BDO", "****4419");
        var execute = new ExecuteOfficialPayablePaymentHandler(actor, new SodGuard(), posting);
        Result<Guid> blocked = await execute.HandleAsync(new ExecuteOfficialPayablePaymentCommand(
            Guid.NewGuid(), Guid.NewGuid(), "SI-100", 1_100_000, "maker", "verifier", snapshot,
            "BPI", "****9999", false, "BANK-100", new DateOnly(2026, 11, 14), "POL-1"));

        Assert.True(blocked.IsFailure);
        Assert.Equal("Payables.BeneficiaryBankSnapshotMismatch", blocked.Error.Code);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class TestActor(string actorId) : ICurrentActor
    {
        public string ActorId { get; } = actorId;
        public string EmployeeCode => ActorId;
        public string DepartmentId => "DEPT-FIN";
        public string CostCenterId => "CC-CORP-00";
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FinanceController" };
        public string DepartmentScope => "DEPT-FIN";
        public string CorrelationId => "test-correlation";
        public bool IsAuthenticated => true;
        public bool HasRole(string role) => Roles.Contains(role);
        public bool HasScopeForDepartment(string targetDepartmentId) =>
            string.Equals(targetDepartmentId, DepartmentId, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class InMemoryGlRepository : IGeneralLedgerRepository
    {
        public List<GlJournalBatchRecord> Batches { get; } = [];
        private int _sequence;

        public Task<IReadOnlyList<GlChartOfAccountRecord>> GetActiveChartOfAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GlChartOfAccountRecord>>([]);
        public Task<string> NextJournalNumberAsync(int fiscalYear, CancellationToken cancellationToken = default) =>
            Task.FromResult($"JE-{fiscalYear}-{++_sequence:D4}");
        public Task InsertJournalBatchAsync(GlJournalBatchRecord batch, CancellationToken cancellationToken = default)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }
        public Task<GlJournalBatchRecord?> GetJournalByIdAsync(Guid journalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.SingleOrDefault(x => x.JournalId == journalId));
        public Task<IReadOnlyList<GlJournalBatchRecord>> ListJournalBatchesByPeriodAsync(
            string accountingPeriod, string? postingStatus = null, string? sourceModule = null,
            int limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GlJournalBatchRecord>>(Batches);
        public Task UpdateJournalDecisionAsync(
            Guid journalId, string newPostingStatus, string actorId, string actorDisplayName,
            string auditHash, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<GlTrialBalanceRowRecord>> GetTrialBalanceAsync(
            string accountingPeriod, string? departmentId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GlTrialBalanceRowRecord>>([]);
    }
}

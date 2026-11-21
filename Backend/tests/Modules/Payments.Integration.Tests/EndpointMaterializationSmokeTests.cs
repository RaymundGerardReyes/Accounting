using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rge.Finance.Application.Ports;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// In-memory fake repository allowing Minimal API endpoint invocation tests to run
/// deterministically in CI without requiring an active PostgreSQL connection.
/// </summary>
public sealed class InMemoryGeneralLedgerRepository : IGeneralLedgerRepository
{
    public Task<IReadOnlyList<GlChartOfAccountRecord>> GetActiveChartOfAccountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GlChartOfAccountRecord>>([
            new GlChartOfAccountRecord("1010", "Cash on Hand", "Asset", "Debit", "VAT_STANDARD", 0L, 0L, true, "Cash on Hand")
        ]);

    public Task<string> NextJournalNumberAsync(int fiscalYear, CancellationToken cancellationToken = default) =>
        Task.FromResult($"JRN-{fiscalYear}-0001");

    public Task InsertJournalBatchAsync(GlJournalBatchRecord batch, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<GlJournalBatchRecord?> GetJournalByIdAsync(Guid journalId, CancellationToken cancellationToken = default) =>
        Task.FromResult<GlJournalBatchRecord?>(null);

    public Task<IReadOnlyList<GlJournalBatchRecord>> ListJournalBatchesByPeriodAsync(
        string accountingPeriod,
        string? postingStatus = null,
        string? sourceModule = null,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GlJournalBatchRecord>>([
            new(
                Guid.NewGuid(), "JRN-2026-0001", "ManualAdjustment", "doc-1", "INV-001",
                accountingPeriod, DateOnly.FromDateTime(DateTime.UtcNow), "Validated", "MANUAL", "BIR-EOPT-RR7-2024",
                10000, 10000, "emp_01", "Preparer", null, null, null, null,
                "dummyhash", "Test journal", [])
        ]);

    public Task UpdateJournalDecisionAsync(
        Guid journalId,
        string newPostingStatus,
        string actorId,
        string actorDisplayName,
        string auditHash,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<GlTrialBalanceRowRecord>> GetTrialBalanceAsync(
        string accountingPeriod,
        string? departmentId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GlTrialBalanceRowRecord>>([
            new("1010", "Cash on Hand", "Asset", "Debit", 10000, 0, 5000, 0, 15000, 0)
        ]);
}

/// <summary>
/// Guardrail R3: Verifies that the WebHost boots cleanly and that all Minimal API endpoints
/// compile their RequestDelegates and parameter bindings without InvalidOperationException.
/// </summary>
public sealed class EndpointMaterializationSmokeTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EndpointMaterializationSmokeTests(AccountingWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IGeneralLedgerRepository, InMemoryGeneralLedgerRepository>());
            });
        }).CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    public void EndpointDataSource_AllRouteEndpoints_MaterializeRequestDelegatesWithoutBindingExceptions()
    {
        // Act: Retrieve all route endpoints from EndpointDataSource
        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var routeEndpoints = dataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        Assert.NotEmpty(routeEndpoints);

        // Assert: Forcing evaluation of RequestDelegate triggers RequestDelegateFactory compilation
        // on every Minimal API route. Any missing DI service or invalid [FromBody] inference on GET throws here.
        foreach (var endpoint in routeEndpoints)
        {
            Assert.NotNull(endpoint.RequestDelegate);
        }
    }

    [Fact]
    public async Task FinanceGeneralLedger_GetAccounts_BindsParametersAndReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/v1/finance/gl/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FinanceGeneralLedger_GetJournals_BindsParametersAndReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/v1/finance/gl/journals?period=2026-11");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FinanceGeneralLedger_GetTrialBalance_BindsParametersAndReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/v1/finance/gl/trial-balance?period=2026-11");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

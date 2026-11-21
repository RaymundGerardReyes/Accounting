using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Behaviors;
using Rge.Finance.Application.GeneralLedger;
using Rge.Finance.Application.Ports;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Guardrail R3: Validates that DI container dependencies resolve cleanly in scopes
/// and that pipeline behaviors, handlers, and repositories are correctly registered,
/// while scope validation prevents scoped leakage to the root container.
/// </summary>
public sealed class DependencyInjectionValidationTests : IClassFixture<AccountingWebApplicationFactory>
{
    private readonly AccountingWebApplicationFactory _factory;

    public DependencyInjectionValidationTests(AccountingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ServiceScope_ResolvesPipelineAndFinanceHandlersWithoutException()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Verify Core BuildingBlock dependencies
        Assert.NotNull(sp.GetRequiredService<IApplicationCache>());
        Assert.NotNull(sp.GetRequiredService<ICurrentActor>());
        Assert.NotNull(sp.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(sp.GetRequiredService<IAuditBehaviorWriter>());
        Assert.NotNull(sp.GetRequiredService<RequestPipelineExecutor>());

        // Verify Finance General Ledger handlers
        Assert.NotNull(sp.GetRequiredService<GetGeneralLedgerJournalsHandler>());
        Assert.NotNull(sp.GetRequiredService<GetGeneralLedgerTrialBalanceHandler>());
        Assert.NotNull(sp.GetRequiredService<CreateManualJournalHandler>());
        Assert.NotNull(sp.GetRequiredService<DecideJournalApprovalHandler>());

        // Verify Finance Repositories & Services
        Assert.NotNull(sp.GetRequiredService<IGeneralLedgerRepository>());
        Assert.NotNull(sp.GetRequiredService<ISubledgerGlPostingService>());
    }

    [Fact]
    public void RootContainer_ResolvingScopedService_ThrowsInvalidOperationException()
    {
        // Asserts ValidateScopes is active: resolving scoped service from root container must throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            _factory.Services.GetRequiredService<RequestPipelineExecutor>());
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.Payments.Application.Orders.ListOrders;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Persistence.Mappings;
using Rge.Payments.Infrastructure.Persistence.Repositories;
using Rge.Payments.Infrastructure.Persistence.Sql;
using Xunit;

namespace Rge.Payments.Integration.Tests;

public sealed class PostgreSqlLiveDatabaseLedgerTests
{
    private const string ExpectedDbUrl = "postgresql+psycopg2://finance_test_user:FinanceLocal17_Test!@127.0.0.1:5432/finance_local_test?sslmode=disable";

    [Fact]
    public async Task LivePostgreSql_DirectRowMaterialization_Succeeds()
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(ExpectedDbUrl);
        await using var dataSource = NpgsqlDataSourceFactory.Create(normalized);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM public.orders WHERE id = 'ord_001';";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "ord_001 should exist");

        var order = await OrderMaterializer.MaterializeAsync(reader, CancellationToken.None);
        Assert.NotNull(order);
        Assert.Equal("ord_001", order.Id);
        Assert.Equal("ORD-2026-0001", order.OrderNumber);
        Assert.Equal("paid", order.Status.ToString().ToLowerInvariant());
    }

    [Fact]
    public void NormalizeConnectionString_WithLocalhostAndSslModeDisable_SetsSslModeDisable()
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(ExpectedDbUrl);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("127.0.0.1", csb.Host);
        Assert.Equal(5432, csb.Port);
        Assert.Equal("finance_local_test", csb.Database);
        Assert.Equal("finance_test_user", csb.Username);
        Assert.Equal("FinanceLocal17_Test!", csb.Password);
        Assert.Equal(SslMode.Disable, csb.SslMode);
        Assert.True(csb.Pooling);
    }

    [Fact]
    public async Task LivePostgreSql_ConnectAndQueryOrders_ReturnsAuthoritativeOrders()
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(ExpectedDbUrl);

        await using var dataSource = NpgsqlDataSourceFactory.Create(normalized);
        await using var conn = await dataSource.OpenConnectionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM public.orders;";
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);

        Assert.True(count >= 2, $"Expected at least 2 orders in public.orders, found {count}");
    }

    [Fact]
    public async Task LivePostgreSql_OrderRepositoryListAsync_ReturnsAuthoritativeOrdersWithFullMapping()
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(ExpectedDbUrl);
        await using var dataSource = NpgsqlDataSourceFactory.Create(normalized);

        // Test direct SQL execution first to capture any exception
        await using (var testConn = await dataSource.OpenConnectionAsync())
        await using (var testCmd = testConn.CreateCommand())
        {
            testCmd.CommandText = OrderQueries.SelectListSql;
            var p1 = testCmd.CreateParameter();
            p1.ParameterName = "@status";
            p1.Value = DBNull.Value;
            testCmd.Parameters.Add(p1);

            var p2 = testCmd.CreateParameter();
            p2.ParameterName = "@UserId";
            p2.Value = DBNull.Value;
            testCmd.Parameters.Add(p2);

            var p3 = testCmd.CreateParameter();
            p3.ParameterName = "@limit";
            p3.Value = 50;
            testCmd.Parameters.Add(p3);

            try
            {
                await using var reader = await testCmd.ExecuteReaderAsync();
                int rows = 0;
                while (await reader.ReadAsync())
                {
                    rows++;
                }
                Assert.True(rows >= 2, $"Direct SQL read returned {rows} rows (expected >= 2)");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Direct SQL execution failed: {ex.GetType().Name} - {ex.Message}");
            }
        }

        using var factory = new NpgsqlDataSourceFactory(dataSource);
        using var connectionScope = new ConnectionScope(factory);
        var orderRepo = new OrderRepository(connectionScope);

        var orders = await orderRepo.ListAsync(status: null, limit: 50, userId: null);

        Assert.NotNull(orders);
        Assert.True(orders.Count >= 2, $"Expected at least 2 orders from repository, got {orders.Count}");

        var ord1 = orders.FirstOrDefault(o => o.OrderNumber == "ORD-2026-0001");
        Assert.NotNull(ord1);
        Assert.Equal("paid", ord1.Status.ToString().ToLowerInvariant());
        Assert.Equal(1120000, ord1.Amount.AmountMinor);

        var ord2 = orders.FirstOrDefault(o => o.OrderNumber == "ORD-2026-0002");
        Assert.NotNull(ord2);
        Assert.True(ord2.Status.ToString().Equals("pending", StringComparison.OrdinalIgnoreCase) ||
                    ord2.Status.ToString().Equals("pending_payment", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(560000, ord2.Amount.AmountMinor);
    }

    [Theory]
    [InlineData("all", 2)]
    [InlineData("pending", 1)]
    [InlineData("paid", 1)]
    public async Task LivePostgreSql_StatusFiltering_CorrectlyResolvesPendingAndPaid(string filterStatus, int expectedMinCount)
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(ExpectedDbUrl);
        await using var dataSource = NpgsqlDataSourceFactory.Create(normalized);
        using var factory = new NpgsqlDataSourceFactory(dataSource);

        using var connectionScope = new ConnectionScope(factory);
        var orderRepo = new OrderRepository(connectionScope);

        var orders = await orderRepo.ListAsync(status: filterStatus, limit: 50, userId: null);

        Assert.NotNull(orders);
        Assert.True(orders.Count >= expectedMinCount,
            $"Expected at least {expectedMinCount} orders for filter '{filterStatus}', but got {orders.Count}");
    }

    [Fact]
    public async Task LivePostgreSql_ListOrdersHandler_WithPrivilegedOrUnauthenticatedActor_ReturnsFullLedger()
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(ExpectedDbUrl);
        await using var dataSource = NpgsqlDataSourceFactory.Create(normalized);
        using var factory = new NpgsqlDataSourceFactory(dataSource);

        using var connectionScope = new ConnectionScope(factory);
        var orderRepo = new OrderRepository(connectionScope);
        var attemptRepo = new InMemoryPaymentAttemptRepository();

        // 1. Unauthenticated actor -> full ledger
        var unauthActor = new TestCurrentActor(isAuthenticated: false, actorId: "anonymous", roles: []);
        var handler = new ListOrdersHandler(orderRepo, attemptRepo, unauthActor);

        var result = await handler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 50));
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Count >= 2, $"Unauthenticated dashboard should see authoritative ledger, got {result.Value.Count}");

        // 2. Privileged finance reviewer -> full ledger
        var finReviewer = new TestCurrentActor(isAuthenticated: true, actorId: "usr_fin_01", roles: ["finance_reviewer"]);
        var reviewerHandler = new ListOrdersHandler(orderRepo, attemptRepo, finReviewer);

        var reviewerResult = await reviewerHandler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 50));
        Assert.True(reviewerResult.IsSuccess);
        Assert.True(reviewerResult.Value.Count >= 2, $"Finance reviewer should see authoritative ledger, got {reviewerResult.Value.Count}");

        // 3. System admin -> full ledger
        var adminActor = new TestCurrentActor(isAuthenticated: true, actorId: "usr_adm_01", roles: ["system_admin"]);
        var adminHandler = new ListOrdersHandler(orderRepo, attemptRepo, adminActor);

        var adminResult = await adminHandler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 50));
        Assert.True(adminResult.IsSuccess);
        Assert.True(adminResult.Value.Count >= 2, $"System admin should see authoritative ledger, got {adminResult.Value.Count}");
    }

    private sealed class TestCurrentActor : ICurrentActor
    {
        public string ActorId { get; }
        public string EmployeeCode => $"EMP-{ActorId}";
        public string DepartmentId => "finance";
        public string CostCenterId => "CC-001";
        public IReadOnlySet<string> Roles { get; }
        public string DepartmentScope => "*";
        public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
        public bool IsAuthenticated { get; }

        public TestCurrentActor(bool isAuthenticated, string actorId, IEnumerable<string> roles)
        {
            IsAuthenticated = isAuthenticated;
            ActorId = actorId;
            Roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        }

        public bool HasRole(string role) => Roles.Contains(role);
        public bool HasScopeForDepartment(string targetDepartmentId) => true;
    }

    private sealed class InMemoryPaymentAttemptRepository : IPaymentAttemptRepository
    {
        public Task<PaymentAttempt?> GetByIdAsync(string attemptId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentAttempt?>(null);
        public Task<PaymentAttempt?> GetByProviderPaymentIdAsync(string providerPaymentId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentAttempt?>(null);
        public Task<PaymentAttempt?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) => Task.FromResult<PaymentAttempt?>(null);
        public Task<IReadOnlyList<PaymentAttempt>> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PaymentAttempt>>([]);
        public Task<PaymentAttempt?> GetLatestByOrderIdAsync(string orderId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentAttempt?>(null);
        public Task<IReadOnlyDictionary<string, PaymentAttempt>> GetLatestByOrderIdsAsync(IReadOnlyCollection<string> orderIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, PaymentAttempt>>(new Dictionary<string, PaymentAttempt>());
        public Task<IReadOnlyList<PaymentAttempt>> ListNonTerminalAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PaymentAttempt>>([]);
        public Task SaveAsync(PaymentAttempt attempt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

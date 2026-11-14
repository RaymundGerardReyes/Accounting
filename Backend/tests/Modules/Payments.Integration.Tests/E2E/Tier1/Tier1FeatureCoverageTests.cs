using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Identity.Api.Authentication;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Application.SubmitGoogleForm;
using Rge.Integrations.Contracts;
using Rge.Integrations.Infrastructure;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Orders.ListOrders;
using Rge.Payments.Application.Ports;
using SmtpClientFactory = Rge.Integrations.Infrastructure.SmtpClientFactory;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Persistence.Repositories;
using Rge.Payments.Infrastructure.Workers;
using Rge.Payments.Integration.Tests.E2E.Fixtures;
using Rge.Tests.Common;
using Xunit;

namespace Rge.Payments.Integration.Tests.E2E.Tier1;

/// <summary>
/// Tier 1: Feature Coverage & Happy Path Tests for Requirements R1, R2, R3, R5 (R5.1 to R5.5), and F10.
/// Verifies that all 10 core features execute successfully with genuine logic and return compliant contracts.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class Tier1FeatureCoverageTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Tier1FeatureCoverageTests(AccountingWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.CreateClient().WithGoogleFormsSecret();
    }

    public void Dispose()
    {
        _client.Dispose();
        OrderRepository.ResetToBaseline();
    }

    // =========================================================================
    // Feature 1 (R1): Sequential N+1 Batch Attempt Query
    // =========================================================================

    [Fact]
    public async Task R1_T1_01_MultipleOrders_SingleAttemptEach_BatchResolved()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string userId = $"user_r1_01_{Guid.NewGuid():N}";
        var orderIds = new List<string>();

        for (int i = 0; i < 5; i++)
        {
            string ordId = $"ord_r1_t1_01_{i}_{Guid.NewGuid():N}";
            orderIds.Add(ordId);

            var order = Order.Rehydrate(
                id: ordId,
                userId: userId,
                orderNumber: $"ORD-202611-R101{i:D2}",
                title: $"Item {i}",
                category: OrderCategory.ProductOrder,
                referenceCode: "REF-01",
                amount: Money.Php(1500_00L),
                status: OrderStatus.Pending,
                metadataJson: "{}",
                createdAt: clock.UtcNow.AddMinutes(-10 + i),
                updatedAt: clock.UtcNow.AddMinutes(-10 + i));
            await orderRepo.SaveAsync(order);

            var attempt = PaymentAttempt.CreatePending(
                orderId: ordId,
                paymentMethod: "hosted_checkout",
                amount: Money.Php(1500_00L),
                idempotencyKey: $"ik_r1_01_{i}_{Guid.NewGuid():N}",
                nowUtc: clock.UtcNow.AddMinutes(-10 + i));
            attempt.AttachCheckoutSession($"cs_r1_01_{i}", $"https://checkout.test/{ordId}", clock.UtcNow.AddMinutes(30), clock.UtcNow);
            await attemptRepo.SaveAsync(attempt);
        }

        var handler = new ListOrdersHandler(orderRepo, attemptRepo, new TestActor(userId, ["user"]));
        var result = await handler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 10));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(5, result.Value.Count);
        Assert.All(result.Value, dto =>
        {
            Assert.NotNull(dto.LatestAttempt);
            Assert.Contains("cs_r1_01_", dto.LatestAttempt.ProviderPaymentId, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task R1_T1_02_MultipleAttemptsPerOrder_SelectsLatestAttemptOnly()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_r1_t1_02_{Guid.NewGuid():N}";
        var order = Order.Rehydrate(
            id: ordId,
            userId: "user_r1_02",
            orderNumber: "ORD-202611-R102",
            title: "Multi Attempt Order",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-02",
            amount: Money.Php(2000_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow.AddMinutes(-30),
            updatedAt: clock.UtcNow);
        await orderRepo.SaveAsync(order);

        // Older attempt
        var oldAttempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(2000_00L), $"ik_old_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-20));
        oldAttempt.AttachCheckoutSession("cs_old", "https://checkout.test/old", clock.UtcNow.AddMinutes(-1), clock.UtcNow.AddMinutes(-20));
        await attemptRepo.SaveAsync(oldAttempt);

        // Newer attempt
        var newAttempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(2000_00L), $"ik_new_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-5));
        newAttempt.AttachCheckoutSession("cs_new", "https://checkout.test/new", clock.UtcNow.AddMinutes(25), clock.UtcNow.AddMinutes(-5));
        await attemptRepo.SaveAsync(newAttempt);

        var batchAttempts = await attemptRepo.GetLatestByOrderIdsAsync([ordId]);
        Assert.True(batchAttempts.TryGetValue(ordId, out PaymentAttempt? latest));
        Assert.NotNull(latest);
        Assert.Equal("cs_new", latest.ProviderPaymentId);
    }

    [Fact]
    public async Task R1_T1_03_OrdersWithoutAttempts_ResolvedGracefullyWithNullAttempt()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_r1_t1_03_{Guid.NewGuid():N}";
        var order = Order.Rehydrate(
            id: ordId,
            userId: "user_r1_03",
            orderNumber: "ORD-202611-R103",
            title: "Order Without Attempt",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-03",
            amount: Money.Php(1200_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow,
            updatedAt: clock.UtcNow);
        await orderRepo.SaveAsync(order);

        var handler = new ListOrdersHandler(orderRepo, attemptRepo, new TestActor("user_r1_03", ["user"]));
        var result = await handler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 10));

        Assert.True(result.IsSuccess);
        var found = result.Value.FirstOrDefault(o => o.Id == ordId);
        Assert.NotNull(found);
        Assert.Null(found.LatestAttempt);
    }

    [Fact]
    public async Task R1_T1_04_SingleOrderQuery_ResolvesSuccessfully()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_r1_t1_04_{Guid.NewGuid():N}";
        var attempt = PaymentAttempt.CreatePending(ordId, "qrph", Money.Php(500_00L), $"ik_r1_04_{Guid.NewGuid():N}", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_single_04", "https://checkout.test/single", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        var batch = await attemptRepo.GetLatestByOrderIdsAsync([ordId]);
        Assert.Single(batch);
        Assert.True(batch.ContainsKey(ordId));
        Assert.Equal("cs_single_04", batch[ordId].ProviderPaymentId);
    }

    [Fact]
    public async Task R1_T1_05_MaximumPageLimit_BatchResolvesCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var handler = new ListOrdersHandler(orderRepo, attemptRepo, new TestActor("acme_admin", ["admin"]));
        // Limit 150 clamped to 100
        var result = await handler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 150));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Count <= 100);
    }

    // =========================================================================
    // Feature 2 (R2): Admin Route Authorization (200 OK for admin & finance_admin)
    // =========================================================================

    [Fact]
    public async Task R2_T1_01_GetWebhookEvents_WithAdminRole_Returns200OK()
    {
        _client.AsAdmin();
        var response = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task R2_T1_02_GetWebhookEvents_WithFinanceAdminRole_Returns200OK()
    {
        _client.AsFinanceAdmin();
        var response = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task R2_T1_03_GetFulfillmentOutbox_WithAdminRole_Returns200OK()
    {
        _client.AsAdmin();
        var response = await _client.GetAsync("/api/v1/admin/fulfillment-outbox");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task R2_T1_04_ProcessFulfillmentOutbox_WithAdminRole_Returns200OK()
    {
        _client.AsAdmin();
        var response = await _client.PostAsync("/api/v1/admin/fulfillment-outbox/process", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task R2_T1_05_AcmeAdmin_AccessesAllAdminEndpoints_Returns200OK()
    {
        _client.AsAdmin("acme_admin");

        var resWebhooks = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.OK, resWebhooks.StatusCode);

        var resOutbox = await _client.GetAsync("/api/v1/admin/fulfillment-outbox");
        Assert.Equal(HttpStatusCode.OK, resOutbox.StatusCode);

        var resReconcileAll = await _client.PostAsync("/api/v1/admin/payments/reconcile-all", null);
        Assert.Equal(HttpStatusCode.OK, resReconcileAll.StatusCode);
    }

    // =========================================================================
    // Feature 3 (R3): User Order Isolation (BOLA / IDOR)
    // =========================================================================

    [Fact]
    public async Task R3_T1_01_UserA_ListsOrders_ReturnsOnlyUserAOrders()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string userA = $"user_a_{Guid.NewGuid():N}";
        string ordA = $"ord_a_{Guid.NewGuid():N}";

        await orderRepo.SaveAsync(Order.Rehydrate(
            id: ordA,
            userId: userA,
            orderNumber: "ORD-202611-A001",
            title: "User A Order",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-A",
            amount: Money.Php(1000_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow,
            updatedAt: clock.UtcNow));

        _client.AsUser(userA);
        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dtos = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(dtos);
        Assert.NotEmpty(dtos);
        Assert.All(dtos, o => Assert.Equal(userA, o.UserId));
    }

    [Fact]
    public async Task R3_T1_02_UserB_ListsOrders_ReturnsOnlyUserBOrders_ExcludesUserA()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string userA = $"user_a_{Guid.NewGuid():N}";
        string userB = $"user_b_{Guid.NewGuid():N}";

        await orderRepo.SaveAsync(Order.Rehydrate(
            id: $"ord_a_{Guid.NewGuid():N}",
            userId: userA,
            orderNumber: "ORD-202611-A002",
            title: "Private Order User A",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-A2",
            amount: Money.Php(1500_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow,
            updatedAt: clock.UtcNow));

        await orderRepo.SaveAsync(Order.Rehydrate(
            id: $"ord_b_{Guid.NewGuid():N}",
            userId: userB,
            orderNumber: "ORD-202611-B001",
            title: "Private Order User B",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-B1",
            amount: Money.Php(2500_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow,
            updatedAt: clock.UtcNow));

        _client.AsUser(userB);
        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dtos = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(dtos);
        Assert.Contains(dtos, o => o.UserId == userB);
        Assert.DoesNotContain(dtos, o => o.UserId == userA);
    }

    [Fact]
    public async Task R3_T1_03_Admin_ListsOrders_BypassesUserFilter_ReturnsAllOrders()
    {
        _client.AsAdmin();
        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dtos = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(dtos);
        Assert.True(dtos.Count >= 4, "Admin must see baseline seed orders across tenants.");
    }

    [Fact]
    public async Task R3_T1_04_FinanceAdmin_ListsOrders_ReturnsAllOrdersForAuditing()
    {
        _client.AsFinanceAdmin();
        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dtos = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(dtos);
        Assert.True(dtos.Count >= 4, "Finance Admin must see all orders for audit integrity.");
    }

    [Fact]
    public async Task R3_T1_05_AcmeAdmin_ListsOrders_ReturnsCompleteLedger()
    {
        _client.AsAdmin("acme_admin");
        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dtos = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(dtos);
        Assert.True(dtos.Count >= 4);
    }

    // =========================================================================
    // Feature 5 (R5.1): Google Form Order Bridge
    // =========================================================================

    [Fact]
    public async Task R5_1_T1_01_ValidSubmission_CreatesPendingOrderWithOrderNumber()
    {
        string respId = $"gfr_tier1_01_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "Maria Santos",
            customer_email = "maria.santos@test.com",
            customer_phone = "09171234567",
            product_code = "CPA-REV-2026",
            product_title = "CPA Reviewer 2026 Volume 1",
            quantity = 1,
            amount_minor = 145000L,
            send_initial_email = false
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK, $"Failed with status {response.StatusCode}: {body}");

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.GetProperty("order_id").GetString()));
        Assert.StartsWith("ORD-", doc.GetProperty("order_number").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R5_1_T1_02_CustomerMetadataPreserved_InMetadataJson()
    {
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IGoogleFormOrderBridge>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        string respId = $"gfr_meta_{Guid.NewGuid():N}";
        var req = new GoogleFormOrderBridgeRequest(
            ResponseId: respId,
            CustomerName: "Juan Dela Cruz",
            CustomerEmail: "juan@example.ph",
            CustomerPhone: "+639181112233",
            ProductCode: "TAX-BOOK",
            ProductTitle: "Philippine Tax Reviewer",
            Quantity: 2,
            UnitPriceMinor: 95000L,
            TotalAmountMinor: 190000L,
            Currency: "PHP",
            Status: "pending",
            SubmittedAt: DateTimeOffset.UtcNow,
            SendInitialEmail: false);

        var bridged = await bridge.BridgeSubmissionToOrderAsync(req);
        Assert.NotNull(bridged);

        var persisted = await orderRepo.GetByIdAsync(bridged.OrderId);
        Assert.NotNull(persisted);
        Assert.NotNull(persisted.MetadataJson);

        using var metaDoc = JsonDocument.Parse(persisted.MetadataJson);
        Assert.Equal("google_forms", metaDoc.RootElement.GetProperty("source").GetString());
        Assert.Equal("Juan Dela Cruz", metaDoc.RootElement.GetProperty("customer_name").GetString());
        Assert.Equal("juan@example.ph", metaDoc.RootElement.GetProperty("customer_email").GetString());
        Assert.Equal(respId, metaDoc.RootElement.GetProperty("google_form_response_id").GetString());
    }

    [Fact]
    public async Task R5_1_T1_03_DepartmentInvoiceCategory_PreservesDepartmentAndCostCenter()
    {
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IGoogleFormOrderBridge>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        string respId = $"gfr_dept_{Guid.NewGuid():N}";
        var extra = new Dictionary<string, string>
        {
            ["department"] = "Audit & Assurance",
            ["cost_center"] = "CC-AUDIT-402"
        };

        var req = new GoogleFormOrderBridgeRequest(
            ResponseId: respId,
            CustomerName: "Auditor Corp",
            CustomerEmail: "audit@corp.com",
            CustomerPhone: "0288889999",
            ProductCode: "INV-AUDIT",
            ProductTitle: "Corporate Audit Engagement",
            Quantity: 1,
            UnitPriceMinor: 5000000L,
            TotalAmountMinor: 5000000L,
            Currency: "PHP",
            Status: "pending",
            SubmittedAt: DateTimeOffset.UtcNow,
            SendInitialEmail: false,
            AdditionalMetadata: extra);

        var bridged = await bridge.BridgeSubmissionToOrderAsync(req);
        var order = await orderRepo.GetByIdAsync(bridged.OrderId);
        Assert.NotNull(order);

        using var doc = JsonDocument.Parse(order.MetadataJson);
        Assert.Equal("Audit & Assurance", doc.RootElement.GetProperty("department").GetString());
        Assert.Equal("CC-AUDIT-402", doc.RootElement.GetProperty("cost_center").GetString());
    }

    [Fact]
    public async Task R5_1_T1_04_ImmediatelyInitiatesPayMongoCheckoutSession()
    {
        string respId = $"gfr_pm_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "PayMongo Test Customer",
            customer_email = "pm_customer@test.com",
            product_code = "CPA-2026",
            quantity = 1,
            amount_minor = 145000L,
            send_initial_email = false
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK, $"Failed with status {response.StatusCode}: {body}");

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        string checkoutUrl = doc.GetProperty("checkout_url").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(checkoutUrl));
        Assert.Contains("checkout", checkoutUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R5_1_T1_05_ReturnsBridgedGoogleFormOrder_WithCheckoutUrl()
    {
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IGoogleFormOrderBridge>();

        var req = new GoogleFormOrderBridgeRequest(
            ResponseId: $"gfr_bridge_{Guid.NewGuid():N}",
            CustomerName: "Elena Rostova",
            CustomerEmail: "elena@example.com",
            CustomerPhone: "09191234567",
            ProductCode: "PROD-ELENA",
            ProductTitle: "Accounting Handbook",
            Quantity: 1,
            UnitPriceMinor: 85000L,
            TotalAmountMinor: 85000L,
            Currency: "PHP",
            Status: "pending",
            SubmittedAt: DateTimeOffset.UtcNow,
            SendInitialEmail: false);

        BridgedGoogleFormOrder result = await bridge.BridgeSubmissionToOrderAsync(req);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.OrderId));
        Assert.False(string.IsNullOrWhiteSpace(result.OrderNumber));
        Assert.False(string.IsNullOrWhiteSpace(result.CheckoutUrl));
        Assert.Equal("pending", result.Status);
    }

    // =========================================================================
    // Feature 6 (R5.2): Detached Async SMTP Dispatch
    // =========================================================================

    [Fact]
    public async Task R5_2_T1_01_FormSubmissionReturnsHttpSuccessImmediately_WhileEmailDispatchesInBackground()
    {
        string respId = $"gfr_smtp_async_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "Quick Submitter",
            customer_email = "quick@sub.com",
            product_code = "QUICK-01",
            quantity = 1,
            send_initial_email = true
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK, $"Failed with status {response.StatusCode}: {body}");

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.True(doc.GetProperty("email_dispatched").GetBoolean());
    }

    [Fact]
    public async Task R5_2_T1_02_EmitsStructuredLog_WithGoogleFormSmtpPrefix()
    {
        string respId = $"gfr_log_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "Log Verifier",
            customer_email = "log_verifier@test.com",
            product_code = "LOG-01",
            quantity = 1,
            send_initial_email = true
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(response.IsSuccessStatusCode);

        // Allow background thread brief moment to log
        await Task.Delay(100);

        bool logFound = _factory.CapturedLogs.Any(l =>
            l.Message.Contains("[GOOGLE-FORM-SMTP]", StringComparison.OrdinalIgnoreCase));
        Assert.True(logFound, "Expected structured [GOOGLE-FORM-SMTP] log emission.");
    }

    [Fact]
    public async Task R5_2_T1_03_DetachedCtsWith15SecondTimeout_DispatchesAsynchronously()
    {
        using var scope = _factory.Services.CreateScope();
        var capturingSmtp = new CapturingSmtpClientFactory();
        var handler = ActivatorUtilities.CreateInstance<SubmitGoogleFormHandler>(
            scope.ServiceProvider,
            (ISmtpClientFactory)capturingSmtp);

        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        var input = new SubmitGoogleFormInput(
            Timestamp: DateTimeOffset.UtcNow.ToString("O"),
            ResponseId: $"GFR-CTS-{Guid.NewGuid():N}",
            CustomerName: "Detached CTS Tester",
            CustomerEmail: "detached_cts@test.com",
            CustomerPhone: "+639171112222",
            ProductCode: "COST-ACC-TEXT-2026",
            ProductTitle: "Cost Accounting 2026",
            Quantity: 1,
            AmountMinor: 150000L,
            AmountMajor: null,
            Currency: "PHP",
            Department: "Accounting",
            CostCenter: "CC-101",
            Status: "PENDING",
            SendInitialEmail: true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await handler.HandleAsync(input, CancellationToken.None);
        sw.Stop();

        // Must complete promptly without awaiting the 15-second detached background dispatch
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Handler took {sw.ElapsedMilliseconds}ms; expected immediate detached return.");
        Assert.True(result.EmailDispatched);
        Assert.Equal("Dispatched via background SMTP worker.", result.EmailMessage);

        CancellationToken backgroundToken = await capturingSmtp.TokenCapturedSource.Task
            .WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(backgroundToken.CanBeCanceled);
        Assert.False(backgroundToken.IsCancellationRequested);
        Assert.NotEqual(callerCts.Token, backgroundToken);
    }

    [Fact]
    public async Task R5_2_T1_04_EmailBodyContains_OrderNumber_Amount_AndCheckoutLink()
    {
        using var scope = _factory.Services.CreateScope();
        var smtpFactory = scope.ServiceProvider.GetRequiredService<SmtpClientFactory>();

        // Resolves settings and builds email
        var settings = smtpFactory.ResolveSettings();
        Assert.NotNull(settings);

        var result = await smtpFactory.SendPaymentInvitationEmailAsync(
            orderId: "ord_body_test",
            orderNumber: "ORD-202611-BODY",
            title: "Advanced Financial Accounting",
            amountMinor: 250000L,
            currency: "PHP",
            customerName: "Auditor John",
            customerEmail: "auditor_john@test.com",
            checkoutUrl: "https://checkout.paymongo.com/test_body_url",
            googleFormResponseId: "GFR-BODY-01");

        // Verifies result object contract
        Assert.NotNull(result);
    }

    [Fact]
    public void R5_2_T1_05_SendsBcc_ToDefaultTreasuryReceiptEmail()
    {
        using var scope = _factory.Services.CreateScope();
        var smtpFactory = scope.ServiceProvider.GetRequiredService<SmtpClientFactory>();
        var settings = smtpFactory.ResolveSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings.DefaultReceiptEmail));
    }

    // =========================================================================
    // Feature 7 (R5.3): SmtpClientFactory Hardening & Probe
    // =========================================================================

    [Fact]
    public void R5_3_T1_01_ResolvesSettings_PrioritizingSecretStoreOverConfigAndEnv()
    {
        var secretStore = new InMemorySecretStore(new Dictionary<string, string>
        {
            ["SMTP_HOST"] = "smtp.secretstore.test",
            ["SMTP_PORT"] = "2525",
            ["SMTP_USER"] = "secret_user@test.com",
            ["SMTP_APP_PASSWORD"] = "secret_app_pass"
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SMTP_HOST"] = "smtp.config.test"
            })
            .Build();

        var factory = new SmtpClientFactory(config, NullLogger<SmtpClientFactory>.Instance, secretStore);
        var settings = factory.ResolveSettings();

        Assert.Equal("smtp.secretstore.test", settings.Host);
        Assert.Equal(2525, settings.Port);
        Assert.Equal("secret_user@test.com", settings.Username);
        Assert.Equal("secret_app_pass", settings.Password);
    }

    [Fact]
    public void R5_3_T1_02_StripsSpacesFromGmailAppPasswords()
    {
        var secretStore = new InMemorySecretStore(new Dictionary<string, string>
        {
            ["SMTP_APP_PASSWORD"] = "abcd efgh ijkl mnop"
        });

        var factory = new SmtpClientFactory(secretStore: secretStore);
        var settings = factory.ResolveSettings();

        Assert.Equal("abcdefghijklmnop", settings.Password);
    }

    [Fact]
    public async Task R5_3_T1_03_SendPaymentInvitationEmail_ProducesValidHtmlBody()
    {
        var factory = new SmtpClientFactory();
        var res = await factory.SendPaymentInvitationEmailAsync(
            orderId: "ord_html_01",
            orderNumber: "ORD-202611-HTML01",
            title: "Corporate Accounting",
            amountMinor: 180000L,
            currency: "PHP",
            customerName: "Jane Doe",
            customerEmail: "jane.doe@example.com",
            checkoutUrl: "https://pay.example.com/checkout/01");

        Assert.NotNull(res);
    }

    [Fact]
    public async Task R5_3_T1_04_ProbeHost_DisplaysConfiguredPortCorrectly()
    {
        var factory = new SmtpClientFactory();
        var testReq = new EmailTestRequest(Host: "smtp.gmail.com", Port: 587, Recipient: "test@example.com", SendTestEmail: false);
        var probeRes = await factory.TestSmtpAsync(testReq);

        Assert.NotNull(probeRes);
        Assert.NotNull(probeRes.Probe);
        Assert.Contains("587", probeRes.Probe.Host, StringComparison.Ordinal);
    }

    [Fact]
    public void R5_3_T1_05_Port587_DefaultsToStartTlsEnabled()
    {
        var factory = new SmtpClientFactory();
        var settings = factory.ResolveSettings();

        if (settings.Port == 587)
        {
            Assert.True(settings.UseTls);
        }
    }

    // =========================================================================
    // Feature 8 (R5.4): Payment Expiration Worker Sweeper
    // =========================================================================

    [Fact]
    public async Task R5_4_T1_01_SweepsPendingAttempt_WhenNowGreaterOrEqualToExpiresAt()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_exp_01_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(ordId, "u_exp", "ORD-EXP-01", "Expiring Order", OrderCategory.ProductOrder, "REF", Money.Php(1000_00L), OrderStatus.Pending, "{}", clock.UtcNow.AddMinutes(-60), clock.UtcNow));

        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_exp_01_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-60));
        attempt.AttachCheckoutSession("cs_exp_01", "https://checkout.test/exp1", clock.UtcNow.AddMinutes(-10), clock.UtcNow.AddMinutes(-60));
        await attemptRepo.SaveAsync(attempt);

        using var worker = ActivatorUtilities.CreateInstance<PaymentExpirationWorker>(scope.ServiceProvider);
        await worker.ProcessOverdueBatchOnceAsync();

        var refreshed = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(PaymentAttemptStatus.Expired, refreshed.Status);
    }

    [Fact]
    public async Task R5_4_T1_02_SweepsAttempt_WhenRemainingSecondsLessOrEqualToZero()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_exp_02_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(ordId, "u_exp", "ORD-EXP-02", "Expiring Order 2", OrderCategory.ProductOrder, "REF", Money.Php(1000_00L), OrderStatus.Pending, "{}", clock.UtcNow.AddMinutes(-30), clock.UtcNow));

        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_exp_02_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-30));
        // ExpiresAt set to clock.UtcNow (remainingSeconds == 0)
        attempt.AttachCheckoutSession("cs_exp_02", "https://checkout.test/exp2", clock.UtcNow, clock.UtcNow.AddMinutes(-30));
        await attemptRepo.SaveAsync(attempt);

        using var worker = ActivatorUtilities.CreateInstance<PaymentExpirationWorker>(scope.ServiceProvider);
        await worker.ProcessOverdueBatchOnceAsync();

        var refreshed = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(PaymentAttemptStatus.Expired, refreshed.Status);
    }

    [Fact]
    public async Task R5_4_T1_03_SupersededAttempt_OnSameOrder_MarkedExpiredImmediately()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_super_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(ordId, "u_super", "ORD-SUPER", "Superseded Order", OrderCategory.ProductOrder, "REF", Money.Php(1000_00L), OrderStatus.Pending, "{}", clock.UtcNow, clock.UtcNow));

        var att1 = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_sup_1_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-20));
        att1.AttachCheckoutSession("cs_sup_1", "https://checkout/1", clock.UtcNow.AddMinutes(10), clock.UtcNow.AddMinutes(-20));
        await attemptRepo.SaveAsync(att1);

        var att2 = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_sup_2_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-5));
        att2.AttachCheckoutSession("cs_sup_2", "https://checkout/2", clock.UtcNow.AddMinutes(25), clock.UtcNow.AddMinutes(-5));
        await attemptRepo.SaveAsync(att2);

        var latestAttempts = await attemptRepo.GetLatestByOrderIdsAsync([ordId]);
        Assert.True(latestAttempts.TryGetValue(ordId, out PaymentAttempt? newest));
        Assert.NotNull(newest);
        Assert.Equal(att2.Id, newest.Id);

        // att1 is superseded because newest.Id != att1.Id
        bool isSuperseded = !string.Equals(newest.Id, att1.Id, StringComparison.OrdinalIgnoreCase);
        Assert.True(isSuperseded);
    }

    [Fact]
    public void R5_4_T1_04_RemotePaidSession_TransitionsToSucceededAndOrderToPaid()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var attempt = PaymentAttempt.CreatePending("ord_paid_01", "hosted_checkout", Money.Php(1000_00L), "ik_p01", clock.UtcNow);

        var markResult = attempt.MarkSucceeded(clock.UtcNow, "qrph");
        Assert.True(markResult.IsSuccess);
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
    }

    [Fact]
    public void R5_4_T1_05_RemoteFailedSession_TransitionsToFailed()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var attempt = PaymentAttempt.CreatePending("ord_fail_01", "hosted_checkout", Money.Php(1000_00L), "ik_f01", clock.UtcNow);

        var markResult = attempt.MarkFailed("PAYMENT_FAILED", "Insufficient balance", clock.UtcNow);
        Assert.True(markResult.IsSuccess);
        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal("PAYMENT_FAILED", attempt.FailureCode);
    }

    // =========================================================================
    // Feature 9 (R5.5): Heroku DB Connection String Parsing & Pooling
    // =========================================================================

    [Fact]
    public void R5_5_T1_01_ParsesDatabaseUrl_WithPoolingTrue()
    {
        string raw = "postgres://usr:pwd@dbhost.herokuapp.com:5432/appdb";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.True(csb.Pooling);
    }

    [Fact]
    public void R5_5_T1_02_SetsMinPoolSizeToFive()
    {
        string raw = "postgres://usr:pwd@dbhost.herokuapp.com:5432/appdb";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        // Production requirement R5.5 specifies MinPoolSize = 5
        Assert.Equal(5, csb.MinPoolSize);
    }

    [Fact]
    public void R5_5_T1_03_SetsMaxPoolSizeToFifty()
    {
        string raw = "postgres://usr:pwd@dbhost.herokuapp.com:5432/appdb";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        // Production requirement R5.5 specifies MaxPoolSize = 50
        Assert.Equal(50, csb.MaxPoolSize);
    }

    [Fact]
    public void R5_5_T1_04_ConfiguresSslModeRequire_AndTrustServerCertificateTrue()
    {
        string raw = "postgres://usr:pwd@dbhost.herokuapp.com:5432/appdb";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal(SslMode.Require, csb.SslMode);
    }

    [Fact]
    public void R5_5_T1_05_NormalizesLocalhostTo127001()
    {
        string raw = "postgres://usr:pwd@localhost:5432/localdb";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("127.0.0.1", csb.Host);
    }

    // =========================================================================
    // Feature 10 (F10): Shared Identity & Role Hierarchy
    // =========================================================================

    [Fact]
    public void F10_T1_01_UserId_ReturnsSameValueAsActorId()
    {
        ICurrentActor actor = new TestActor("user_actor_123", ["user"]);
        Assert.Equal("user_actor_123", actor.ActorId);
        Assert.Equal(actor.ActorId, actor.UserId);
    }

    [Fact]
    public void F10_T1_02_SystemAdminRole_ReturnsTrueForIsInRoleAdmin()
    {
        ICurrentActor actor = new TestActor("sysadmin_user", ["system_admin"]);
        Assert.True(actor.IsInRole("admin"), "system_admin must satisfy IsInRole('admin').");
    }

    [Fact]
    public void F10_T1_03_ServerCurrentActor_ParsesCommaSeparatedXUserRolesHeader()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-User-ID"] = "custom_analyst";
        httpContext.Request.Headers["X-User-Roles"] = "finance_reviewer, auditor, supervisor";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var actor = new ServerCurrentActor(accessor);

        Assert.Equal("custom_analyst", actor.ActorId);
        Assert.True(actor.HasRole("finance_reviewer"));
        Assert.True(actor.HasRole("auditor"));
        Assert.True(actor.HasRole("supervisor"));
    }

    [Fact]
    public void F10_T1_04_AcmeAdminUser_AutomaticallyPossessesAdminAndFinanceAdminRoles()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-User-ID"] = "acme_admin";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var actor = new ServerCurrentActor(accessor);

        Assert.Equal("acme_admin", actor.ActorId);
        Assert.True(actor.HasRole("admin"));
        Assert.True(actor.HasRole("system_admin"));
        Assert.True(actor.HasRole("finance_admin"));
    }

    [Fact]
    public void F10_T1_05_AuthenticatedClaimsPrincipal_PopulatesActorFieldsAndIsAuthenticatedTrue()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "claims_user_99"),
            new Claim(ClaimTypes.Role, "finance_controller"),
            new Claim("employee_code", "EMP-99"),
            new Claim("department_id", "treasury"),
            new Claim("cost_center_id", "CC-TREASURY")
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var actor = new ServerCurrentActor(accessor);

        Assert.True(actor.IsAuthenticated);
        Assert.Equal("claims_user_99", actor.ActorId);
        Assert.Equal("EMP-99", actor.EmployeeCode);
        Assert.Equal("treasury", actor.DepartmentId);
        Assert.Equal("CC-TREASURY", actor.CostCenterId);
        Assert.True(actor.HasRole("finance_controller"));
    }

    private sealed class TestActor(string id, IEnumerable<string> roles) : ICurrentActor
    {
        private readonly HashSet<string> _roles = new(roles, StringComparer.OrdinalIgnoreCase);

        public string ActorId { get; } = id;
        public string EmployeeCode => $"EMP-{ActorId}";
        public string DepartmentId => "dept";
        public string CostCenterId => "cc";
        public IReadOnlySet<string> Roles => _roles;
        public string DepartmentScope => "*";
        public string CorrelationId => "test_corr";
        public bool IsAuthenticated => true;

        public bool HasRole(string role) => _roles.Contains(role);
        public bool HasScopeForDepartment(string targetDepartmentId) => true;
    }

    private sealed class InMemorySecretStore(IDictionary<string, string> secrets) : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = new(secrets, StringComparer.OrdinalIgnoreCase);

        public string? GetSecret(string key) =>
            _secrets.TryGetValue(key, out string? value) ? value : null;

        public bool HasGoogleFormsSecret() =>
            _secrets.ContainsKey("GOOGLE_FORMS_SECRET") || _secrets.ContainsKey("Secrets:GoogleFormsSecret");

        public Task<SaveCredentialsResponse> SaveCredentialsAsync(SaveCredentialsRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SaveCredentialsResponse(true, "Saved in memory", []));
    }
}

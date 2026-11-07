using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Orders.ManageOrder;
using Rge.Payments.Application.PaymentAttempts.ExpireAttempt;
using Rge.Payments.Application.Reconciliation.ReconcileAllNonTerminal;
using Rge.Payments.Application.Reconciliation.ReconcileOne;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Comprehensive integration tests verifying the full Order CRUD lifecycle, baseline seed orders fallback,
/// server-authoritative pricing, order cancellation, and payment reconciliation/expiration operations
/// using an in-memory ASP.NET Core test server without external database dependencies.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class OrderCrudLifecycleIntegrationTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrderCrudLifecycleIntegrationTests(AccountingWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-User-ID", "acme_admin");
    }

    public void Dispose()
    {
        _client.Dispose();
        OrderRepository.ResetToBaseline();
    }

    // =========================================================================
    // 1. Baseline Seed Orders Fallback (GET /api/v1/orders)
    // =========================================================================

    [Fact]
    public async Task GetOrders_ReturnsBaselineSeedOrders_WhenRepositoryUnpopulated()
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/v1/orders");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orders = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(orders);
        Assert.True(orders.Count >= 4, $"Expected at least 4 baseline seed orders, but found {orders.Count}.");

        // Verify known baseline seed IDs
        var ids = orders.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ord_seed_001_cpa2026", ids);
        Assert.Contains("ord_seed_002_costacc", ids);
        Assert.Contains("ord_seed_003_mfgdept", ids);
        Assert.Contains("ord_seed_004_canceldoc", ids);

        // Verify diversity of categories and statuses
        Assert.Contains(orders, o => o.Category == OrderCategory.ProductOrder && o.Status == OrderStatus.Pending);
        Assert.Contains(orders, o => o.Category == OrderCategory.ProductOrder && o.Status == OrderStatus.Paid);
        Assert.Contains(orders, o => o.Category == OrderCategory.DepartmentInvoice && o.Status == OrderStatus.Pending);
        Assert.Contains(orders, o => o.Category == OrderCategory.DepartmentInvoice && o.Status == OrderStatus.Cancelled);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("paid")]
    [InlineData("cancelled")]
    public async Task GetOrders_FilteredByStatus_ReturnsOnlyMatchingOrders(string filterStatus)
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync($"/api/v1/orders?status={filterStatus}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orders = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(orders);
        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.Equal(filterStatus, o.Status, ignoreCase: true));
    }

    // =========================================================================
    // 2. Order Creation & Server-Authoritative Pricing (POST /api/v1/orders)
    // =========================================================================

    [Fact]
    public async Task PostOrder_CreatesOrderWithServerAuthoritativePricing()
    {
        // Arrange: Client attempts to supply a low-ball amount 50 centavos, but COST-ACC-TEXT-2026
        // has an authoritative price of PHP 1,850.00 (185,000 centavos) per unit * qty 2 = 370,000 centavos.
        var request = new CreateOrderRequest(
            Title: "Cost Accounting Textbook 2026 - Student Copy",
            Category: OrderCategory.ProductOrder,
            ReferenceCode: "COST-ACC-TEXT-2026",
            AmountMinor: 50,
            Quantity: 2,
            CustomerName: "Test Student",
            CustomerEmail: "student@example.ph",
            CustomerPhone: "+639171112233",
            Metadata: new Dictionary<string, object?> { ["batch"] = "2026-Q1" });

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/v1/orders", request, JsonDefaults.Options);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.StartsWith("ORD-", created.OrderNumber, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Cost Accounting Textbook 2026 - Student Copy", created.Title);
        Assert.Equal(OrderCategory.ProductOrder, created.Category);
        Assert.Equal("COST-ACC-TEXT-2026", created.ReferenceCode);
        Assert.Equal(370000, created.AmountMinor); // Server-authoritative: 1850 * 100 * 2
        Assert.Equal("PHP", created.Currency);
        Assert.Equal(OrderStatus.Pending, created.Status);
    }

    // =========================================================================
    // 3. Order Retrieval by ID and Order Number (GET /api/v1/orders/{id})
    // =========================================================================

    [Fact]
    public async Task GetOrderById_And_ByOrderNumber_ReturnsMatchingOrder()
    {
        // 1. Fetch seed order by ID
        HttpResponseMessage idResponse = await _client.GetAsync("/api/v1/orders/ord_seed_001_cpa2026");
        Assert.Equal(HttpStatusCode.OK, idResponse.StatusCode);
        var orderById = await idResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(orderById);
        Assert.Equal("ord_seed_001_cpa2026", orderById.Id);
        Assert.Equal("ORD-202611-0001", orderById.OrderNumber);

        // 2. Fetch seed order by Order Number
        HttpResponseMessage numberResponse = await _client.GetAsync("/api/v1/orders/ORD-202611-0001");
        Assert.Equal(HttpStatusCode.OK, numberResponse.StatusCode);
        var orderByNumber = await numberResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(orderByNumber);
        Assert.Equal(orderById.Id, orderByNumber.Id);

        // 3. Fetch non-existent order returns 404 Not Found
        HttpResponseMessage notFoundResponse = await _client.GetAsync("/api/v1/orders/ord_nonexistent_9999");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
    }

    // =========================================================================
    // 4. Order Mutation (PATCH & PUT /api/v1/orders/{id})
    // =========================================================================

    [Fact]
    public async Task PatchAndPutOrder_UpdatesDetailsOnPendingOrder()
    {
        // Arrange: Create a new pending order
        var createRequest = new CreateOrderRequest(
            Title: "Original Order Title",
            Category: OrderCategory.ProductOrder,
            ReferenceCode: "CPA-REV-AUDIT-2026",
            Quantity: 1);

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/api/v1/orders", createRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(created);

        // Act 1: PATCH title and metadata
        using var patchDoc = JsonDocument.Parse("""{"department":"Audit Services","urgency":"high"}""");
        var patchRequest = new UpdateOrderRequest(
            Title: "Patched Order Title",
            Metadata: patchDoc.RootElement);

        HttpResponseMessage patchResponse = await _client.PatchAsJsonAsync($"/api/v1/orders/{created.Id}", patchRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patchResult = await patchResponse.Content.ReadFromJsonAsync<UpdateOrderResponse>(JsonDefaults.Options);
        Assert.NotNull(patchResult);
        Assert.True(patchResult.Success);
        Assert.Equal("Patched Order Title", patchResult.Title);

        // Verify via GET
        HttpResponseMessage verifyGet = await _client.GetAsync($"/api/v1/orders/{created.Id}");
        var verified = await verifyGet.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(verified);
        Assert.Equal("Patched Order Title", verified.Title);

        // Act 2: PUT updated title and amount
        var putRequest = new UpdateOrderRequest(
            Title: "PUT Updated Title",
            AmountMinor: 250000);

        HttpResponseMessage putResponse = await _client.PutAsJsonAsync($"/api/v1/orders/{created.Id}", putRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putResult = await putResponse.Content.ReadFromJsonAsync<UpdateOrderResponse>(JsonDefaults.Options);
        Assert.NotNull(putResult);
        Assert.True(putResult.Success);
        Assert.Equal("PUT Updated Title", putResult.Title);
        Assert.Equal(250000, putResult.AmountMinor);
    }

    // =========================================================================
    // 5. Order Cancellation & Conflict Handling (POST /cancel & DELETE)
    // =========================================================================

    [Fact]
    public async Task CancelOrder_CancelsPendingOrder_AndSubsequentUpdateReturnsConflict()
    {
        // Arrange: Create a pending order
        var createRequest = new CreateOrderRequest(
            Title: "Order To Be Cancelled",
            Category: OrderCategory.DepartmentInvoice,
            ReferenceCode: "manufacturing");

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/api/v1/orders", createRequest, JsonDefaults.Options);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(created);

        // Act 1: Cancel via POST /api/v1/orders/{id}/cancel
        var cancelRequest = new CancelOrderRequest(Reason: "Budget reallocated for Q1");
        HttpResponseMessage cancelResponse = await _client.PostAsJsonAsync($"/api/v1/orders/{created.Id}/cancel", cancelRequest, JsonDefaults.Options);

        // Assert cancellation succeeded
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelResult = await cancelResponse.Content.ReadFromJsonAsync<CancelOrderResponse>(JsonDefaults.Options);
        Assert.NotNull(cancelResult);
        Assert.True(cancelResult.Success);
        Assert.Equal(OrderStatus.Cancelled, cancelResult.Status);

        // Verify status via GET
        HttpResponseMessage getResponse = await _client.GetAsync($"/api/v1/orders/{created.Id}");
        var order = await getResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Cancelled, order.Status);

        // Act 2: Repeated cancellation is idempotent (200 OK)
        HttpResponseMessage repeatCancelResponse = await _client.PostAsJsonAsync($"/api/v1/orders/{created.Id}/cancel", cancelRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.OK, repeatCancelResponse.StatusCode);

        // Act 3: Attempting to update a cancelled order returns 409 Conflict
        var updateRequest = new UpdateOrderRequest(Title: "Illegal update on cancelled order");
        HttpResponseMessage updateConflictResponse = await _client.PatchAsJsonAsync($"/api/v1/orders/{created.Id}", updateRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Conflict, updateConflictResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteOrder_SoftCancelsOrder()
    {
        // Arrange: Create a pending order
        var createRequest = new CreateOrderRequest(
            Title: "Order For Deletion",
            Category: OrderCategory.DepartmentInvoice,
            ReferenceCode: "assembly");

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/api/v1/orders", createRequest, JsonDefaults.Options);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(created);

        // Act: DELETE /api/v1/orders/{id}
        HttpResponseMessage deleteResponse = await _client.DeleteAsync($"/api/v1/orders/{created.Id}?reason=DeletedViaTest");

        // Assert
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var deleteResult = await deleteResponse.Content.ReadFromJsonAsync<CancelOrderResponse>(JsonDefaults.Options);
        Assert.NotNull(deleteResult);
        Assert.True(deleteResult.Success);
        Assert.Equal(OrderStatus.Cancelled, deleteResult.Status);

        // Verify via GET
        HttpResponseMessage getResponse = await _client.GetAsync($"/api/v1/orders/{created.Id}");
        var order = await getResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task UpdateOrCancelPaidOrder_ReturnsConflict()
    {
        // Seed order ord_seed_002_costacc is already paid
        const string paidOrderId = "ord_seed_002_costacc";

        // Act 1: Attempt to PATCH paid order -> 409 Conflict
        var patchRequest = new UpdateOrderRequest(Title: "Cannot rename paid order");
        HttpResponseMessage patchResponse = await _client.PatchAsJsonAsync($"/api/v1/orders/{paidOrderId}", patchRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Conflict, patchResponse.StatusCode);

        // Act 2: Attempt to PUT paid order -> 409 Conflict
        var putRequest = new UpdateOrderRequest(Title: "Cannot replace paid order");
        HttpResponseMessage putResponse = await _client.PutAsJsonAsync($"/api/v1/orders/{paidOrderId}", putRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Conflict, putResponse.StatusCode);

        // Act 3: Attempt to cancel paid order -> 409 Conflict
        var cancelRequest = new CancelOrderRequest(Reason: "Cannot cancel settled payment");
        HttpResponseMessage cancelResponse = await _client.PostAsJsonAsync($"/api/v1/orders/{paidOrderId}/cancel", cancelRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Conflict, cancelResponse.StatusCode);
    }

    // =========================================================================
    // 6. Payment Attempt Expiration & Reconciliation Operations
    // =========================================================================

    [Fact]
    public async Task PaymentAttempt_ExpireLifecycle_Succeeds()
    {
        // Arrange: Create a pending order
        var createRequest = new CreateOrderRequest(
            Title: "Order For Expiry Proof",
            Category: OrderCategory.ProductOrder,
            ReferenceCode: "CPA-REV-AUDIT-2026");

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/api/v1/orders", createRequest, JsonDefaults.Options);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(created);

        // Act 1: Initiate payment attempt
        var attemptRequest = new CreatePaymentAttemptRequest(PaymentMethod: "qrph", CustomerName: "Test Buyer");
        HttpResponseMessage attemptResponse = await _client.PostAsJsonAsync($"/api/v1/orders/{created.Id}/payment-attempts", attemptRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Created, attemptResponse.StatusCode);

        var attempt = await attemptResponse.Content.ReadFromJsonAsync<PaymentAttemptDto>(JsonDefaults.Options);
        Assert.NotNull(attempt);
        Assert.Equal(created.Id, attempt.OrderId);
        Assert.NotNull(attempt.CheckoutUrl);

        // Act 2: Expire attempt via POST /api/v1/payments/{payment_id}/expire
        var expireRequest = new ExpirePaymentRequest(Reason: "User abandoned cart");
        HttpResponseMessage expireResponse = await _client.PostAsJsonAsync($"/api/v1/payments/{attempt.Id}/expire", expireRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.OK, expireResponse.StatusCode);

        var expireResult = await expireResponse.Content.ReadFromJsonAsync<ExpireAttemptResponse>(JsonDefaults.Options);
        Assert.NotNull(expireResult);
        Assert.True(expireResult.Success);
        Assert.Equal("expired", expireResult.Status);

        // Verify via order GET that latest attempt is expired
        HttpResponseMessage orderGet = await _client.GetAsync($"/api/v1/orders/{created.Id}");
        var order = await orderGet.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(order);
        Assert.NotNull(order.LatestAttempt);
        Assert.Equal("expired", order.LatestAttempt.Status);
    }

    [Fact]
    public async Task PaymentAttempt_ReconcileLifecycle_MarksOrderPaid()
    {
        // Arrange: Create a pending order
        var createRequest = new CreateOrderRequest(
            Title: "Order For Reconciliation Proof",
            Category: OrderCategory.ProductOrder,
            ReferenceCode: "COST-ACC-TEXT-2026");

        HttpResponseMessage createResponse = await _client.PostAsJsonAsync("/api/v1/orders", createRequest, JsonDefaults.Options);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(created);

        // Act 1: Initiate payment attempt
        var attemptRequest = new CreatePaymentAttemptRequest(PaymentMethod: "gcash");
        HttpResponseMessage attemptResponse = await _client.PostAsJsonAsync($"/api/v1/orders/{created.Id}/payment-attempts", attemptRequest, JsonDefaults.Options);
        var attempt = await attemptResponse.Content.ReadFromJsonAsync<PaymentAttemptDto>(JsonDefaults.Options);
        Assert.NotNull(attempt);
        Assert.NotNull(attempt.ProviderPaymentId);

        // Simulate gateway settling payment to "paid"
        _factory.Gateway.SetRemoteSessionStatus(
            checkoutSessionId: attempt.ProviderPaymentId,
            status: "paid",
            paymentMethodUsed: "gcash",
            paidAtUtc: DateTimeOffset.UtcNow);

        // Act 2: Trigger reconciliation endpoint
        HttpResponseMessage reconcileResponse = await _client.PostAsync($"/api/v1/admin/payments/{attempt.Id}/reconcile", null);
        Assert.Equal(HttpStatusCode.OK, reconcileResponse.StatusCode);

        var reconcileResult = await reconcileResponse.Content.ReadFromJsonAsync<ReconcileOneResponse>(JsonDefaults.Options);
        Assert.NotNull(reconcileResult);
        Assert.Equal("reconciled", reconcileResult.Status);
        Assert.NotNull(reconcileResult.Attempt);
        Assert.Equal("succeeded", reconcileResult.Attempt.Status);

        // Verify order status transitioned to "paid"
        HttpResponseMessage orderGet = await _client.GetAsync($"/api/v1/orders/{created.Id}");
        var order = await orderGet.Content.ReadFromJsonAsync<OrderDto>(JsonDefaults.Options);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Paid, order.Status);

        // Verify that now-paid order rejects subsequent update attempts with 409 Conflict
        var updateRequest = new UpdateOrderRequest(Title: "Attempted edit on reconciled order");
        HttpResponseMessage conflictResponse = await _client.PatchAsJsonAsync($"/api/v1/orders/{created.Id}", updateRequest, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        // Act 3: Verify batch reconcile endpoint executes cleanly
        HttpResponseMessage reconcileAllResponse = await _client.PostAsync("/api/v1/admin/payments/reconcile-all", null);
        Assert.Equal(HttpStatusCode.OK, reconcileAllResponse.StatusCode);
        var reconcileAllResult = await reconcileAllResponse.Content.ReadFromJsonAsync<ReconcileAllNonTerminalResponse>(JsonDefaults.Options);
        Assert.NotNull(reconcileAllResult);
    }
}

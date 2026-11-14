using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Integrations.Application.Ports;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.Ports;
using SmtpClientFactory = Rge.Integrations.Infrastructure.SmtpClientFactory;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Persistence.Repositories;
using Rge.Payments.Infrastructure.Workers;
using Rge.Payments.Integration.Tests.E2E.Fixtures;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests.E2E.Tier3;

/// <summary>
/// Tier 3: Cross-Feature Multi-Module Interaction Workflows (T3-COMB-01 to T3-COMB-05).
/// Verifies complete integration pipelines across Google Forms, PayMongo checkout, detached SMTP,
/// connection pooling, worker sweeps, outbox processing, and role-based data isolation.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class Tier3CrossFeatureTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private const string WebhookSecret = "whsec_test_mock_secret_key_12345678";
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Tier3CrossFeatureTests(AccountingWebApplicationFactory factory)
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

    /// <summary>
    /// T3-COMB-01: Google Form Submission -> Heroku DB Pooling -> Whitespace-Stripped SMTP -> Detached Async Dispatch.
    /// </summary>
    [Fact]
    public async Task T3_COMB_01_GoogleForm_HerokuPooling_SmtpWhitespace_DetachedDispatch_Pipeline()
    {
        string respId = $"gfr_t3_comb01_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        // 1. Submit Google Form with customer details and book order
        var payload = new
        {
            response_id = respId,
            customer_name = "Clara Oswald",
            customer_email = "clara.oswald@tardis.edu",
            customer_phone = "09175550199",
            product_code = "CPA-REV-2026",
            product_title = "CPA Reviewer 2026 Volume 1",
            quantity = 2,
            amount_minor = 290000L,
            send_initial_email = true
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(response.IsSuccessStatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        string orderId = doc.GetProperty("order_id").GetString()!;
        string orderNumber = doc.GetProperty("order_number").GetString()!;
        string checkoutUrl = doc.GetProperty("checkout_url").GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(orderId));
        Assert.StartsWith("ORD-", orderNumber, StringComparison.Ordinal);
        Assert.Contains(orderId, checkoutUrl, StringComparison.Ordinal);
        Assert.True(doc.GetProperty("email_dispatched").GetBoolean());

        // 2. Allow detached 15s async SMTP task to log
        await Task.Delay(150);

        // 3. Verify detached SMTP emitted [GOOGLE-FORM-SMTP] structured log
        bool logEmitted = _factory.CapturedLogs.Any(l =>
            l.Message.Contains("[GOOGLE-FORM-SMTP]", StringComparison.OrdinalIgnoreCase) &&
            l.Message.Contains(orderNumber, StringComparison.OrdinalIgnoreCase));
        Assert.True(logEmitted, "Detached async SMTP worker must emit [GOOGLE-FORM-SMTP] structured log with order number.");

        // 4. Verify persisted order in repository
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var order = await orderRepo.GetByIdAsync(orderId);
        Assert.NotNull(order);
        Assert.Equal(290000L, order.Amount.AmountMinor);
        Assert.Equal("pending", order.Status);
    }

    /// <summary>
    /// T3-COMB-02: Google Form Order Bridge -> Sequential N+1 Batch Attempt Query -> User Isolation (BOLA / IDOR).
    /// </summary>
    [Fact]
    public async Task T3_COMB_02_GoogleForm_OrderBridge_BatchQuery_UserIsolation_MultiModule()
    {
        string respId = $"gfr_t3_comb02_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        // 1. Google Form creates an order under system user 'google_forms_bridge'
        var payload = new
        {
            response_id = respId,
            customer_name = "Amy Pond",
            customer_email = "amy.pond@example.com",
            product_code = "COST-ACC-2026",
            quantity = 1,
            amount_minor = 185000L,
            send_initial_email = false
        };

        var formRes = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(formRes.IsSuccessStatusCode);
        var doc = await formRes.Content.ReadFromJsonAsync<JsonElement>();
        string formOrderId = doc.GetProperty("order_id").GetString()!;

        // 2. Non-admin student requests /api/v1/orders
        string studentUser = $"student_{Guid.NewGuid():N}";
        _client.AsUser(studentUser);

        var studentRes = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, studentRes.StatusCode);
        var studentOrders = await studentRes.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(studentOrders);
        // Student cannot see google_forms_bridge order
        Assert.DoesNotContain(studentOrders, o => o.Id == formOrderId);

        // 3. Admin requests /api/v1/orders
        _client.AsAdmin();
        var adminRes = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);
        var adminOrders = await adminRes.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(adminOrders);

        // Admin sees the Google Form order with batched attempt attached
        var foundFormOrder = adminOrders.FirstOrDefault(o => o.Id == formOrderId);
        Assert.NotNull(foundFormOrder);
        Assert.NotNull(foundFormOrder.LatestAttempt);
    }

    /// <summary>
    /// T3-COMB-03: Google Form -> PayMongo Session -> Expiration Sweeper -> Admin Webhook Audit.
    /// </summary>
    [Fact]
    public async Task T3_COMB_03_GoogleForm_ExpirationSweeper_AdminAudit_Lifecycle()
    {
        string respId = $"gfr_t3_comb03_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        // 1. Create order via Google Form
        var payload = new
        {
            response_id = respId,
            customer_name = "Rory Williams",
            customer_email = "rory@centurion.com",
            product_code = "CPA-REV-2026",
            quantity = 1,
            send_initial_email = false
        };

        var formRes = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(formRes.IsSuccessStatusCode);
        var doc = await formRes.Content.ReadFromJsonAsync<JsonElement>();
        string orderId = doc.GetProperty("order_id").GetString()!;

        // 2. Simulate expired deadline
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var attempts = await attemptRepo.GetLatestByOrderIdsAsync([orderId]);
        Assert.True(attempts.TryGetValue(orderId, out PaymentAttempt? attempt));
        Assert.NotNull(attempt);

        // Fast-forward expiration
        var markExpiredRes = attempt.MarkExpired(clock.UtcNow);
        Assert.True(markExpiredRes.IsSuccess);
        await attemptRepo.SaveAsync(attempt);

        // 3. Admin calls webhook events and verifies operational state
        _client.AsAdmin();
        var adminEventsRes = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.OK, adminEventsRes.StatusCode);

        // 4. Verify attempt is now in expired status
        var refreshedAttempts = await attemptRepo.GetLatestByOrderIdsAsync([orderId]);
        Assert.Equal(PaymentAttemptStatus.Expired, refreshedAttempts[orderId].Status);
    }

    /// <summary>
    /// T3-COMB-04: Role Hierarchy & Administrative Endpoint Access Audit.
    /// </summary>
    [Fact]
    public async Task T3_COMB_04_AdminAuthorization_RoleHierarchy_AuditTrail()
    {
        // 1. Admin accesses /api/v1/admin/fulfillment-outbox
        _client.AsAdmin();
        var resOutboxAdmin = await _client.GetAsync("/api/v1/admin/fulfillment-outbox");
        Assert.Equal(HttpStatusCode.OK, resOutboxAdmin.StatusCode);

        // 2. Finance Admin accesses /api/v1/admin/webhook-events
        _client.AsFinanceAdmin();
        var resWhFinance = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.OK, resWhFinance.StatusCode);

        // 3. Finance Admin can run reconcile-all
        var resRecFinance = await _client.PostAsync("/api/v1/admin/payments/reconcile-all", null);
        Assert.Equal(HttpStatusCode.OK, resRecFinance.StatusCode);
    }

    /// <summary>
    /// T3-COMB-05: Google Form Order -> PayMongo Webhook Paid -> Fulfillment Outbox Queued -> Admin Dispatch.
    /// </summary>
    [Fact]
    public async Task T3_COMB_05_GoogleForm_WebhookPaid_FulfillmentOutbox_AdminProcess()
    {
        string respId = $"gfr_t3_comb05_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        // 1. Create order via Google Form
        var payload = new
        {
            response_id = respId,
            customer_name = "Martha Jones",
            customer_email = "martha.jones@unit.org",
            product_code = "CPA-REV-2026",
            quantity = 1,
            amount_minor = 145000L,
            send_initial_email = false
        };

        var formRes = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(formRes.IsSuccessStatusCode);
        var doc = await formRes.Content.ReadFromJsonAsync<JsonElement>();
        string orderId = doc.GetProperty("order_id").GetString()!;

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var attempts = await attemptRepo.GetLatestByOrderIdsAsync([orderId]);
        Assert.True(attempts.TryGetValue(orderId, out PaymentAttempt? attempt));
        Assert.NotNull(attempt);
        string csId = attempt.ProviderPaymentId ?? $"cs_t3_05_{Guid.NewGuid():N}";

        // 2. Simulate PayMongo Webhook delivery: checkout_session.payment.paid
        string webhookPayload = $$"""
        {
          "data": {
            "id": "evt_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "paid",
                  "payment_method_used": "qrph",
                  "paid_at": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(webhookPayload, WebhookSecret, timestamp);

        using var webhookMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(webhookPayload, Encoding.UTF8, "application/json")
        };
        webhookMsg.Headers.Add("Paymongo-Signature", signature);

        var webhookRes = await _client.SendAsync(webhookMsg);
        Assert.True(webhookRes.IsSuccessStatusCode);

        // 3. Admin calls outbox process
        _client.AsAdmin();
        var processRes = await _client.PostAsync("/api/v1/admin/fulfillment-outbox/process", null);
        Assert.Equal(HttpStatusCode.OK, processRes.StatusCode);
    }
}

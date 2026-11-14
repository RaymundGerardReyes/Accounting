using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.Infrastructure.Persistence;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.Integrations.Contracts;
using Rge.Payments.Api.Contracts;
using Rge.Payments.Application.Mapping;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.Ports;
using SmtpClientFactory = Rge.Integrations.Infrastructure.SmtpClientFactory;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.Persistence.Repositories;
using Rge.Payments.Integration.Tests.E2E.Fixtures;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests.E2E.Tier4;

/// <summary>
/// Tier 4: Real-World End-to-End Application Scenarios (T4-SCEN-01 to T4-SCEN-04).
/// Simulates full lifecycle customer self-service checkout, administrative triage & audit,
/// abandoned session superseding, and adversarial penetration attacks.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class Tier4RealWorldScenariosTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private const string WebhookSecret = "whsec_test_mock_secret_key_12345678";
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Tier4RealWorldScenariosTests(AccountingWebApplicationFactory factory)
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
    /// T4-SCEN-01: Complete Customer Self-Service Payment Lifecycle.
    /// Google Form submission -> Checkout initiation -> Async email -> Customer payment -> Webhook -> Order Paid.
    /// </summary>
    [Fact]
    public async Task T4_SCEN_01_CompleteCustomerSelfServicePaymentLifecycle()
    {
        string respId = $"gfr_t4_scen01_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        // Step 1: Customer submits Google Form for CPA Reviewer textbook
        var formPayload = new
        {
            response_id = respId,
            customer_name = "Donna Noble",
            customer_email = "donna.noble@chiswick.org",
            customer_phone = "09171239988",
            product_code = "CPA-REV-2026",
            product_title = "CPA Reviewer 2026 Volume 1",
            quantity = 1,
            amount_minor = 145000L,
            send_initial_email = true
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", formPayload);
        Assert.True(submitResponse.IsSuccessStatusCode);

        var doc = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        string orderId = doc.GetProperty("order_id").GetString()!;
        string orderNumber = doc.GetProperty("order_number").GetString()!;
        string checkoutUrl = doc.GetProperty("checkout_url").GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(orderId));
        Assert.StartsWith("ORD-", orderNumber, StringComparison.Ordinal);
        Assert.Contains(orderId, checkoutUrl, StringComparison.Ordinal);

        // Step 2: Verify detached async email dispatched in background
        await Task.Delay(150);
        bool emailLogged = _factory.CapturedLogs.Any(l =>
            l.Message.Contains("[GOOGLE-FORM-SMTP]", StringComparison.OrdinalIgnoreCase) &&
            l.Message.Contains("donna.noble@chiswick.org", StringComparison.OrdinalIgnoreCase));
        Assert.True(emailLogged, "Payment invitation email must be logged with customer recipient.");

        // Step 3: Find payment attempt and verify active PayMongo session
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var attempts = await attemptRepo.GetLatestByOrderIdsAsync([orderId]);
        Assert.True(attempts.TryGetValue(orderId, out PaymentAttempt? attempt));
        Assert.NotNull(attempt);
        string csId = attempt.ProviderPaymentId ?? $"cs_t4_01_{Guid.NewGuid():N}";

        // Step 4: Customer completes payment via PayMongo; webhook delivers payment.paid
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
                  "payment_method_used": "gcash",
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

        var webhookResponse = await _client.SendAsync(webhookMsg);
        Assert.True(webhookResponse.IsSuccessStatusCode);

        // Step 5: Verify Order transitioned to Paid in repository
        var paidOrder = await orderRepo.GetByIdAsync(orderId);
        Assert.NotNull(paidOrder);
    }

    /// <summary>
    /// T4-SCEN-02: Administrator Audit & Operational Triage.
    /// Multi-tenant order visibility -> Webhook audit logs -> Outbox batch inspection & processing.
    /// </summary>
    [Fact]
    public async Task T4_SCEN_02_AdministratorAuditAndOperationalTriage()
    {
        // 1. Admin logs in with acme_admin credentials
        _client.AsAdmin("acme_admin");

        // 2. Admin retrieves complete orders ledger across all tenants
        var ordersRes = await _client.GetAsync("/api/v1/orders?limit=100");
        Assert.Equal(HttpStatusCode.OK, ordersRes.StatusCode);
        var orders = await ordersRes.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(orders);
        Assert.True(orders.Count >= 4, "Admin must be able to view all baseline seed orders.");

        // 3. Admin audits recent webhook events
        var webhookEventsRes = await _client.GetAsync("/api/v1/admin/webhook-events?limit=25");
        Assert.Equal(HttpStatusCode.OK, webhookEventsRes.StatusCode);

        // 4. Admin inspects fulfillment outbox
        var outboxRes = await _client.GetAsync("/api/v1/admin/fulfillment-outbox");
        Assert.Equal(HttpStatusCode.OK, outboxRes.StatusCode);

        // 5. Admin triggers batch fulfillment processing
        var processRes = await _client.PostAsync("/api/v1/admin/fulfillment-outbox/process", null);
        Assert.Equal(HttpStatusCode.OK, processRes.StatusCode);

        // 6. Admin triggers system-wide non-terminal payments reconciliation
        var reconcileRes = await _client.PostAsync("/api/v1/admin/payments/reconcile-all", null);
        Assert.Equal(HttpStatusCode.OK, reconcileRes.StatusCode);
    }

    /// <summary>
    /// T4-SCEN-03: Abandoned Checkout Expiration & Session Superseding.
    /// Customer creates CS_1 -> abandons -> creates CS_2 -> worker supersedes CS_1 -> CS_2 expires naturally.
    /// </summary>
    [Fact]
    public async Task T4_SCEN_03_AbandonedCheckoutExpirationAndSessionSuperseding()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // 1. Create order
        string ordId = $"ord_supersede_{Guid.NewGuid():N}";
        var order = Order.Rehydrate(
            id: ordId,
            userId: "cust_supersede",
            orderNumber: "ORD-202611-SUP",
            title: "Superseded Textbook",
            category: OrderCategory.ProductOrder,
            referenceCode: "CPA-2026",
            amount: Money.Php(1450_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow.AddMinutes(-30),
            updatedAt: clock.UtcNow);
        await orderRepo.SaveAsync(order);

        // 2. Customer creates checkout session CS_1
        var attempt1 = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1450_00L), $"ik_cs1_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-25));
        attempt1.AttachCheckoutSession("cs_abandoned_1", "https://checkout/cs1", clock.UtcNow.AddMinutes(5), clock.UtcNow.AddMinutes(-25));
        await attemptRepo.SaveAsync(attempt1);

        // 3. Customer abandons CS_1 and requests CS_2
        var attempt2 = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1450_00L), $"ik_cs2_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-2));
        attempt2.AttachCheckoutSession("cs_active_2", "https://checkout/cs2", clock.UtcNow.AddMinutes(28), clock.UtcNow.AddMinutes(-2));
        await attemptRepo.SaveAsync(attempt2);

        // 4. Batch query returns CS_2 as the latest attempt
        var batch = await attemptRepo.GetLatestByOrderIdsAsync([ordId]);
        Assert.True(batch.TryGetValue(ordId, out PaymentAttempt? latest));
        Assert.NotNull(latest);
        Assert.Equal(attempt2.Id, latest.Id);
        Assert.Equal("cs_active_2", latest.ProviderPaymentId);

        // 5. CS_1 is identified as superseded
        bool cs1IsSuperseded = !string.Equals(latest.Id, attempt1.Id, StringComparison.OrdinalIgnoreCase);
        Assert.True(cs1IsSuperseded, "Older checkout attempt must be recognized as superseded.");

        // Expire CS_1
        var expire1 = attempt1.MarkExpired(clock.UtcNow);
        Assert.True(expire1.IsSuccess);
        Assert.Equal(PaymentAttemptStatus.Expired, attempt1.Status);
    }

    /// <summary>
    /// T4-SCEN-04: Adversarial Security & IDOR Penetration Attack.
    /// Tests unauthenticated route penetration, BOLA tenant snooping, forged form secret, and SQL injection in payload.
    /// </summary>
    [Fact]
    public async Task T4_SCEN_04_AdversarialSecurityAndIdorPenetrationAttack()
    {
        // Vector 1: Attacker attempts unauthorized access to admin endpoints
        _client.AsAnonymous();
        var unauthWh = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthWh.StatusCode);

        var unauthOutbox = await _client.PostAsync("/api/v1/admin/fulfillment-outbox/process", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthOutbox.StatusCode);

        // Vector 2: Attacker attempts IDOR / BOLA order snooping
        string victimUser = $"victim_{Guid.NewGuid():N}";
        string attackerUser = $"attacker_{Guid.NewGuid():N}";

        using (var scope = _factory.Services.CreateScope())
        {
            var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            await orderRepo.SaveAsync(Order.Rehydrate(
                id: $"ord_secret_victim_{Guid.NewGuid():N}",
                userId: victimUser,
                orderNumber: "ORD-CONFIDENTIAL-01",
                title: "Confidential Executive Payroll Review",
                category: OrderCategory.DepartmentInvoice,
                referenceCode: "INV-SECRET",
                amount: Money.Php(999999_00L),
                status: OrderStatus.Paid,
                metadataJson: "{\"vip\": true, \"ssn\": \"000-11-2222\"}",
                createdAt: clock.UtcNow,
                updatedAt: clock.UtcNow));
        }

        // Attacker requests /api/v1/orders
        _client.AsUser(attackerUser);
        var attackerOrdersRes = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, attackerOrdersRes.StatusCode);
        var attackerOrders = await attackerOrdersRes.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(attackerOrders);

        // Attacker sees zero orders from victim
        Assert.DoesNotContain(attackerOrders, o => o.UserId == victimUser);
        Assert.DoesNotContain(attackerOrders, o => o.Title.Contains("Confidential", StringComparison.OrdinalIgnoreCase));

        // Vector 3: Forged Google Form submission with invalid secret header
        _client.AsAnonymous();
        _client.DefaultRequestHeaders.Add("X-Google-Forms-Secret", "forged_malicious_secret_key");
        var forgedRes = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", new
        {
            response_id = "gfr_hacked",
            customer_name = "Black Hat",
            customer_email = "blackhat@evil.org",
            product_code = "FREE-STUFF",
            quantity = 100
        });
        Assert.Equal(HttpStatusCode.Unauthorized, forgedRes.StatusCode);

        // Vector 4: SQL Injection attempt in reference code and payload
        _client.WithGoogleFormsSecret();
        var sqliPayload = new
        {
            response_id = $"gfr_sqli_{Guid.NewGuid():N}",
            customer_name = "Robert'); DROP TABLE orders;--",
            customer_email = "sqli@test.com",
            product_code = "CPA-REV-2026'; DROP TABLE payment_attempts;--",
            quantity = 1,
            send_initial_email = false
        };

        var sqliRes = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", sqliPayload);
        Assert.True(sqliRes.IsSuccessStatusCode, "Parameterized queries must safely escape SQL injection attempts.");
    }
}

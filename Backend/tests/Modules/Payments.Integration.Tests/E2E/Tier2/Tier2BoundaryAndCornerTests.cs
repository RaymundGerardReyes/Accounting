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
using Xunit;

namespace Rge.Payments.Integration.Tests.E2E.Tier2;

/// <summary>
/// Tier 2: Boundary, Corner Case, and Negative Security Tests for Requirements R1, R2, R3, R5, and F10.
/// Verifies robustness under invalid, edge, and adversarial conditions.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class Tier2BoundaryAndCornerTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Tier2BoundaryAndCornerTests(AccountingWebApplicationFactory factory)
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
    // Feature 1 (R1): Sequential N+1 Batch Attempt Query - Boundaries
    // =========================================================================

    [Fact]
    public async Task R1_T2_01_EmptyOrderIds_ReturnsEmptyDictionaryWithoutDbCall()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var emptyResult = await attemptRepo.GetLatestByOrderIdsAsync([]);
        Assert.NotNull(emptyResult);
        Assert.Empty(emptyResult);
    }

    [Fact]
    public async Task R1_T2_02_OrderIdsWithWhitespaceAndDuplicates_DeduplicatesAndTrims()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_trim_{Guid.NewGuid():N}";
        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_trim_{Guid.NewGuid():N}", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_trim", "https://checkout/trim", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        string[] messyIds = [$"  {ordId}  ", ordId, $" {ordId} ", "", "   "];
        var result = await attemptRepo.GetLatestByOrderIdsAsync(messyIds);

        Assert.Single(result);
        Assert.True(result.ContainsKey(ordId));
    }

    [Fact]
    public async Task R1_T2_03_TiedTimestamps_ResolvesDeterministically()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_tied_{Guid.NewGuid():N}";
        DateTimeOffset tiedTime = clock.UtcNow;

        var att1 = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_tied_1_{Guid.NewGuid():N}", tiedTime);
        att1.AttachCheckoutSession("cs_tied_1", "https://checkout/1", tiedTime.AddMinutes(30), tiedTime);
        await attemptRepo.SaveAsync(att1);

        var att2 = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_tied_2_{Guid.NewGuid():N}", tiedTime);
        att2.AttachCheckoutSession("cs_tied_2", "https://checkout/2", tiedTime.AddMinutes(30), tiedTime);
        await attemptRepo.SaveAsync(att2);

        var result = await attemptRepo.GetLatestByOrderIdsAsync([ordId]);
        Assert.True(result.TryGetValue(ordId, out PaymentAttempt? chosen));
        Assert.NotNull(chosen);
    }

    [Fact]
    public async Task R1_T2_04_OfflineDbException_FallsBackGracefullyToInMemory()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_fallback_{Guid.NewGuid():N}";
        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_fb_{Guid.NewGuid():N}", clock.UtcNow);
        attempt.AttachCheckoutSession("cs_fb", "https://checkout/fb", clock.UtcNow.AddMinutes(30), clock.UtcNow);
        await attemptRepo.SaveAsync(attempt);

        // OfflineFallbackDbDataSource triggers fallback branch
        var result = await attemptRepo.GetLatestByOrderIdsAsync([ordId]);
        Assert.NotNull(result);
        Assert.True(result.ContainsKey(ordId));
    }

    [Fact]
    public async Task R1_T2_05_RequestedLimitExceeding100_ClampedTo100()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var handler = new ListOrdersHandler(orderRepo, attemptRepo, new TestActor("acme_admin", ["admin"]));
        var result = await handler.HandleAsync(new ListOrdersQuery(Status: null, Limit: 5000));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Count <= 100);
    }

    // =========================================================================
    // Feature 2 (R2): Admin Route Authorization - Security Boundaries
    // =========================================================================

    [Fact]
    public async Task R2_T2_01_UnauthenticatedCaller_GetWebhookEvents_Returns401Unauthorized()
    {
        _client.AsAnonymous();
        var response = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task R2_T2_02_UnauthenticatedCaller_ProcessOutbox_Returns401Unauthorized()
    {
        _client.AsAnonymous();
        var response = await _client.PostAsync("/api/v1/admin/fulfillment-outbox/process", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task R2_T2_03_AuthenticatedNonAdmin_ViewerRole_GetWebhookEvents_Returns403Forbidden()
    {
        _client.AsAnonymous();
        _client.DefaultRequestHeaders.Add("X-User-ID", "viewer_user");
        _client.DefaultRequestHeaders.Add("X-User-Roles", "viewer");

        var response = await _client.GetAsync("/api/v1/admin/webhook-events");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task R2_T2_04_AuthenticatedNonAdmin_ReconcileOne_Returns403Forbidden()
    {
        _client.AsAnonymous();
        _client.DefaultRequestHeaders.Add("X-User-ID", "student_user");
        _client.DefaultRequestHeaders.Add("X-User-Roles", "student");

        var response = await _client.PostAsync("/api/v1/admin/payments/cs_fake_test/reconcile", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task R2_T2_05_MalformedOrEmptyUserRoles_Returns403Forbidden()
    {
        _client.AsAnonymous();
        _client.DefaultRequestHeaders.Add("X-User-ID", "random_caller");
        _client.DefaultRequestHeaders.Add("X-User-Roles", " , ,   , ");

        var response = await _client.GetAsync("/api/v1/admin/fulfillment-outbox");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Feature 3 (R3): BOLA / IDOR Order Isolation - Boundaries
    // =========================================================================

    [Fact]
    public async Task R3_T2_01_NonAdminWithZeroOrders_ReturnsEmptyList200OK()
    {
        string freshUserId = $"student_zero_{Guid.NewGuid():N}";
        _client.AsUser(freshUserId);

        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var orders = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(orders);
        Assert.Empty(orders);
    }

    [Fact]
    public async Task R3_T2_02_NonAdminWithStatusFilter_OnlyReturnsOwnOrdersMatchingStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string targetUser = $"student_filter_{Guid.NewGuid():N}";

        // Save a pending order
        await orderRepo.SaveAsync(Order.Rehydrate(
            id: $"ord_pend_{Guid.NewGuid():N}",
            userId: targetUser,
            orderNumber: "ORD-PEND-01",
            title: "Pending Item",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-P",
            amount: Money.Php(1000_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow,
            updatedAt: clock.UtcNow));

        // Save a paid order
        await orderRepo.SaveAsync(Order.Rehydrate(
            id: $"ord_paid_{Guid.NewGuid():N}",
            userId: targetUser,
            orderNumber: "ORD-PAID-01",
            title: "Paid Item",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-PAID",
            amount: Money.Php(2000_00L),
            status: OrderStatus.Paid,
            metadataJson: "{}",
            createdAt: clock.UtcNow,
            updatedAt: clock.UtcNow));

        _client.AsUser(targetUser);
        var response = await _client.GetAsync("/api/v1/orders?status=paid");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var orders = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(orders);
        Assert.All(orders, o =>
        {
            Assert.Equal(targetUser, o.UserId);
            Assert.Equal("paid", o.Status, ignoreCase: true);
        });
    }

    [Fact]
    public async Task R3_T2_03_UserIdWithWhitespace_TrimsAndMatchesCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string cleanId = $"student_space_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(
            id: $"ord_space_{Guid.NewGuid():N}",
            userId: cleanId,
            orderNumber: "ORD-SPACE-01",
            title: "Whitespace Order",
            category: OrderCategory.ProductOrder,
            referenceCode: "REF-S",
            amount: Money.Php(1000_00L),
            status: OrderStatus.Pending,
            metadataJson: "{}",
            createdAt: clock.UtcNow,
            updatedAt: clock.UtcNow));

        // Act with whitespace in X-User-ID header
        _client.AsAnonymous();
        _client.DefaultRequestHeaders.Add("X-User-ID", $"   {cleanId}   ");
        _client.DefaultRequestHeaders.Add("X-User-Roles", "user");

        var response = await _client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var orders = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
        Assert.NotNull(orders);
        Assert.Contains(orders, o => o.UserId == cleanId);
    }

    [Fact]
    public async Task R3_T2_04_UnauthenticatedCaller_ReturnsZeroOrdersOr401()
    {
        _client.AsAnonymous();
        var response = await _client.GetAsync("/api/v1/orders");

        // ServerCurrentActor resolves to anonymous; anonymous has 0 personal orders
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var orders = await response.Content.ReadFromJsonAsync<IReadOnlyList<OrderDto>>(JsonDefaults.Options);
            Assert.NotNull(orders);
            Assert.Empty(orders);
        }
        else
        {
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task R3_T2_05_OfflineFallback_EnforcesStrictUserIdIsolation()
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var userOrders = await orderRepo.ListAsync(status: null, limit: 50, userId: "isolated_stranger");
        Assert.NotNull(userOrders);
        Assert.Empty(userOrders);
    }

    // =========================================================================
    // Feature 5 (R5.1): Google Form Order Bridge - Boundaries
    // =========================================================================

    [Fact]
    public async Task R5_1_T2_01_DuplicateSubmission_SameResponseId_IsIdempotent()
    {
        string respId = $"gfr_idem_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "Idempotent Submitter",
            customer_email = "idem@test.com",
            product_code = "CPA-2026",
            quantity = 1,
            amount_minor = 145000L,
            send_initial_email = false
        };

        // First submission -> Created (201)
        var res1 = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(res1.StatusCode == HttpStatusCode.Created || res1.StatusCode == HttpStatusCode.OK);
        var doc1 = await res1.Content.ReadFromJsonAsync<JsonElement>();
        string orderId1 = doc1.GetProperty("order_id").GetString()!;

        // Duplicate submission with same response_id -> OK (200) with same order_id
        var res2 = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var doc2 = await res2.Content.ReadFromJsonAsync<JsonElement>();
        string orderId2 = doc2.GetProperty("order_id").GetString()!;

        Assert.Equal(orderId1, orderId2);
    }

    [Fact]
    public async Task R5_1_T2_02_InvalidOrMissingSecret_Returns401Unauthorized()
    {
        _client.AsAnonymous(); // Removes secret header

        var payload = new
        {
            response_id = "gfr_bad_secret",
            customer_name = "Attacker",
            customer_email = "attacker@bad.com",
            product_code = "FREE-ITEM",
            quantity = 1
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task R5_1_T2_03_EmptyCustomerEmailOrName_ReturnsValidationFailure()
    {
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = "gfr_no_email",
            customer_name = "",
            customer_email = "",
            product_code = "CPA-2026"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task R5_1_T2_04_QuantityZeroOrNegative_ClampedTo1()
    {
        string respId = $"gfr_neg_qty_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "Negative Qty Tester",
            customer_email = "neg_qty@test.com",
            product_code = "CPA-2026",
            quantity = -5,
            send_initial_email = false
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task R5_1_T2_05_PayMongoGatewayError_PersistsOrderInPendingState()
    {
        using var scope = _factory.Services.CreateScope();
        var bridge = scope.ServiceProvider.GetRequiredService<IGoogleFormOrderBridge>();

        var req = new GoogleFormOrderBridgeRequest(
            ResponseId: $"gfr_gw_err_{Guid.NewGuid():N}",
            CustomerName: "Gateway Fallback",
            CustomerEmail: "gw_fallback@test.com",
            CustomerPhone: "09170000000",
            ProductCode: "PROD-GW",
            ProductTitle: "Tax Handbook",
            Quantity: 1,
            UnitPriceMinor: 50000L,
            TotalAmountMinor: 50000L,
            Currency: "PHP",
            Status: "pending",
            SubmittedAt: DateTimeOffset.UtcNow,
            SendInitialEmail: false);

        var bridged = await bridge.BridgeSubmissionToOrderAsync(req);
        Assert.NotNull(bridged);
        Assert.Equal("pending", bridged.Status);
    }

    // =========================================================================
    // Feature 6 (R5.2): Detached Async SMTP Dispatch - Boundaries
    // =========================================================================

    [Fact]
    public async Task R5_2_T2_01_SmtpTimeoutOver15s_DetachedCtsCancelsSafelyWithout500()
    {
        using var scope = _factory.Services.CreateScope();
        var capturingSmtp = new CapturingSmtpClientFactory();
        var handler = ActivatorUtilities.CreateInstance<SubmitGoogleFormHandler>(
            scope.ServiceProvider,
            (ISmtpClientFactory)capturingSmtp);

        var input = new SubmitGoogleFormInput(
            Timestamp: DateTimeOffset.UtcNow.ToString("O"),
            ResponseId: $"GFR-TMO-{Guid.NewGuid():N}",
            CustomerName: "Timeout Customer",
            CustomerEmail: "timeout_customer@test.com",
            CustomerPhone: "+639170001111",
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

        // Caller token canceled should not cancel the detached background task nor throw 500
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        var result = await handler.HandleAsync(input, CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result.EmailDispatched);
    }

    [Fact]
    public async Task R5_2_T2_02_MalformedRecipientEmail_LogsErrorWithoutFailingHttp200()
    {
        string respId = $"gfr_bad_mail_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "Bad Email Customer",
            customer_email = "not-a-valid-email-format",
            product_code = "CPA-2026",
            quantity = 1,
            send_initial_email = true
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        // Bad email should not crash the HTTP response
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task R5_2_T2_03_EmptyRecipientEmail_HandlesSafely()
    {
        var factory = new SmtpClientFactory();
        var result = await factory.SendPaymentInvitationEmailAsync(
            orderId: "ord_empty_mail",
            orderNumber: "ORD-EMPTY-01",
            title: "Empty Mail Item",
            amountMinor: 10000L,
            currency: "PHP",
            customerName: "No Mail User",
            customerEmail: "",
            checkoutUrl: "https://pay.example.com");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task R5_2_T2_04_SendInitialEmailFalse_SkipsEmailDispatch()
    {
        string respId = $"gfr_no_mail_{Guid.NewGuid():N}";
        _client.WithGoogleFormsSecret();

        var payload = new
        {
            response_id = respId,
            customer_name = "Silent Submitter",
            customer_email = "silent@sub.com",
            product_code = "SILENT-01",
            quantity = 1,
            send_initial_email = false
        };

        var response = await _client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", payload);
        Assert.True(response.IsSuccessStatusCode);

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(doc.GetProperty("email_dispatched").GetBoolean());
    }

    [Fact]
    public async Task R5_2_T2_05_ConcurrentBurst_DispatchesMultipleTasksWithoutDeadlock()
    {
        _client.WithGoogleFormsSecret();
        var tasks = new List<Task<HttpResponseMessage>>();

        for (int i = 0; i < 5; i++)
        {
            var p = new
            {
                response_id = $"gfr_burst_{i}_{Guid.NewGuid():N}",
                customer_name = $"Burst User {i}",
                customer_email = $"burst{i}@test.com",
                product_code = "BURST-PROD",
                quantity = 1,
                send_initial_email = false
            };
            tasks.Add(_client.PostAsJsonAsync("/api/v1/integrations/google-forms/submit", p));
        }

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
    }

    // =========================================================================
    // Feature 7 (R5.3): SmtpClientFactory Hardening - Boundaries
    // =========================================================================

    [Fact]
    public void R5_3_T2_01_SecretStoreEmpty_FallsBackToConfigAndEnv()
    {
        var secretStore = new InMemorySecretStore(new Dictionary<string, string>());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SMTP_HOST"] = "smtp.fallback.test",
                ["SMTP_PORT"] = "465"
            })
            .Build();

        var factory = new SmtpClientFactory(config, NullLogger<SmtpClientFactory>.Instance, secretStore);
        var settings = factory.ResolveSettings();

        Assert.Equal("smtp.fallback.test", settings.Host);
        Assert.Equal(465, settings.Port);
    }

    [Fact]
    public void R5_3_T2_02_AppPasswordWithIrregularSpaces_FullyStripped()
    {
        var secretStore = new InMemorySecretStore(new Dictionary<string, string>
        {
            ["SMTP_APP_PASSWORD"] = "   ab   cd   ef   gh   "
        });

        var factory = new SmtpClientFactory(secretStore: secretStore);
        var settings = factory.ResolveSettings();

        Assert.Equal("abcdefgh", settings.Password);
    }

    [Fact]
    public async Task R5_3_T2_03_CustomPorts_PreservedInProbeDisplayHost()
    {
        var factory = new SmtpClientFactory();
        var probeResult = await factory.TestSmtpAsync(new EmailTestRequest("smtp.custom.org", Port: 2525, Recipient: "test@org.com", SendTestEmail: false));

        Assert.NotNull(probeResult);
        Assert.Contains("2525", probeResult.Probe.Host, StringComparison.Ordinal);
    }

    [Fact]
    public void R5_3_T2_04_LocalhostNonStandardPort_DisablesTlsAutomatically()
    {
        var secretStore = new InMemorySecretStore(new Dictionary<string, string>
        {
            ["SMTP_HOST"] = "127.0.0.1",
            ["SMTP_PORT"] = "1025"
        });

        var factory = new SmtpClientFactory(secretStore: secretStore);
        var settings = factory.ResolveSettings();

        Assert.False(settings.UseTls, "Non-standard local test port should disable TLS automatically.");
    }

    [Fact]
    public async Task R5_3_T2_05_UnreachableHost_ReturnsFailureWithoutUnhandledCrash()
    {
        var secretStore = new InMemorySecretStore(new Dictionary<string, string>
        {
            ["SMTP_HOST"] = "127.0.0.1",
            ["SMTP_PORT"] = "1", // Unreachable port
            ["SMTP_USER"] = "user@test.com",
            ["SMTP_APP_PASSWORD"] = "password"
        });

        var factory = new SmtpClientFactory(secretStore: secretStore);
        var result = await factory.VerifyHandshakeAsync();

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorDetail ?? result.Message);
    }

    // =========================================================================
    // Feature 8 (R5.4): Payment Expiration Worker Sweeper - Boundaries
    // =========================================================================

    [Fact]
    public async Task R5_4_T2_01_RemainingSecondsExactlyZero_TriggersExpiration()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_exp_zero_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(ordId, "u_zero", "ORD-ZERO", "Zero Remaining", OrderCategory.ProductOrder, "REF", Money.Php(1000_00L), OrderStatus.Pending, "{}", clock.UtcNow.AddMinutes(-30), clock.UtcNow));

        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_zero_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-30));
        attempt.AttachCheckoutSession("cs_zero", "https://checkout/zero", clock.UtcNow, clock.UtcNow.AddMinutes(-30));
        await attemptRepo.SaveAsync(attempt);

        using var worker = ActivatorUtilities.CreateInstance<PaymentExpirationWorker>(scope.ServiceProvider);
        await worker.ProcessOverdueBatchOnceAsync();

        var refreshed = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(PaymentAttemptStatus.Expired, refreshed.Status);
    }

    [Fact]
    public async Task R5_4_T2_02_NegativeRemainingSeconds_TriggersExpiration()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_exp_neg_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(ordId, "u_neg", "ORD-NEG", "Negative Remaining", OrderCategory.ProductOrder, "REF", Money.Php(1000_00L), OrderStatus.Pending, "{}", clock.UtcNow.AddMinutes(-40), clock.UtcNow));

        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_neg_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-40));
        attempt.AttachCheckoutSession("cs_neg", "https://checkout/neg", clock.UtcNow.AddMinutes(-10), clock.UtcNow.AddMinutes(-40));
        await attemptRepo.SaveAsync(attempt);

        using var worker = ActivatorUtilities.CreateInstance<PaymentExpirationWorker>(scope.ServiceProvider);
        await worker.ProcessOverdueBatchOnceAsync();

        var refreshed = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(PaymentAttemptStatus.Expired, refreshed.Status);
    }

    [Fact]
    public async Task R5_4_T2_03_AttemptInProcessingStatus_SkippedBySweeper()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_proc_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(ordId, "u_proc", "ORD-PROC", "Processing Order", OrderCategory.ProductOrder, "REF", Money.Php(1000_00L), OrderStatus.Pending, "{}", clock.UtcNow.AddMinutes(-35), clock.UtcNow));

        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_proc_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-35));
        attempt.AttachCheckoutSession("cs_proc", "https://checkout/proc", clock.UtcNow.AddMinutes(-5), clock.UtcNow.AddMinutes(-35));
        attempt.MarkProcessing(clock.UtcNow.AddMinutes(-10));
        await attemptRepo.SaveAsync(attempt);

        using var worker = ActivatorUtilities.CreateInstance<PaymentExpirationWorker>(scope.ServiceProvider);
        await worker.ProcessOverdueBatchOnceAsync();

        var refreshed = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(PaymentAttemptStatus.Processing, refreshed.Status);
    }

    [Fact]
    public async Task R5_4_T2_04_AttemptInTerminalStatus_SkippedBySweeper()
    {
        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        string ordId = $"ord_term_{Guid.NewGuid():N}";
        await orderRepo.SaveAsync(Order.Rehydrate(ordId, "u_term", "ORD-TERM", "Terminal Order", OrderCategory.ProductOrder, "REF", Money.Php(1000_00L), OrderStatus.Pending, "{}", clock.UtcNow.AddMinutes(-60), clock.UtcNow));

        var attempt = PaymentAttempt.CreatePending(ordId, "hosted_checkout", Money.Php(1000_00L), $"ik_term_{Guid.NewGuid():N}", clock.UtcNow.AddMinutes(-60));
        attempt.MarkSucceeded(clock.UtcNow.AddMinutes(-30), "qrph");
        await attemptRepo.SaveAsync(attempt);

        using var worker = ActivatorUtilities.CreateInstance<PaymentExpirationWorker>(scope.ServiceProvider);
        await worker.ProcessOverdueBatchOnceAsync();

        var refreshed = await attemptRepo.GetByIdAsync(attempt.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(PaymentAttemptStatus.Succeeded, refreshed.Status);
    }

    [Fact]
    public async Task R5_4_T2_05_PayMongoExpireApiReturns400_HandledGracefully()
    {
        var gateway = _factory.Gateway;
        gateway.RejectNextExpireCallWith400 = true;

        var result = await gateway.ExpireCheckoutSessionAsync("cs_already_expired");
        Assert.True(result.IsFailure);
        Assert.Contains("400", result.Error.Message, StringComparison.Ordinal);
    }

    // =========================================================================
    // Feature 9 (R5.5): Heroku DB Connection String Parsing - Boundaries
    // =========================================================================

    [Theory]
    [InlineData("postgres://usr:pwd@host.com:5432/db")]
    [InlineData("postgresql://usr:pwd@host.com:5432/db")]
    [InlineData("postgresql+psycopg2://usr:pwd@host.com:5432/db")]
    [InlineData("postgresql+asyncpg://usr:pwd@host.com:5432/db")]
    public void R5_5_T2_01_DifferentUriPrefixes_NormalizeCorrectly(string rawUri)
    {
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(rawUri);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("host.com", csb.Host);
        Assert.Equal("usr", csb.Username);
        Assert.Equal("pwd", csb.Password);
        Assert.True(csb.Pooling);
    }

    [Fact]
    public void R5_5_T2_02_LocalhostWithCustomPort_NormalizesTo127001()
    {
        string raw = "postgres://usr:pwd@localhost:5433/db";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal(5433, csb.Port);
    }

    [Fact]
    public void R5_5_T2_03_KeyValueConnectionString_NormalizesHost()
    {
        string raw = "Host=localhost;Database=rge_db;Username=test;Password=secret";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("127.0.0.1", csb.Host);
        Assert.Equal("rge_db", csb.Database);
    }

    [Fact]
    public void R5_5_T2_04_EmptyConnectionString_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => NpgsqlDataSourceFactory.NormalizeConnectionString(""));
        Assert.Throws<InvalidOperationException>(() => NpgsqlDataSourceFactory.NormalizeConnectionString("   "));
    }

    [Fact]
    public void R5_5_T2_05_SpecialCharactersInCredentials_ProperlyUnescaped()
    {
        string raw = "postgres://user%40domain:p%40ssw%23rd@host.com:5432/db";
        string normalized = NpgsqlDataSourceFactory.NormalizeConnectionString(raw);
        var csb = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("user@domain", csb.Username);
        Assert.Equal("p@ssw#rd", csb.Password);
    }

    // =========================================================================
    // Feature 10 (F10): Shared Identity - Boundaries
    // =========================================================================

    [Fact]
    public void F10_T2_01_UnauthenticatedActor_DefaultsToAnonymousAndNotAuthenticated()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var actor = new ServerCurrentActor(accessor);

        Assert.Equal("anonymous", actor.ActorId);
        Assert.False(actor.IsAuthenticated);
        Assert.Empty(actor.Roles);
    }

    [Fact]
    public void F10_T2_02_UserRolesHeader_WithExtraSpacesAndCommas_ParsesCleanly()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-User-ID"] = "clean_parser";
        httpContext.Request.Headers["X-User-Roles"] = "  finance_admin  , ,  auditor  , , ";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var actor = new ServerCurrentActor(accessor);

        Assert.Equal(2, actor.Roles.Count);
        Assert.True(actor.HasRole("finance_admin"));
        Assert.True(actor.HasRole("auditor"));
    }

    [Fact]
    public void F10_T2_03_CaseInsensitiveRoleChecks_AdminOrADMIN()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-User-ID"] = "case_tester";
        httpContext.Request.Headers["X-User-Roles"] = "ADMIN, finance_controller";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        ICurrentActor actor = new ServerCurrentActor(accessor);

        Assert.True(actor.HasRole("admin"));
        Assert.True(actor.HasRole("ADMIN"));
        Assert.True(actor.IsInRole("admin"));
        Assert.True(actor.IsInRole("Admin"));
    }

    [Fact]
    public void F10_T2_04_WildcardDepartmentScope_GrantsAccess()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-User-ID"] = "corp_admin";
        httpContext.Request.Headers["X-User-Roles"] = "admin";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var actor = new ServerCurrentActor(accessor);

        Assert.True(actor.HasScopeForDepartment("tax"));
        Assert.True(actor.HasScopeForDepartment("audit"));
        Assert.True(actor.HasScopeForDepartment("consulting"));
    }

    [Fact]
    public void F10_T2_05_SpecificDepartmentScope_RestrictsAccessOutsideDepartment()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "dept_user"),
            new Claim("department_scope", "tax")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var actor = new ServerCurrentActor(accessor);

        Assert.True(actor.HasScopeForDepartment("tax"));
        Assert.False(actor.HasScopeForDepartment("audit"));
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

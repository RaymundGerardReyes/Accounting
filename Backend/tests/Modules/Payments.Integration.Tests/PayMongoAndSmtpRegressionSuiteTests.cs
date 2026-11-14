#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA1849 // Call async methods when in an async method
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Accounting.Host.Composition;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Application.SaveCredentials;
using Rge.Integrations.Application.TestPayMongo;
using Rge.Integrations.Application.TestSmtp;
using Rge.Integrations.Contracts;
using Rge.Integrations.Infrastructure;
using SmtpClientFactory = Rge.Integrations.Infrastructure.SmtpClientFactory;
using Rge.Payments.Application.Ports;
using Rge.Payments.Infrastructure.PayMongo;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Mail;
using Rge.Tests.Common;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// PayMongo & SMTP 45-Scenario Comprehensive Regression Test Suite:
/// Scenarios 1-15: PayMongo Checkout Session, Basic Auth formatting, centavo invariants, and payload validation.
/// Scenarios 16-30: SMTP protocol lifecycle, STARTTLS/SSL configuration, timeout handling, and receipt attachments.
/// Scenarios 31-45: Multi-key webhook correlation, replay defenses, and credential rotation desynchronization.
/// </summary>
public sealed class PayMongoAndSmtpRegressionSuiteTests
{
    private static OfficialReceipt CreateSampleReceipt(string orderId = "ord_reg_001", string customerName = "Regression Tester")
    {
        return SmtpParityAndLifecycleTests.CreateSampleReceipt(orderId, customerName);
    }

    // =========================================================================
    // Scenarios 01-15: PayMongo Gateway, Invariants & Auth Formatting
    // =========================================================================

    [Theory]
    [InlineData("sk_test_key_1", "sk_test_key_1:")]
    [InlineData("sk_live_prod_abc", "sk_live_prod_abc:")]
    [InlineData("sk_test_1234567890", "sk_test_1234567890:")]
    public async Task Scenario01_To_03_PayMongo_OutboundBasicAuth_HasTrailingColon(string key, string expectedDecoded)
    {
        using var interceptor = new PayMongoInterceptingHandler();
        using var httpClient = new HttpClient(interceptor);
        var client = new PayMongoClient(
            httpClient,
            Options.Create(new PayMongoOptions { SecretKey = key, BaseUrl = "https://api.paymongo.com/v1/" }),
            NullLogger<PayMongoClient>.Instance);

        var request = new CreateCheckoutSessionRequest(
            OrderId: "ord_auth_test",
            OrderNumber: "ORD-AUTH-01",
            Description: "Auth test",
            Amount: new Money(50_00, "PHP"),
            RequestedPaymentMethod: "qrph",
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            CustomerName: "Auth Test",
            CustomerEmail: "auth@example.com",
            CustomerPhone: null,
            LineItems: [new CheckoutLineItemDto("Auth item", 1, 50_00, "PHP")]);

        await client.CreateCheckoutSessionAsync(request);

        var captured = interceptor.CapturedRequests.First();
        byte[] decodedBytes = Convert.FromBase64String(captured.Headers.Authorization!.Parameter!);
        string decoded = System.Text.Encoding.UTF8.GetString(decodedBytes);

        Assert.Equal(expectedDecoded, decoded);
    }

    [Theory]
    [InlineData(100_00)]  // PHP 100.00
    [InlineData(1_00)]    // PHP 1.00
    [InlineData(500_50)]  // PHP 500.50
    [InlineData(9999_99)] // PHP 9,999.99
    public async Task Scenario04_To_07_PayMongo_AmountMinor_SerializesIntegerCentavos(long amountMinor)
    {
        using var interceptor = new PayMongoInterceptingHandler();
        using var httpClient = new HttpClient(interceptor);
        var client = new PayMongoClient(
            httpClient,
            Options.Create(new PayMongoOptions { SecretKey = "sk_test_key", BaseUrl = "https://api.paymongo.com/v1/" }),
            NullLogger<PayMongoClient>.Instance);

        var request = new CreateCheckoutSessionRequest(
            OrderId: "ord_centavo_test",
            OrderNumber: "ORD-CENT-01",
            Description: "Centavo test",
            Amount: new Money(amountMinor, "PHP"),
            RequestedPaymentMethod: "card",
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            CustomerName: "Centavo Tester",
            CustomerEmail: "cent@example.com",
            CustomerPhone: null,
            LineItems: [new CheckoutLineItemDto("Centavo item", 1, amountMinor, "PHP")]);

        await client.CreateCheckoutSessionAsync(request);

        string body = interceptor.CapturedBodies.First();
        using var doc = JsonDocument.Parse(body);
        long serializedAmount = doc.RootElement
            .GetProperty("data")
            .GetProperty("attributes")
            .GetProperty("line_items")[0]
            .GetProperty("amount")
            .GetInt64();

        Assert.Equal(amountMinor, serializedAmount);
    }

    [Fact]
    public async Task Scenario08_PayMongo_IdempotencyKeyHeader_IsSentOnOutboundCreate()
    {
        using var interceptor = new PayMongoInterceptingHandler();
        using var httpClient = new HttpClient(interceptor);
        var client = new PayMongoClient(
            httpClient,
            Options.Create(new PayMongoOptions { SecretKey = "sk_test_idem", BaseUrl = "https://api.paymongo.com/v1/" }),
            NullLogger<PayMongoClient>.Instance);

        string idempotencyKey = Guid.NewGuid().ToString("N");
        var request = new CreateCheckoutSessionRequest(
            OrderId: "ord_idem_test",
            OrderNumber: "ORD-IDEM-01",
            Description: "Idempotency test",
            Amount: new Money(25_00, "PHP"),
            RequestedPaymentMethod: "gcash",
            IdempotencyKey: idempotencyKey,
            CustomerName: "Idem Test",
            CustomerEmail: "idem@example.com",
            CustomerPhone: null,
            LineItems: [new CheckoutLineItemDto("Idem item", 1, 25_00, "PHP")]);

        await client.CreateCheckoutSessionAsync(request);

        var captured = interceptor.CapturedRequests.First();
        Assert.True(captured.Headers.Contains("Idempotency-Key"));
        Assert.Equal(idempotencyKey, captured.Headers.GetValues("Idempotency-Key").First());
    }

    [Theory]
    [InlineData("QRPH", "qrph")]
    [InlineData("GCASH", "gcash")]
    [InlineData("PAYMAYA", "paymaya")]
    [InlineData("CARD", "card")]
    public async Task Scenario09_To_12_PayMongo_PaymentMethodMapping_ProducesExpectedArray(string inputMethod, string expectedMapped)
    {
        using var interceptor = new PayMongoInterceptingHandler();
        using var httpClient = new HttpClient(interceptor);
        var client = new PayMongoClient(
            httpClient,
            Options.Create(new PayMongoOptions { SecretKey = "sk_test_method", BaseUrl = "https://api.paymongo.com/v1/" }),
            NullLogger<PayMongoClient>.Instance);

        var request = new CreateCheckoutSessionRequest(
            OrderId: "ord_method_test",
            OrderNumber: "ORD-METH-01",
            Description: "Method test",
            Amount: new Money(10_00, "PHP"),
            RequestedPaymentMethod: inputMethod,
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            CustomerName: "Method Test",
            CustomerEmail: "method@example.com",
            CustomerPhone: null,
            LineItems: [new CheckoutLineItemDto("Method item", 1, 10_00, "PHP")]);

        await client.CreateCheckoutSessionAsync(request);

        string body = interceptor.CapturedBodies.First();
        using var doc = JsonDocument.Parse(body);
        var types = doc.RootElement
            .GetProperty("data")
            .GetProperty("attributes")
            .GetProperty("payment_method_types");

        Assert.Equal(expectedMapped, types[0].GetString());
    }

    [Fact]
    public async Task Scenario13_PayMongo_SuccessAndCancelUrls_EmbedOrderIdQueryParam()
    {
        using var interceptor = new PayMongoInterceptingHandler();
        using var httpClient = new HttpClient(interceptor);
        var client = new PayMongoClient(
            httpClient,
            Options.Create(new PayMongoOptions
            {
                SecretKey = "sk_test_urls",
                BaseUrl = "https://api.paymongo.com/v1/",
                SuccessUrl = "http://localhost:3000/payment/return",
                CancelUrl = "http://localhost:3000/checkout"
            }),
            NullLogger<PayMongoClient>.Instance);

        string orderId = "ord_url_embed_123";
        var request = new CreateCheckoutSessionRequest(
            OrderId: orderId,
            OrderNumber: "ORD-URL-01",
            Description: "Url test",
            Amount: new Money(10_00, "PHP"),
            RequestedPaymentMethod: "card",
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            CustomerName: "Url Test",
            CustomerEmail: "url@example.com",
            CustomerPhone: null,
            LineItems: [new CheckoutLineItemDto("Url item", 1, 10_00, "PHP")]);

        await client.CreateCheckoutSessionAsync(request);

        string body = interceptor.CapturedBodies.First();
        using var doc = JsonDocument.Parse(body);
        var attrs = doc.RootElement.GetProperty("data").GetProperty("attributes");

        string successUrl = attrs.GetProperty("success_url").GetString()!;
        string cancelUrl = attrs.GetProperty("cancel_url").GetString()!;

        Assert.Contains($"order_id={orderId}", successUrl, StringComparison.Ordinal);
        Assert.Contains($"order_id={orderId}", cancelUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scenario14_PayMongo_OfflineMode_ReturnsSyntheticCheckoutSession()
    {
        // When SecretKey starts with sk_test_offline, client bypasses network
        using var httpClient = new HttpClient();
        var client = new PayMongoClient(
            httpClient,
            Options.Create(new PayMongoOptions { SecretKey = "sk_test_offline_demo" }),
            NullLogger<PayMongoClient>.Instance);

        var request = new CreateCheckoutSessionRequest(
            OrderId: "ord_offline",
            OrderNumber: "ORD-OFFLINE-01",
            Description: "Offline test",
            Amount: new Money(10_00, "PHP"),
            RequestedPaymentMethod: "qrph",
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            CustomerName: "Offline Test",
            CustomerEmail: "offline@example.com",
            CustomerPhone: null,
            LineItems: [new CheckoutLineItemDto("Offline item", 1, 10_00, "PHP")]);

        var result = await client.CreateCheckoutSessionAsync(request);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("cs_offline_", result.Value.ProviderPaymentId, StringComparison.Ordinal);
        Assert.Equal("active", result.Value.Status);
    }

    [Fact]
    public async Task Scenario15_PayMongo_ExpireCheckoutSession_Offline_Succeeds()
    {
        using var httpClient = new HttpClient();
        var client = new PayMongoClient(
            httpClient,
            Options.Create(new PayMongoOptions { SecretKey = "sk_test_offline_demo" }),
            NullLogger<PayMongoClient>.Instance);

        var result = await client.ExpireCheckoutSessionAsync("cs_offline_test_to_expire");
        Assert.True(result.IsSuccess);
    }

    // =========================================================================
    // Scenarios 16-30: SMTP Implementation, Parity, Timeouts & Attachments
    // =========================================================================

    [Fact]
    public async Task Scenario16_SmtpReceiptMailer_SendsMail_WithPdfAttachment()
    {
        await using var server = new InProcessMockSmtpServer();
        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "password",
            EnableSsl = false
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateSampleReceipt("ord_reg_attach");
        byte[] pdf = "%PDF-1.4 sample stream"u8.ToArray();

        var result = await mailer.SendReceiptEmailAsync(receipt, "recipient@test.ph", pdf);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(server.ReceivedEmails);
        string email = server.ReceivedEmails.First();
        Assert.Contains("application/pdf", email, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scenario17_SmtpReceiptMailer_HtmlEncodesCustomerInput_AgainstXss()
    {
        await using var server = new InProcessMockSmtpServer();
        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "password",
            EnableSsl = false
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateSampleReceipt("ord_xss", "<script>alert('xss')</script>");

        await mailer.SendReceiptEmailAsync(receipt, "test@test.ph", [1, 2, 3]);

        string email = server.ReceivedEmails.First();
        Assert.DoesNotContain("<script>alert('xss')</script>", email, StringComparison.Ordinal);
        // MailMessage encodes HTML or quoted-printable; ensure '<' and '>' are escaped as &lt; / &gt;
        Assert.Contains("&lt;script&gt;", email, StringComparison.Ordinal);
        Assert.Contains("&lt;/script&gt;", email, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scenario18_SmtpReceiptMailer_Timeout_ReturnsFailure_Safely()
    {
        await using var server = new InProcessMockSmtpServer { SimulateTimeout = true };
        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = server.Port,
            SenderEmail = "billing@rge.ph",
            AppPassword = "password",
            EnableSsl = false,
            TimeoutMilliseconds = 500
        };

        var mailer = new SmtpReceiptMailer(options);
        var receipt = CreateSampleReceipt();

        var result = await mailer.SendReceiptEmailAsync(receipt, "timeout@test.ph", [1, 2, 3]);

        Assert.False(result.IsSuccess);
        Assert.Equal("Receipts.SmtpSendFailed", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Scenario19_To_21_SmtpReceiptMailer_InvalidRecipient_ReturnsValidation(string? invalidEmail)
    {
        var mailer = new SmtpReceiptMailer(new SmtpReceiptOptions { SenderEmail = "b@r.ph", AppPassword = "p" });
        var receipt = CreateSampleReceipt();

        var result = await mailer.SendReceiptEmailAsync(receipt, invalidEmail!, [1, 2]);

        Assert.False(result.IsSuccess);
        Assert.Equal("Receipts.EmptyRecipient", result.Error.Code);
    }

    [Fact]
    public async Task Scenario22_SmtpReceiptMailer_MissingSenderEmail_SkipsSilently()
    {
        var mailer = new SmtpReceiptMailer(new SmtpReceiptOptions { SenderEmail = "", AppPassword = "p" });
        var receipt = CreateSampleReceipt();

        var result = await mailer.SendReceiptEmailAsync(receipt, "user@test.ph", [1, 2]);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Scenario23_SmtpReceiptMailer_MissingPassword_SkipsSilently()
    {
        var mailer = new SmtpReceiptMailer(new SmtpReceiptOptions { SenderEmail = "billing@rge.ph", AppPassword = "" });
        var receipt = CreateSampleReceipt();

        var result = await mailer.SendReceiptEmailAsync(receipt, "user@test.ph", [1, 2]);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Scenario24_SmtpClientFactory_Probe_MeasuresLatency_GreaterThanZero()
    {
        var secretStore = new SecretStoreWriter();
        var factory = new SmtpClientFactory(secretStore);

        var response = await factory.TestSmtpAsync(new EmailTestRequest(Host: "smtp.gmail.com", Port: 587));

        Assert.True(response.Success);
        Assert.True(response.Probe.LatencyMs >= 1);
        Assert.Equal("connected", response.Probe.OperationalState);
    }

    [Fact]
    public async Task Scenario25_SmtpClientFactory_Probe_DispatchesTestEmailResult()
    {
        var secretStore = new SecretStoreWriter();
        var factory = new SmtpClientFactory(secretStore);

        var response = await factory.TestSmtpAsync(new EmailTestRequest(
            Host: "smtp.gmail.com",
            Port: 587,
            SendTestEmail: true,
            Recipient: "audit@rge.ph"));

        Assert.True(response.Success);
        Assert.NotNull(response.EmailDispatch);
        Assert.True(response.EmailDispatch.Dispatched);
        Assert.Equal("audit@rge.ph", response.EmailDispatch.Recipient);
    }

    [Fact]
    public async Task Scenario26_SmtpClientFactory_ReadsFallbackHostFromSecretStore()
    {
        var secretStore = new SecretStoreWriter();
        await secretStore.SaveCredentialsAsync(new SaveCredentialsRequest(SmtpHost: "custom.relay.ph"));

        var factory = new SmtpClientFactory(secretStore);
        var response = await factory.TestSmtpAsync(new EmailTestRequest());

        Assert.Contains("custom.relay.ph", response.Probe.Host, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(465)]
    [InlineData(587)]
    [InlineData(2525)]
    public async Task Scenario27_To_30_SmtpClientFactory_CustomPorts_ArePreservedInProbeHost(int port)
    {
        var secretStore = new SecretStoreWriter();
        var factory = new SmtpClientFactory(secretStore);

        var response = await factory.TestSmtpAsync(new EmailTestRequest(Port: port));

        Assert.EndsWith($":{port}", response.Probe.Host, StringComparison.Ordinal);
    }

    // =========================================================================
    // Scenarios 31-45: Multi-Key Webhooks, Correlation, Replay & Rotation
    // =========================================================================

    [Fact]
    public async Task Scenario31_SaveCredentials_RotatesSmtpAndPayMongoSecrets_Atomically()
    {
        var store = new SecretStoreWriter();
        var response = await store.SaveCredentialsAsync(new SaveCredentialsRequest(
            PaymongoSecretKey: "sk_test_rotated_123",
            PaymongoWebhookSecret: "whsec_rotated_456",
            SmtpHost: "smtp.office365.com",
            SmtpPort: 587,
            SmtpUser: "accounting@rge.ph",
            SmtpAppPassword: "new_password_2026"));

        Assert.True(response.Success);
        Assert.Equal(6, response.UpdatedKeys.Count);
        Assert.Equal("sk_test_rotated_123", store.GetSecret("PAYMONGO_SECRET_KEY"));
        Assert.Equal("whsec_rotated_456", store.GetSecret("PAYMONGO_WEBHOOK_SECRET"));
        Assert.Equal("smtp.office365.com", store.GetSecret("SMTP_HOST"));
        Assert.Equal("587", store.GetSecret("SMTP_PORT"));
        Assert.Equal("accounting@rge.ph", store.GetSecret("SMTP_USER"));
        Assert.Equal("new_password_2026", store.GetSecret("SMTP_APP_PASSWORD"));
    }

    [Fact]
    public async Task Scenario32_TestPayMongo_Probe_ReportsModeCorrectly()
    {
        var store = new SecretStoreWriter();

        var testRes = await store.TestPayMongoAsync(new PayMongoTestRequest(SecretKey: "sk_test_abc"));
        Assert.Equal("test", testRes.Probe.Mode);

        var liveRes = await store.TestPayMongoAsync(new PayMongoTestRequest(SecretKey: "sk_live_xyz"));
        Assert.Equal("live", liveRes.Probe.Mode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Scenario33_To_36_PayMongoSignatureVerifier_ValidatesTimestampsWithinTolerance(int minutesOffset)
    {
        var now = new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now);
        const string secret = "whsec_timestamp_test";
        const string json = "{\"id\":\"evt_test\"}";

        string header = WebhookSigner.CreateSignatureHeader(json, secret, now.AddMinutes(-minutesOffset).ToUnixTimeSeconds());
        var verifier = new PayMongoSignatureVerifier(
            Options.Create(new PayMongoOptions { WebhookSecret = secret, AllowTestWebhookSignatures = true }),
            clock);

        Assert.True(verifier.IsValidSignature(header, json));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(1440)]
    public void Scenario37_To_40_PayMongoSignatureVerifier_RejectsTimestampsOutsideTolerance(int minutesOffset)
    {
        var now = new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now);
        const string secret = "whsec_replay_test";
        const string json = "{\"id\":\"evt_test\"}";

        string header = WebhookSigner.CreateSignatureHeader(json, secret, now.AddMinutes(-minutesOffset).ToUnixTimeSeconds());
        var verifier = new PayMongoSignatureVerifier(
            Options.Create(new PayMongoOptions { WebhookSecret = secret, AllowTestWebhookSignatures = true }),
            clock);

        Assert.False(verifier.IsValidSignature(header, json));
    }

    [Fact]
    public void Scenario41_PayMongoSignatureVerifier_TamperedBody_FailsVerification()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new FakeClock(now);
        const string secret = "whsec_tamper_test";

        string validHeader = WebhookSigner.CreateSignatureHeader("{\"data\":1}", secret, now.ToUnixTimeSeconds());
        var verifier = new PayMongoSignatureVerifier(
            Options.Create(new PayMongoOptions { WebhookSecret = secret, AllowTestWebhookSignatures = true }),
            clock);

        Assert.False(verifier.IsValidSignature(validHeader, "{\"data\":2}"));
    }

    [Fact]
    public void Scenario42_PayMongoSignatureVerifier_EmptySignatureHeader_FailsVerification()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var verifier = new PayMongoSignatureVerifier(
            Options.Create(new PayMongoOptions { WebhookSecret = "whsec_test", AllowTestWebhookSignatures = true }),
            clock);

        Assert.False(verifier.IsValidSignature("", "{}"));
        Assert.False(verifier.IsValidSignature(null, "{}"));
    }

    [Fact]
    public void Scenario43_PayMongoSignatureVerifier_EmptyWebhookSecret_FailsVerification()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var verifier = new PayMongoSignatureVerifier(
            Options.Create(new PayMongoOptions { WebhookSecret = "", AllowTestWebhookSignatures = true }),
            clock);

        string header = WebhookSigner.CreateSignatureHeader("{}", "whsec_any", clock.UtcNow.ToUnixTimeSeconds());
        Assert.False(verifier.IsValidSignature(header, "{}"));
    }

    [Fact]
    public async Task Scenario44_SecretStoreWriter_ProbeAll_AggregatesIntegrations()
    {
        var store = new SecretStoreWriter();
        var result = await store.ProbeAllAsync();

        Assert.True(result.Success);
        Assert.NotEmpty(result.Integrations);
    }

    [Fact]
    public void Scenario45_ExtractIdWithPrefix_ExtractsTokensAccurately()
    {
        const string sampleUrl = "https://checkout.paymongo.com/sources/src_ba7hPZAKRbxYVCFXT7LFao9q/authenticate";
        string? srcId = PayMongoClient.ExtractIdWithPrefix(sampleUrl, "src_");

        Assert.Equal("src_ba7hPZAKRbxYVCFXT7LFao9q", srcId);
    }
}

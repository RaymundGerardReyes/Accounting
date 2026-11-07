#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA1849 // Call async methods when in an async method
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
using Rge.Integrations.Contracts;
using Rge.Payments.Application.Ports;
using Rge.Payments.Infrastructure.PayMongo;
using Rge.Tests.Common;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Deterministic HttpMessageHandler for capturing and verifying outbound PayMongo HTTP requests.
/// </summary>
internal sealed class PayMongoInterceptingHandler : HttpMessageHandler
{
    public ConcurrentBag<HttpRequestMessage> CapturedRequests { get; } = [];
    public ConcurrentBag<string> CapturedBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CapturedRequests.Add(request);

        string body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : string.Empty;
        CapturedBodies.Add(body);

        string path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.Contains("checkout_sessions", StringComparison.OrdinalIgnoreCase))
        {
            const string json = """
            {
              "data": {
                "id": "cs_diagnostic_proof_123",
                "type": "checkout_session",
                "attributes": {
                  "checkout_url": "https://checkout.paymongo.com/cs_diagnostic_proof_123",
                  "status": "active",
                  "payment_method_used": "qrph",
                  "payments": [],
                  "expired_at": 1735689600
                }
              }
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>
/// PayMongo Diagnostic Proof Test Suite proving:
/// 1. Outbound Basic Auth formatting has trailing colon ($"{secretKey}:") before Base64 encoding.
/// 2. Payload serializes amounts in integer centavos (ADR-0004).
/// 3. Credential updates via SaveCredentialsHandler immediately reflect in TestPayMongo.
/// 4. Webhook HMAC signatures are verified accurately across t=, te=, li=.
/// </summary>
public sealed class PayMongoDiagnosticProofTests
{
    [Fact]
    public async Task PayMongoClient_OutboundRequest_IncludesBasicAuthWithTrailingColon()
    {
        using var interceptor = new PayMongoInterceptingHandler();
        using var httpClient = new HttpClient(interceptor);

        const string testKey = "sk_test_diagnostic_key_xyz";
        var options = Options.Create(new PayMongoOptions
        {
            SecretKey = testKey,
            BaseUrl = "https://api.paymongo.com/v1/"
        });

        var client = new PayMongoClient(httpClient, options, NullLogger<PayMongoClient>.Instance);

        var request = new CreateCheckoutSessionRequest(
            OrderId: "ord_proof_001",
            OrderNumber: "ORD-PROOF-001",
            Description: "Diagnostic proof checkout",
            Amount: new Money(100_00, "PHP"),
            RequestedPaymentMethod: "qrph",
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            CustomerName: "Juan Dela Cruz",
            CustomerEmail: "juan@example.com",
            CustomerPhone: "+639171234567",
            LineItems: [new CheckoutLineItemDto("Line item 1", 1, 100_00, "PHP")]);

        var result = await client.CreateCheckoutSessionAsync(request);
        Assert.True(result.IsSuccess);

        Assert.NotEmpty(interceptor.CapturedRequests);
        var captured = interceptor.CapturedRequests.First();

        AuthenticationHeaderValue? authHeader = captured.Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Basic", authHeader.Scheme);

        // Decode Base64 auth parameter
        byte[] authBytes = Convert.FromBase64String(authHeader.Parameter!);
        string decoded = Encoding.UTF8.GetString(authBytes);

        // Prove the trailing colon requirement: $"{secretKey}:"
        Assert.Equal($"{testKey}:", decoded);
    }

    [Fact]
    public async Task PayMongoClient_CheckoutPayload_SerializesIntegerCentavos_And_PreservesInvariant()
    {
        using var interceptor = new PayMongoInterceptingHandler();
        using var httpClient = new HttpClient(interceptor);

        var options = Options.Create(new PayMongoOptions
        {
            SecretKey = "sk_test_centavos_proof",
            BaseUrl = "https://api.paymongo.com/v1/"
        });

        var client = new PayMongoClient(httpClient, options, NullLogger<PayMongoClient>.Instance);

        const long expectedCentavos = 150_75; // PHP 150.75
        var request = new CreateCheckoutSessionRequest(
            OrderId: "ord_centavo_002",
            OrderNumber: "ORD-CENTAVO-002",
            Description: "Centavo Invariant Proof",
            Amount: new Money(expectedCentavos, "PHP"),
            RequestedPaymentMethod: "card",
            IdempotencyKey: Guid.NewGuid().ToString("N"),
            CustomerName: "Maria Santos",
            CustomerEmail: "maria@example.com",
            CustomerPhone: null,
            LineItems: [new CheckoutLineItemDto("Invoice item", 1, expectedCentavos, "PHP")]);

        var result = await client.CreateCheckoutSessionAsync(request);
        Assert.True(result.IsSuccess);

        Assert.NotEmpty(interceptor.CapturedBodies);
        string capturedJson = interceptor.CapturedBodies.First();

        using var doc = JsonDocument.Parse(capturedJson);
        var root = doc.RootElement;
        var attributes = root.GetProperty("data").GetProperty("attributes");

        var lineItems = attributes.GetProperty("line_items");
        Assert.True(lineItems.GetArrayLength() > 0);

        var firstItem = lineItems[0];
        long amountInPayload = firstItem.GetProperty("amount").GetInt64();

        // Must be exact integer centavos (15075), not floating 150.75
        Assert.Equal(expectedCentavos, amountInPayload);
    }

    [Fact]
    public async Task Integrations_SaveCredentials_ImmediatelyReflectsIn_TestPayMongoHandler()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var config = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountingPlatformHost(config);
        services.Replace(ServiceDescriptor.Singleton<IClock>(clock));

        await using ServiceProvider sp = services.BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var saveHandler = scope.ServiceProvider.GetRequiredService<SaveCredentialsHandler>();
        var testHandler = scope.ServiceProvider.GetRequiredService<TestPayMongoHandler>();

        const string rotatedKey = "sk_live_rotated_key_99999";
        const string rotatedWebhookSec = "whsec_rotated_webhook_88888";

        // Save new credentials
        var saveResult = await saveHandler.HandleAsync(new SaveCredentialsCommand(new SaveCredentialsRequest(
            PaymongoSecretKey: rotatedKey,
            PaymongoWebhookSecret: rotatedWebhookSec)));

        Assert.True(saveResult.IsSuccess);
        Assert.Contains("PAYMONGO_SECRET_KEY", saveResult.Value.UpdatedKeys, StringComparer.Ordinal);
        Assert.Contains("PAYMONGO_WEBHOOK_SECRET", saveResult.Value.UpdatedKeys, StringComparer.Ordinal);

        // Probe without passing explicit secret key in request -> probe reads from store
        var testResult = await testHandler.HandleAsync(new TestPayMongoCommand(new PayMongoTestRequest()));

        Assert.True(testResult.IsSuccess);
        Assert.Equal("live", testResult.Value.Probe.Mode);
        Assert.Equal("connected", testResult.Value.Probe.OperationalState);
    }

    [Theory]
    [InlineData("whsec_valid_secret_key_12345", true)]
    [InlineData("whsec_wrong_key", false)]
    public void PayMongoSignatureVerifier_ValidatesHmacSignaturesAccurately(string secretToVerifyWith, bool shouldSucceed)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero));
        const string correctSecret = "whsec_valid_secret_key_12345";
        const string jsonPayload = "{\"data\":{\"id\":\"evt_proof_001\",\"attributes\":{\"type\":\"checkout_session.payment.paid\"}}}";

        string validSignatureHeader = WebhookSigner.CreateSignatureHeader(
            rawBody: jsonPayload,
            secret: correctSecret,
            timestampUnix: clock.UtcNow.ToUnixTimeSeconds());

        var options = Options.Create(new PayMongoOptions
        {
            WebhookSecret = secretToVerifyWith,
            AllowTestWebhookSignatures = true
        });

        var verifier = new PayMongoSignatureVerifier(options, clock);
        bool isValid = verifier.IsValidSignature(validSignatureHeader, jsonPayload);

        Assert.Equal(shouldSucceed, isValid);
    }

    [Fact]
    public void PayMongoSignatureVerifier_RejectsReplayAttacksOutsideToleranceWindow()
    {
        var fixedTime = new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(fixedTime);
        const string secret = "whsec_replay_proof_secret";
        const string jsonPayload = "{\"data\":{\"id\":\"evt_proof_replay\"}}";

        // Signature generated 6 minutes ago (exceeds 5 minute replay window)
        string oldSignatureHeader = WebhookSigner.CreateSignatureHeader(
            rawBody: jsonPayload,
            secret: secret,
            timestampUnix: fixedTime.AddMinutes(-6).ToUnixTimeSeconds());

        var options = Options.Create(new PayMongoOptions
        {
            WebhookSecret = secret,
            AllowTestWebhookSignatures = true
        });

        var verifier = new PayMongoSignatureVerifier(options, clock);
        bool isValid = verifier.IsValidSignature(oldSignatureHeader, jsonPayload);

        Assert.False(isValid, "Verifier must reject webhook timestamps older than 5 minutes.");
    }
}

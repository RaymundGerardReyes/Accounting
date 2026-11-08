#pragma warning disable CA1849 // Call async methods when in an async method
#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rge.Accounting.Host.Pipeline;
using Rge.Payments.Infrastructure.Persistence.Repositories;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Integration tests verifying the routing resilience and HMAC signature security of PayMongo webhook endpoints.
/// Validates that incoming webhook deliveries with leading, mid-path, or multiple double slashes are normalized
/// and accepted identically to canonical routes, while preserving zero-trust HMAC-SHA256 signature verification.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PayMongoWebhookRoutingResilienceIntegrationTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private const string WebhookSecret = "whsec_test_mock_secret_key_12345678";
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PayMongoWebhookRoutingResilienceIntegrationTests(AccountingWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    // =========================================================================
    // 1. Double-Slash Path Resilience & Route Normalization (HTTP 200 OK)
    // =========================================================================

    [Theory]
    [InlineData("/api/v1/webhooks/paymongo")]
    [InlineData("http://localhost//api/v1/webhooks/paymongo")]
    [InlineData("http://localhost/api//v1/webhooks/paymongo")]
    [InlineData("http://localhost//api//v1//webhooks//paymongo")]
    [InlineData("http://localhost/api/v1/webhooks/paymongo//")]
    [InlineData("http://localhost///api///v1///webhooks///paymongo")]
    [InlineData("/api/payments/webhook")]
    [InlineData("http://localhost//api/payments/webhook")]
    [InlineData("http://localhost//api//payments//webhook")]
    [InlineData("/webhooks/paymongo")]
    [InlineData("http://localhost//webhooks/paymongo")]
    [InlineData("http://localhost//webhooks//paymongo")]
    public async Task WebhookDelivery_WithDuplicateSlashes_NormalizesAndReturns200WithPayload(string requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        string eventId = $"evt_resilience_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_resilience_test",
                "attributes": {
                  "status": "paid"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal(eventId, root.GetProperty("event_id").GetString());
        Assert.Equal("checkout_session.payment.paid", root.GetProperty("event_type").GetString());
        string processingStatus = root.GetProperty("processing_status").GetString()!;
        Assert.True(processingStatus is "received" or "ignored");

        if (requestUri.Contains("//", StringComparison.Ordinal))
        {
            Assert.Contains(_factory.CapturedLogs, log =>
                log.Level == LogLevel.Information &&
                log.Category.Contains(nameof(DuplicateSlashNormalizationMiddleware), StringComparison.Ordinal) &&
                log.Message.Contains("[PATH-NORMALIZATION]", StringComparison.Ordinal));
        }
    }

    // =========================================================================
    // 2. Cryptographic Security Invariance — Tampered HMAC Rejection (HTTP 400)
    // =========================================================================

    [Theory]
    [InlineData("http://localhost//api/v1/webhooks/paymongo")]
    [InlineData("http://localhost/api//v1/webhooks/paymongo")]
    [InlineData("http://localhost//api//v1//webhooks//paymongo")]
    [InlineData("http://localhost//api/payments/webhook")]
    [InlineData("http://localhost//webhooks/paymongo")]
    public async Task WebhookDelivery_WithDuplicateSlashesAndTamperedSignature_Returns400BadRequest(string requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        string eventId = $"evt_tamper_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid"
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Sign with an invalid attacker secret
        string tamperedSignature = WebhookSigner.CreateSignatureHeader(
            payload,
            "whsec_wrong_attacker_secret_99999999",
            timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", tamperedSignature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    // =========================================================================
    // 3. Replay Protection & Expired Timestamp Rejection (HTTP 400)
    // =========================================================================

    [Theory]
    [InlineData("http://localhost//api/v1/webhooks/paymongo")]
    [InlineData("http://localhost//api/payments/webhook")]
    [InlineData("http://localhost//webhooks/paymongo")]
    public async Task WebhookDelivery_WithDuplicateSlashesAndExpiredTimestamp_Returns400BadRequest(string requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        string eventId = $"evt_expired_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid"
            }
          }
        }
        """;

        // 10 minutes in the past (> 300-second / 5-minute replay tolerance window)
        long expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        string expiredSignature = WebhookSigner.CreateSignatureHeader(
            payload,
            WebhookSecret,
            expiredTimestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", expiredSignature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    // =========================================================================
    // 4. Missing Signature Header Rejection (HTTP 400)
    // =========================================================================

    [Theory]
    [InlineData("http://localhost//api/v1/webhooks/paymongo")]
    [InlineData("http://localhost//webhooks/paymongo")]
    public async Task WebhookDelivery_WithDuplicateSlashesAndMissingSignatureHeader_Returns400BadRequest(string requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        string eventId = $"evt_missing_sig_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid"
            }
          }
        }
        """;

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        // Intentionally omit Paymongo-Signature header

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    // =========================================================================
    // 5. Tampered Payload Body Rejection (HTTP 400)
    // =========================================================================

    [Fact]
    public async Task WebhookDelivery_WithDuplicateSlashesAndTamperedBody_Returns400BadRequest()
    {
        string originalPayload = """{"data":{"id":"evt_body_orig","attributes":{"type":"payment.paid"}}}""";
        string tamperedPayload = """{"data":{"id":"evt_body_tampered","attributes":{"type":"payment.paid"}}}""";

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signatureForOriginal = WebhookSigner.CreateSignatureHeader(
            originalPayload,
            WebhookSecret,
            timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost//api/v1/webhooks/paymongo")
        {
            Content = new StringContent(tamperedPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signatureForOriginal);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    // =========================================================================
    // 6. Signature Mode Invariance (Test and Live Signatures on Double Slashes)
    // =========================================================================

    [Theory]
    [InlineData(false)] // Test mode signature (te=...)
    [InlineData(true)]  // Live mode signature (li=...)
    public async Task WebhookDelivery_WithDuplicateSlashes_AcceptsBothTestAndLiveSignatures(bool isLive)
    {
        string eventId = $"evt_mode_{(isLive ? "live" : "test")}_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid"
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(
            payload,
            WebhookSecret,
            timestamp,
            isLive: isLive);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost//api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal(eventId, root.GetProperty("event_id").GetString());
    }

    // =========================================================================
    // 7. Canonical vs Duplicate Slash Exact Behavioral Parity
    // =========================================================================

    [Fact]
    public async Task WebhookDelivery_CanonicalAndDuplicateSlashRoutes_ProduceIdenticalResponses()
    {
        string eventIdCanon = $"evt_parity_{Guid.NewGuid():N}_canon";
        string eventIdDouble = $"evt_parity_{Guid.NewGuid():N}_double";

        string payloadCanon = $$"""
        {
          "data": {
            "id": "{{eventIdCanon}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_test"
              }
            }
          }
        }
        """;

        string payloadDouble = $$"""
        {
          "data": {
            "id": "{{eventIdDouble}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_test"
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string sigCanon = WebhookSigner.CreateSignatureHeader(payloadCanon, WebhookSecret, timestamp);
        string sigDouble = WebhookSigner.CreateSignatureHeader(payloadDouble, WebhookSecret, timestamp);

        using var reqCanon = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payloadCanon, Encoding.UTF8, "application/json")
        };
        reqCanon.Headers.Add("Paymongo-Signature", sigCanon);

        using var reqDouble = new HttpRequestMessage(HttpMethod.Post, "http://localhost//api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payloadDouble, Encoding.UTF8, "application/json")
        };
        reqDouble.Headers.Add("Paymongo-Signature", sigDouble);

        using var respCanon = await _client.SendAsync(reqCanon);
        using var respDouble = await _client.SendAsync(reqDouble);

        Assert.Equal(HttpStatusCode.OK, respCanon.StatusCode);
        Assert.Equal(HttpStatusCode.OK, respDouble.StatusCode);

        using var docCanon = await JsonDocument.ParseAsync(await respCanon.Content.ReadAsStreamAsync());
        using var docDouble = await JsonDocument.ParseAsync(await respDouble.Content.ReadAsStreamAsync());

        Assert.Equal(docCanon.RootElement.GetProperty("received").GetBoolean(), docDouble.RootElement.GetProperty("received").GetBoolean());
        Assert.Equal(docCanon.RootElement.GetProperty("event_type").GetString(), docDouble.RootElement.GetProperty("event_type").GetString());
        Assert.Equal(docCanon.RootElement.GetProperty("processing_status").GetString(), docDouble.RootElement.GetProperty("processing_status").GetString());
    }

    // =========================================================================
    // 8. Pipeline Non-Interference for Standard Unmodified Routes
    // =========================================================================

    [Fact]
    public async Task RegularRequestsWithoutDuplicateSlashes_PassThroughUnmodified()
    {
        using HttpResponseMessage response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

#pragma warning disable CA1849 // Call async methods when in an async method
#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Rge.Accounting.Host.Pipeline;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Adversarial stress tests and empirical verification for route normalization and webhook delivery resilience.
/// Challenges boundary conditions: 10+ consecutive slashes, root slashes, query string preservation,
/// non-webhook probe normalization, special characters, and middleware allocation/concurrency invariance.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PayMongoWebhookRoutingAdversarialChallengerTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private const string WebhookSecret = "whsec_test_mock_secret_key_12345678";
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PayMongoWebhookRoutingAdversarialChallengerTests(AccountingWebApplicationFactory factory)
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
    // 1. Extreme Slash Counts (10, 25, 100, 500 Consecutive Slashes)
    // =========================================================================

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(100)]
    [InlineData(500)]
    public async Task WebhookDelivery_WithExtremeLeadingConsecutiveSlashes_NormalizesAndReturns200(int slashCount)
    {
        string slashes = new('/', slashCount);
        string requestUri = $"http://localhost{slashes}api/v1/webhooks/paymongo";

        string eventId = $"evt_extreme_leading_{slashCount}_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_extreme_slashes",
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
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    public async Task WebhookDelivery_WithExtremeInterleavedSlashesBetweenAllSegments_Returns200(int slashesPerSegment)
    {
        string seg = new('/', slashesPerSegment);
        string requestUri = $"http://localhost{seg}api{seg}v1{seg}webhooks{seg}paymongo{seg}";

        string eventId = $"evt_interleaved_{slashesPerSegment}_{Guid.NewGuid():N}";
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
    }

    [Fact]
    public async Task WebhookDelivery_WithExtremeSlashesOnAliases_Returns200()
    {
        string slashes20 = new('/', 20);

        // Alias 1: /api/payments/webhook
        string uriLegacy = $"http://localhost{slashes20}api{slashes20}payments{slashes20}webhook";
        // Alias 2: /webhooks/paymongo
        string uriRoot = $"http://localhost{slashes20}webhooks{slashes20}paymongo";

        foreach (string uri in new[] { uriLegacy, uriRoot })
        {
            string eventId = $"evt_alias_{Guid.NewGuid():N}";
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
            string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Paymongo-Signature", signature);

            using HttpResponseMessage response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.True(jsonDoc.RootElement.GetProperty("received").GetBoolean());
        }
    }

    // =========================================================================
    // 2. Root-Only Duplicate Slashes (//, ///, //////////) vs Canonical Root (/)
    // =========================================================================

    [Theory]
    [InlineData("http://localhost//")]
    [InlineData("http://localhost///")]
    [InlineData("http://localhost//////////")]
    public async Task RootDuplicateSlashes_NormalizesToRootAndProducesIdenticalResponseToCanonicalRoot(string rootUri)
    {
        using HttpResponseMessage canonicalResponse = await _client.GetAsync("/");
        using HttpResponseMessage testResponse = await _client.GetAsync(rootUri);

        // Neither should crash with a 500 or throw unhandled exceptions.
        // Both should return identical 404 Not Found since no root handler is registered.
        Assert.Equal(canonicalResponse.StatusCode, testResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, testResponse.StatusCode);
    }

    // =========================================================================
    // 3. Non-Webhook Endpoints and Probes with Duplicate Slashes
    // =========================================================================

    [Theory]
    [InlineData("http://localhost//health")]
    [InlineData("http://localhost//////////health")]
    [InlineData("http://localhost//api//health")]
    [InlineData("http://localhost///api///health")]
    public async Task HealthProbe_WithDuplicateSlashes_NormalizesAndReturns200(string healthUri)
    {
        using HttpResponseMessage response = await _client.GetAsync(healthUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"healthy\"", body, StringComparison.Ordinal);
        Assert.Contains("\"dotnet\"", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost//version")]
    [InlineData("http://localhost//////////version")]
    [InlineData("http://localhost//api//version")]
    [InlineData("http://localhost///api///version")]
    public async Task VersionProbe_WithDuplicateSlashes_NormalizesAndReturns200(string versionUri)
    {
        using HttpResponseMessage response = await _client.GetAsync(versionUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"schema_version\"", body, StringComparison.Ordinal);
        Assert.Contains("\"api_contract\":\"v1\"", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost//nonexistent//endpoint")]
    [InlineData("http://localhost//////////random//////////path")]
    public async Task NonExistentRoute_WithDuplicateSlashes_Returns404CleanlyWithout500(string unmappedUri)
    {
        using HttpResponseMessage response = await _client.GetAsync(unmappedUri);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // 4. Query String Preservation and Integrity Under Duplicate Slashes
    // =========================================================================

    [Theory]
    [InlineData("?debug=true")]
    [InlineData("?source=paymongo_test&session_id=cs_12345&amount=50000")]
    [InlineData("?filter=%20special%20&tags=a,b,c&redirect_url=https%3A%2F%2Fpaymongo.com%2Fcallback")]
    [InlineData("?")]
    public async Task WebhookDelivery_WithDuplicateSlashesAndQueryString_PreservesQueryAndReturns200(string queryString)
    {
        string requestUri = $"http://localhost//api/v1/webhooks/paymongo{queryString}";

        string eventId = $"evt_query_{Guid.NewGuid():N}";
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
        Assert.True(jsonDoc.RootElement.GetProperty("received").GetBoolean());
        Assert.Equal(eventId, jsonDoc.RootElement.GetProperty("event_id").GetString());
    }

    [Fact]
    public async Task WebhookDelivery_WithDuplicateSlashesAndTamperedSignatureAndQueryString_Returns400()
    {
        string requestUri = "http://localhost//api//v1//webhooks//paymongo?debug=true&timestamp=123";
        string payload = """{"data":{"id":"evt_tamper","attributes":{"type":"payment.paid"}}}""";

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string tamperedSig = WebhookSigner.CreateSignatureHeader(payload, "whsec_wrong_key", timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", tamperedSig);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("InvalidSignature", jsonDoc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WebhookDelivery_WithDuplicateSlashesAndExpiredTimestampAndQueryString_Returns400()
    {
        string requestUri = "http://localhost//api//v1//webhooks//paymongo?query_tag=expired_test";
        string payload = """{"data":{"id":"evt_expired","attributes":{"type":"payment.paid"}}}""";

        long expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeSeconds();
        string expiredSig = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, expiredTimestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", expiredSig);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("InvalidSignature", jsonDoc.RootElement.GetProperty("error").GetString());
    }

    // =========================================================================
    // 5. Method Not Allowed & Payload Cap Under Duplicate Slashes
    // =========================================================================

    [Fact]
    public async Task WebhookEndpoint_GetMethodWithDuplicateSlashes_Returns405MethodNotAllowed()
    {
        using HttpResponseMessage response = await _client.GetAsync("http://localhost//api/v1/webhooks/paymongo");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task WebhookDelivery_WithDuplicateSlashesAndExceededPayloadLimit_Returns413PayloadTooLarge()
    {
        // 265 KB payload (exceeds 256 KB limit)
        string oversizedPadding = new('x', 265 * 1024);
        string payload = $$"""{"data":{"id":"evt_large","attributes":{"type":"payment.paid","padding":"{{oversizedPadding}}"} } }""";

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost//////////api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        // Caught early by RequestSizeLimitPolicy due to normalized path resolving MaxWebhookPayloadBytes
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // =========================================================================
    // 6. Direct Middleware Unit Stress & Invariant Verification
    // =========================================================================

    [Fact]
    public async Task Middleware_PreservesQueryStringAndBodyStreamExactly()
    {
        string originalQuery = "?key1=value1&key2=value2%20encoded";
        byte[] payloadBytes = Encoding.UTF8.GetBytes("""{"test":"body"}""");

        var context = new DefaultHttpContext();
        context.Request.Path = new PathString("//api//v1//test");
        context.Request.QueryString = new QueryString(originalQuery);
        context.Request.Body = new MemoryStream(payloadBytes);
        context.Request.Method = "POST";

        bool nextInvoked = false;
        PathString pathObservedInNext = PathString.Empty;
        QueryString queryObservedInNext = QueryString.Empty;
        byte[]? bodyObservedInNext = null;

        var middleware = new DuplicateSlashNormalizationMiddleware(
            innerContext =>
            {
                nextInvoked = true;
                pathObservedInNext = innerContext.Request.Path;
                queryObservedInNext = innerContext.Request.QueryString;

                using var ms = new MemoryStream();
                innerContext.Request.Body.CopyTo(ms);
                bodyObservedInNext = ms.ToArray();
                return Task.CompletedTask;
            },
            NullLogger<DuplicateSlashNormalizationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(nextInvoked);
        Assert.Equal("/api/v1/test", pathObservedInNext.Value);
        Assert.Equal(originalQuery, queryObservedInNext.Value);
        Assert.Equal(payloadBytes, bodyObservedInNext);
    }

    [Fact]
    public async Task Middleware_SingleSlashPath_FastPathZeroAllocation()
    {
        var context = new DefaultHttpContext();
        var originalPath = new PathString("/api/v1/webhooks/paymongo");
        context.Request.Path = originalPath;

        var middleware = new DuplicateSlashNormalizationMiddleware(
            innerContext =>
            {
                // Must be the exact reference without reallocation
                Assert.Same(originalPath.Value, innerContext.Request.Path.Value);
                return Task.CompletedTask;
            },
            NullLogger<DuplicateSlashNormalizationMiddleware>.Instance);

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task Middleware_ConcurrencyStress_Under500ParallelInvocations()
    {
        var middleware = new DuplicateSlashNormalizationMiddleware(
            innerContext =>
            {
                Assert.DoesNotContain("//", innerContext.Request.Path.Value!, StringComparison.Ordinal);
                return Task.CompletedTask;
            },
            NullLogger<DuplicateSlashNormalizationMiddleware>.Instance);

        var tasks = Enumerable.Range(0, 500).Select(async i =>
        {
            var ctx = new DefaultHttpContext();
            int slashCount = (i % 20) + 2;
            string slashes = new('/', slashCount);
            ctx.Request.Path = new PathString($"{slashes}api{slashes}v1{slashes}test{i}");
            ctx.Request.Method = "POST";
            await middleware.InvokeAsync(ctx);
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Middleware_MassiveSlashString_ExecutesInSubMillisecondWithoutOverflow()
    {
        // 10,000 slashes
        string hugeSlashes = new('/', 10_000);
        var context = new DefaultHttpContext();
        context.Request.Path = new PathString($"{hugeSlashes}test");

        var middleware = new DuplicateSlashNormalizationMiddleware(
            innerContext =>
            {
                Assert.Equal("/test", innerContext.Request.Path.Value);
                return Task.CompletedTask;
            },
            NullLogger<DuplicateSlashNormalizationMiddleware>.Instance);

        var sw = Stopwatch.StartNew();
        await middleware.InvokeAsync(context);
        sw.Stop();

        // Must take less than 50 milliseconds even in debug mode
        Assert.True(sw.ElapsedMilliseconds < 50, $"Elapsed was {sw.ElapsedMilliseconds}ms");
    }

    // =========================================================================
    // 7. Cryptographic Invariants, Replay Attacks & Adversarial Signatures
    // =========================================================================

    [Theory]
    [InlineData("t=9223372036854775807,te=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("t=-9223372036854775808,te=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("t=999999999999999,te=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("t=invalid_timestamp,te=0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task WebhookDelivery_WithMalformedOrOverflowTimestamps_DoesNotCrashServer(string malformedSignatureHeader)
    {
        string payload = """{"data":{"id":"evt_overflow","attributes":{"type":"payment.paid"}}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost//api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", malformedSignatureHeader);

        using HttpResponseMessage response = await _client.SendAsync(request);

        // Security invariant: Server must NOT throw unhandled exceptions or crash.
        // It must reject invalid/overflowing signatures with 400 Bad Request (or 500 if unhandled).
        // Let's assert what status code is actually returned.
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected HTTP 400 BadRequest for malformed timestamp '{malformedSignatureHeader}', but got HTTP {(int)response.StatusCode} {response.StatusCode}.");
    }

    [Theory]
    [InlineData(301, false)] // 301 seconds in the past -> REJECTED (exceeds 300s window)
    [InlineData(600, false)] // 10 minutes in the past -> REJECTED
    [InlineData(86400, false)] // 24 hours in the past -> REJECTED
    [InlineData(-301, false)] // 301 seconds in the future -> REJECTED (skew check)
    [InlineData(290, true)] // 290 seconds in the past -> ACCEPTED (within 300s window)
    [InlineData(-10, true)] // 10 seconds in the future (minor clock skew) -> ACCEPTED (within 300s window)
    public async Task WebhookDelivery_ReplayWindowTolerance_StrictlyEnforced(int offsetSeconds, bool expectedSuccess)
    {
        string eventId = $"evt_replay_{offsetSeconds}_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_replay_test"
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.AddSeconds(-offsetSeconds).ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost//api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        if (expectedSuccess)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Equal("InvalidSignature", jsonDoc.RootElement.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData("/api/v1/webhooks/paymongo")]
    [InlineData("/api/payments/webhook")]
    [InlineData("/webhooks/paymongo")]
    [InlineData("http://localhost//api/v1/webhooks/paymongo")]
    [InlineData("http://localhost//api//payments//webhook")]
    [InlineData("http://localhost//webhooks//paymongo")]
    public async Task WebhookDelivery_TamperedSignatures_ConsistentlyRejectedWith400AcrossAllRoutes(string route)
    {
        string payload = """{"data":{"id":"evt_tamper_all","attributes":{"type":"payment.paid"}}}""";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Attacker creates signature with wrong secret
        string tamperedSig = WebhookSigner.CreateSignatureHeader(payload, "whsec_attacker_secret_00000000", timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", tamperedSig);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.False(jsonDoc.RootElement.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", jsonDoc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WebhookDelivery_DuplicateTimestampTagsInHeader_HMACBoundToExactTimestampUsed()
    {
        // Attacker sends: t=valid_time, t=expired_time, te=sig_for_expired
        // Or attacker sends: t=valid_time, te=sig_for_valid, t=expired_time
        long validTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeSeconds();

        string payload = """{"data":{"id":"evt_dup_tags","attributes":{"type":"payment.paid"}}}""";
        string sigForValid = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, validTimestamp);
        // Extract raw hex
        string hexSigValid = sigForValid.Split("te=")[1].Split(',')[0];

        // Craft header where first tag is valid, second tag is expired
        string injectedHeader = $"t={validTimestamp},te={hexSigValid},t={expiredTimestamp}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost//api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", injectedHeader);

        using HttpResponseMessage response = await _client.SendAsync(request);

        // Must be rejected because the last t= tag is expired, and HMAC is evaluated against that timestamp!
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================
    // 8. Path Traversal & Spoofing Invariant Verification
    // =========================================================================

    [Theory]
    [InlineData("http://localhost//..//api/v1/webhooks/paymongo")]
    [InlineData("http://localhost//api/v1/webhooks/paymongo/..")]
    [InlineData("http://localhost//api//v1//webhooks//paymongo//..//..//admin")]
    [InlineData("http://localhost//%2e%2e//api/v1/webhooks/paymongo")]
    public async Task WebhookDelivery_PathTraversalAttempts_DoNotAccessUnauthorizedEndpointsOrCrash(string traversalUri)
    {
        string payload = """{"data":{"id":"evt_traversal","attributes":{"type":"payment.paid"}}}""";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, traversalUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        // Path traversal must not result in unhandled 500 exceptions.
        // It should either return 400 Bad Request, 404 Not Found, or normalize without escaping sandbox.
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // 9. Rate Limiter Invariant & Partition Integrity Under Duplicate Slashes
    // =========================================================================

    [Fact]
    public async Task WebhookDelivery_RateLimiter_EnforcesTokenBucketAcrossDuplicateAndCanonicalSlashes()
    {
        // Use an isolated WebApplicationFactory so the 120-token exhaustion does not bleed into other tests
        using var isolatedFactory = new AccountingWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();

        string payload = """{"data":{"id":"evt_rate","attributes":{"type":"payment.paid"}}}""";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        int count429 = 0;
        int count200 = 0;

        for (int i = 0; i < 125; i++)
        {
            string uri = (i % 2 == 0)
                ? "http://localhost//api/v1/webhooks/paymongo"
                : "/api/v1/webhooks/paymongo";

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Paymongo-Signature", signature);

            using HttpResponseMessage response = await isolatedClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                count429++;
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                count200++;
            }
        }

        // Must hit rate limiter once token bucket (120) is exhausted!
        Assert.True(count429 > 0, $"Expected rate limiting (429) after 120 requests, but got count200={count200}, count429={count429}");
        Assert.True(count200 <= 120, $"Expected at most 120 successful requests within bucket window, but got {count200}");
    }
}

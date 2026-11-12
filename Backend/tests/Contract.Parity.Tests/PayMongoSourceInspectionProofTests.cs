using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Payments.Application.Ports;
using Rge.Payments.Infrastructure.PayMongo;
using Xunit;

namespace Rge.Contract.Parity.Tests;

/// <summary>
/// Deterministic in-memory HTTP router for testing PayMongo API interactions without external mocking frameworks.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode StatusCode, string JsonBody)> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public Collection<HttpRequestMessage> CapturedRequests { get; } = [];

    public MockHttpMessageHandler MapRoute(string relativePath, HttpStatusCode statusCode, string jsonBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(jsonBody);

        _routes[relativePath.TrimStart('/')] = (statusCode, jsonBody);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CapturedRequests.Add(request);

        string path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? string.Empty;
        if (path.StartsWith("v1/", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(3);
        }

        if (_routes.TryGetValue(path, out var route))
        {
            var response = new HttpResponseMessage(route.StatusCode)
            {
                Content = new StringContent(route.JsonBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                $$"""{"errors":[{"code":"resource_not_found","detail":"Route '{{path}}' not mapped in mock handler."}]}""",
                Encoding.UTF8,
                "application/json")
        });
    }
}

public sealed class PayMongoSourceInspectionProofTests
{
    private const string ActiveAwaitingNextActionSessionJson = """
    {
      "data": {
        "id": "cs_test_active_awaiting_action",
        "type": "checkout_session",
        "attributes": {
          "checkout_url": "https://checkout.paymongo.com/cs_test_active_awaiting_action",
          "status": "active",
          "payment_method_used": "gcash",
          "payments": [],
          "payment_intent": {
            "id": "pi_test_gcash_123",
            "type": "payment_intent",
            "attributes": {
              "status": "awaiting_next_action",
              "payment_method_id": "pm_test_gcash_method",
              "next_action": {
                "type": "redirect",
                "redirect": {
                  "url": "https://checkout.paymongo.com/sources/src_ba7hPZAKRbxYVCFXT7LFao9q/authenticate",
                  "return_url": "http://localhost:3000/payment/return"
                }
              }
            }
          }
        }
      }
    }
    """;

    private const string SourceExpiredJson = """
    {
      "data": {
        "id": "src_ba7hPZAKRbxYVCFXT7LFao9q",
        "type": "source",
        "attributes": {
          "amount": 250000,
          "currency": "PHP",
          "status": "expired",
          "type": "gcash",
          "created_at": 1730995200,
          "updated_at": 1730995500
        }
      }
    }
    """;

    private const string SourceFailedJson = """
    {
      "data": {
        "id": "src_ba7hPZAKRbxYVCFXT7LFao9q",
        "type": "source",
        "attributes": {
          "amount": 250000,
          "currency": "PHP",
          "status": "failed",
          "type": "gcash",
          "created_at": 1730995200,
          "updated_at": 1730995500
        }
      }
    }
    """;

    private const string SourcePendingJson = """
    {
      "data": {
        "id": "src_ba7hPZAKRbxYVCFXT7LFao9q",
        "type": "source",
        "attributes": {
          "amount": 250000,
          "currency": "PHP",
          "status": "pending",
          "type": "gcash",
          "created_at": 1730995200,
          "updated_at": 1730995210
        }
      }
    }
    """;

    private const string SourceChargeableJson = """
    {
      "data": {
        "id": "src_ba7hPZAKRbxYVCFXT7LFao9q",
        "type": "source",
        "attributes": {
          "amount": 250000,
          "currency": "PHP",
          "status": "chargeable",
          "type": "gcash",
          "created_at": 1730995200,
          "updated_at": 1730995210
        }
      }
    }
    """;

    private const string SourceCancelledJson = """
    {
      "data": {
        "id": "src_ba7hPZAKRbxYVCFXT7LFao9q",
        "type": "source",
        "attributes": {
          "amount": 250000,
          "currency": "PHP",
          "status": "cancelled",
          "type": "gcash",
          "created_at": 1730995200,
          "updated_at": 1730995300
        }
      }
    }
    """;

    private const string FallbackPiCheckoutSessionJson = """
    {
      "data": {
        "id": "cs_test_fallback_pi",
        "type": "checkout_session",
        "attributes": {
          "checkout_url": "https://checkout.paymongo.com/cs_test_fallback_pi",
          "status": "active",
          "payments": [],
          "payment_intent": {
            "id": "pi_fallback_error_999",
            "type": "payment_intent",
            "attributes": {
              "status": "awaiting_next_action"
            }
          }
        }
      }
    }
    """;

    private const string PaymentIntentWithErrorJson = """
    {
      "data": {
        "id": "pi_fallback_error_999",
        "type": "payment_intent",
        "attributes": {
          "status": "awaiting_payment_method",
          "last_payment_error": {
            "code": "insufficient_funds",
            "detail": "Customer has insufficient funds in GCash account.",
            "failed_code": "insufficient_funds",
            "failed_message": "Customer has insufficient funds in GCash account."
          }
        }
      }
    }
    """;

    private static (PayMongoClient Client, HttpClient HttpClient) CreateClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.paymongo.com/v1/")
        };

        var options = Options.Create(new PayMongoOptions
        {
            BaseUrl = "https://api.paymongo.com/v1/",
            SecretKey = "sk_test_authentic_proof_key_12345",
            SuccessUrl = "http://localhost:3000/payment/return",
            CancelUrl = "http://localhost:3000/checkout"
        });

        return (new PayMongoClient(httpClient, options, NullLogger<PayMongoClient>.Instance), httpClient);
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WhenUnderlyingSourceExpired_MaintainsActiveSessionWithRecoverableCode()
    {
        // Arrange
        const string csId = "cs_test_active_awaiting_action";
        const string srcId = "src_ba7hPZAKRbxYVCFXT7LFao9q";

        using var handler = new MockHttpMessageHandler();
        handler
            .MapRoute($"checkout_sessions/{csId}", HttpStatusCode.OK, ActiveAwaitingNextActionSessionJson)
            .MapRoute($"sources/{srcId}", HttpStatusCode.OK, SourceExpiredJson);

        var (client, httpClient) = CreateClient(handler);
        using (httpClient)
        {
            // Act
            var result = await client.GetCheckoutSessionStatusAsync(csId);

            // Assert: Active Checkout Session must NOT be prematurely killed when a child QR/source expires
            Assert.True(result.IsSuccess);
            Assert.Equal("awaiting_next_action", result.Value.Status);
            Assert.Equal("qrph_source_expired", result.Value.FailureCode);
            Assert.Contains(srcId, result.Value.FailureMessage, StringComparison.OrdinalIgnoreCase);

            // Verify both HTTP endpoints were invoked in order
            Assert.Equal(2, handler.CapturedRequests.Count);
            Assert.Equal($"/v1/checkout_sessions/{csId}", handler.CapturedRequests[0].RequestUri?.AbsolutePath);
            Assert.Equal($"/v1/sources/{srcId}", handler.CapturedRequests[1].RequestUri?.AbsolutePath);
        }
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WhenUnderlyingSourceFailed_OverridesStatusToFailed()
    {
        // Arrange
        const string csId = "cs_test_active_awaiting_action";
        const string srcId = "src_ba7hPZAKRbxYVCFXT7LFao9q";

        using var handler = new MockHttpMessageHandler();
        handler
            .MapRoute($"checkout_sessions/{csId}", HttpStatusCode.OK, ActiveAwaitingNextActionSessionJson)
            .MapRoute($"sources/{srcId}", HttpStatusCode.OK, SourceFailedJson);

        var (client, httpClient) = CreateClient(handler);
        using (httpClient)
        {
            // Act
            var result = await client.GetCheckoutSessionStatusAsync(csId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("failed", result.Value.Status);
            Assert.Equal("source_failed", result.Value.FailureCode);
            Assert.Contains("failed authorization", result.Value.FailureMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WhenUnderlyingSourcePending_MaintainsAwaitingNextAction()
    {
        // Arrange
        const string csId = "cs_test_active_awaiting_action";
        const string srcId = "src_ba7hPZAKRbxYVCFXT7LFao9q";

        using var handler = new MockHttpMessageHandler();
        handler
            .MapRoute($"checkout_sessions/{csId}", HttpStatusCode.OK, ActiveAwaitingNextActionSessionJson)
            .MapRoute($"sources/{srcId}", HttpStatusCode.OK, SourcePendingJson);

        var (client, httpClient) = CreateClient(handler);
        using (httpClient)
        {
            // Act
            var result = await client.GetCheckoutSessionStatusAsync(csId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("awaiting_next_action", result.Value.Status);
            Assert.Null(result.Value.FailureCode);
        }
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WhenUnderlyingSourceCancelled_OverridesStatusToFailed()
    {
        // Arrange
        const string csId = "cs_test_active_awaiting_action";
        const string srcId = "src_ba7hPZAKRbxYVCFXT7LFao9q";

        using var handler = new MockHttpMessageHandler();
        handler
            .MapRoute($"checkout_sessions/{csId}", HttpStatusCode.OK, ActiveAwaitingNextActionSessionJson)
            .MapRoute($"sources/{srcId}", HttpStatusCode.OK, SourceCancelledJson);

        var (client, httpClient) = CreateClient(handler);
        using (httpClient)
        {
            // Act
            var result = await client.GetCheckoutSessionStatusAsync(csId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("failed", result.Value.Status);
            Assert.Equal("source_cancelled", result.Value.FailureCode);
        }
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WhenSourceNotFoundOr404_FallsBackGracefullyWithoutCrashing()
    {
        // Arrange
        const string csId = "cs_test_active_awaiting_action";
        const string srcId = "src_ba7hPZAKRbxYVCFXT7LFao9q";

        using var handler = new MockHttpMessageHandler();
        handler
            .MapRoute($"checkout_sessions/{csId}", HttpStatusCode.OK, ActiveAwaitingNextActionSessionJson)
            .MapRoute($"sources/{srcId}", HttpStatusCode.NotFound, """{"errors":[{"code":"resource_not_found"}]}""");

        var (client, httpClient) = CreateClient(handler);
        using (httpClient)
        {
            // Act
            var result = await client.GetCheckoutSessionStatusAsync(csId);

            // Assert: gracefully falls back to baseline awaiting_next_action
            Assert.True(result.IsSuccess);
            Assert.Equal("awaiting_next_action", result.Value.Status);
        }
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WhenPaymentIntentHasError_OverridesStatusToFailed()
    {
        // Arrange
        const string csId = "cs_test_fallback_pi";
        const string piId = "pi_fallback_error_999";

        using var handler = new MockHttpMessageHandler();
        handler
            .MapRoute($"checkout_sessions/{csId}", HttpStatusCode.OK, FallbackPiCheckoutSessionJson)
            .MapRoute($"payment_intents/{piId}", HttpStatusCode.OK, PaymentIntentWithErrorJson);

        var (client, httpClient) = CreateClient(handler);
        using (httpClient)
        {
            // Act
            var result = await client.GetCheckoutSessionStatusAsync(csId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("failed", result.Value.Status);
            Assert.Equal("insufficient_funds", result.Value.FailureCode);
            Assert.Contains("insufficient funds", result.Value.FailureMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WhenUnderlyingSourceChargeable_MaintainsAwaitingNextAction()
    {
        // Arrange
        const string csId = "cs_test_active_awaiting_action";
        const string srcId = "src_ba7hPZAKRbxYVCFXT7LFao9q";

        using var handler = new MockHttpMessageHandler();
        handler
            .MapRoute($"checkout_sessions/{csId}", HttpStatusCode.OK, ActiveAwaitingNextActionSessionJson)
            .MapRoute($"sources/{srcId}", HttpStatusCode.OK, SourceChargeableJson);

        var (client, httpClient) = CreateClient(handler);
        using (httpClient)
        {
            // Act
            var result = await client.GetCheckoutSessionStatusAsync(csId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("awaiting_next_action", result.Value.Status);
            Assert.Null(result.Value.FailureCode);
        }
    }
}

#pragma warning disable CA1849 // Call async methods when in an async method
#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Api.Webhooks;
using Rge.Payments.Application.Ports;
using Rge.Payments.Application.Webhooks.ReceivePayMongoWebhook;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.PayMongo;
using Rge.Payments.Infrastructure.Persistence.Repositories;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Comprehensive end-to-end integration test suite verifying PayMongo webhook resilience,
/// URL path normalization (consecutive slashes), signature header case insensitivity,
/// request stream buffering and rewinding, safe 200 OK acknowledgments, lifecycle transitions,
/// and cryptographic HMAC rejection of tampered/expired signatures.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PayMongoWebhookComprehensiveTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private const string WebhookSecret = "whsec_test_mock_secret_key_12345678";
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PayMongoWebhookComprehensiveTests(AccountingWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        DrainOutbox();
    }

    private void DrainOutbox()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var worker = scope.ServiceProvider.GetService<OutboxWorker>();
            if (worker is not null)
            {
                while (worker.ProcessNextBatchOnceAsync().GetAwaiter().GetResult() > 0)
                {
                }
            }
        }
        catch
        {
            // Best effort test isolation cleanup
        }
    }

    private async Task DrainOutboxAsync()
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var worker = scope.ServiceProvider.GetService<OutboxWorker>();
            if (worker is not null)
            {
                while (await worker.ProcessNextBatchOnceAsync().ConfigureAwait(false) > 0)
                {
                }
            }
        }
        catch
        {
            // Best effort test isolation cleanup
        }
    }

    // =========================================================================
    // Acceptance Criteria (a): Path Normalization
    // Incoming requests with consecutive slashes (//api/v1/webhooks/paymongo,
    // ///api///v1///webhooks///paymongo) normalize and match routes without HTTP 404;
    // standard single-slash URLs continue to route correctly without regression.
    // =========================================================================

    [Theory]
    [InlineData("/api/v1/webhooks/paymongo")]
    [InlineData("//api/v1/webhooks/paymongo")]
    [InlineData("///api/v1/webhooks/paymongo")]
    [InlineData("////api/v1/webhooks/paymongo")]
    [InlineData("/api//v1//webhooks//paymongo")]
    [InlineData("//api//v1//webhooks//paymongo")]
    [InlineData("///api///v1///webhooks///paymongo")]
    [InlineData("/api/v1/webhooks/paymongo//")]
    [InlineData("//api/v1/webhooks/paymongo//")]
    [InlineData("/api/payments/webhook")]
    [InlineData("//api/payments/webhook")]
    [InlineData("/webhooks/paymongo")]
    [InlineData("//webhooks/paymongo")]
    public async Task Webhook_PathNormalization_ConsecutiveLeadingAndInterleavedSlashes_RoutesWithout404(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string eventId = $"evt_norm_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_norm_test",
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

        string requestUri = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"http://localhost{path}";

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
    }

    [Fact]
    public async Task Webhook_PathNormalization_StandardRoutes_PreserveBehaviorWithoutRegression()
    {
        using HttpResponseMessage response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // Acceptance Criteria (b): Signature Header Case Insensitivity
    // Headers paymongo-signature, PAYMONGO-SIGNATURE, PayMongo-Signature,
    // Paymongo-Signature all authenticate successfully.
    // =========================================================================

    [Theory]
    [InlineData("paymongo-signature")]
    [InlineData("PAYMONGO-SIGNATURE")]
    [InlineData("PayMongo-Signature")]
    [InlineData("Paymongo-Signature")]
    [InlineData("pAyMoNgO-sIgNaTuRe")]
    public async Task Webhook_SignatureHeaderCaseInsensitivity_AllCasingsAccepted(string headerName)
    {
        ArgumentNullException.ThrowIfNull(headerName);
        string eventId = $"evt_case_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_case_test",
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(headerName, signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal(eventId, root.GetProperty("event_id").GetString());
    }

    // =========================================================================
    // Acceptance Criteria (c): Stream Buffering and Rewind
    // Webhook request body can be read multiple times via request stream
    // buffering and rewinding (Body.Position == 0).
    // =========================================================================

    [Fact]
    public async Task Webhook_StreamBuffering_CanBeReadMultipleTimesAndRewound()
    {
        string eventId = $"evt_buffer_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_buffering_test",
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

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/webhooks/paymongo";
        httpContext.Request.Method = "POST";
        httpContext.Request.Headers["Paymongo-Signature"] = signature;
        httpContext.Request.Body = new MemoryStream(payloadBytes);
        httpContext.Request.ContentLength = payloadBytes.Length;
        httpContext.Request.ContentType = "application/json";

        // Resolve dependencies
        using var scope = _factory.Services.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<PayMongoSignatureVerifier>();
        var handler = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PayMongoWebhookEndpoint>>();

        MethodInfo? method = typeof(PayMongoWebhookEndpoint).GetMethod(
            "HandleWebhookAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var taskObj = method.Invoke(null, [httpContext, verifier, handler, logger, CancellationToken.None]);
        Assert.NotNull(taskObj);

        var result = await (Task<IResult>)taskObj;
        Assert.NotNull(result);

        // Verify stream state after endpoint execution
        Assert.True(httpContext.Request.Body.CanSeek, "Request body stream must be seekable after buffering.");
        Assert.Equal(0, httpContext.Request.Body.Position);

        // Read stream a second time to prove rewinding allows multiple reads
        using (var secondReader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            string secondReadPayload = await secondReader.ReadToEndAsync();
            Assert.Equal(payload, secondReadPayload);
        }

        // Verify stream position can be rewound again and read a third time
        httpContext.Request.Body.Position = 0;
        using (var thirdReader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            string thirdReadPayload = await thirdReader.ReadToEndAsync();
            Assert.Equal(payload, thirdReadPayload);
        }
    }

    [Fact]
    public async Task Webhook_StreamBuffering_WhenInitialPositionNonZero_RewindsAndReadsFullBody()
    {
        string eventId = $"evt_nonzero_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_nonzero_test",
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

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/webhooks/paymongo";
        httpContext.Request.Method = "POST";
        httpContext.Request.Headers["Paymongo-Signature"] = signature;
        var stream = new MemoryStream(payloadBytes);
        // Simulate an upstream component that partially read 20 bytes and left Position at 20
        stream.Position = 20;
        httpContext.Request.Body = stream;
        httpContext.Request.ContentLength = payloadBytes.Length;
        httpContext.Request.ContentType = "application/json";

        using var scope = _factory.Services.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<PayMongoSignatureVerifier>();
        var handler = scope.ServiceProvider.GetRequiredService<ReceivePayMongoWebhookHandler>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PayMongoWebhookEndpoint>>();

        MethodInfo? method = typeof(PayMongoWebhookEndpoint).GetMethod(
            "HandleWebhookAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var taskObj = method.Invoke(null, [httpContext, verifier, handler, logger, CancellationToken.None]);
        Assert.NotNull(taskObj);

        var result = await (Task<IResult>)taskObj;
        Assert.NotNull(result);

        // Verify stream was rewound to 0 before reading so signature verification succeeded
        Assert.Equal(0, httpContext.Request.Body.Position);

        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        string readPayload = await reader.ReadToEndAsync();
        Assert.Equal(payload, readPayload);
    }

    // =========================================================================
    // Acceptance Criteria (d): Safe 200 OK Acknowledgments
    // Webhook requests with non-matching orders/attempts or unhandled event
    // types (e.g., customer.created, source.cancelled) return HTTP 200 OK
    // with received: true and processing_status: "ignored".
    // =========================================================================

    [Fact]
    public async Task Webhook_Safe200OkAcknowledgment_NonMatchingOrdersAndAttempts_Returns200Ignored()
    {
        string eventId = $"evt_nonmatch_{Guid.NewGuid():N}";
        string unmappedSessionId = $"cs_unmapped_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "{{unmappedSessionId}}",
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
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
        Assert.Equal("ignored", root.GetProperty("processing_status").GetString());
    }

    [Theory]
    [InlineData("customer.created")]
    [InlineData("source.cancelled")]
    [InlineData("payout.paid")]
    [InlineData("invoice.created")]
    public async Task Webhook_Safe200OkAcknowledgment_UnhandledEventTypes_Returns200Ignored(string unhandledEventType)
    {
        ArgumentNullException.ThrowIfNull(unhandledEventType);
        string eventId = $"evt_unhandled_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "{{unhandledEventType}}",
              "data": {
                "id": "item_99999",
                "attributes": {
                  "status": "active"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
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
        Assert.Equal(unhandledEventType, root.GetProperty("event_type").GetString());
        Assert.Equal("ignored", root.GetProperty("processing_status").GetString());
    }

    // =========================================================================
    // Acceptance Criteria (e): Payment Lifecycle Events
    // All handled events (checkout_session.payment.paid, payment.paid,
    // payment.failed, qrph.expired, payment.refunded, payment.refund.updated)
    // process cleanly and perform expected state transitions.
    // =========================================================================

    [Fact]
    public async Task Webhook_Lifecycle_CheckoutSessionPaymentPaid_TransitionsAttemptAndOrderToPaid()
    {
        string attemptId = $"att_cs_paid_{Guid.NewGuid():N}";
        string providerSessionId = $"cs_life_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerSessionId,
            orderNumber: "ORD-CS-PAID-001",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_cs_paid_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "{{providerSessionId}}",
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());

        // Verify entity state transitions
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        Order? updatedOrder = await orderRepo.GetByIdAsync(order.Id);
        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedOrder);
        Assert.Equal("paid", updatedOrder.Status);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);

        // Drain outbox so messages from this test are completed and don't leak into subsequent test suites
        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Lifecycle_PaymentPaid_TransitionsAttemptAndOrderToPaid()
    {
        string attemptId = $"att_pay_paid_{Guid.NewGuid():N}";
        string providerSessionId = $"cs_paypaid_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerSessionId,
            orderNumber: "ORD-PAY-PAID-002",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_pay_paid_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_test_trans_001",
                "attributes": {
                  "status": "paid",
                  "checkout_session_id": "{{providerSessionId}}"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        Order? updatedOrder = await orderRepo.GetByIdAsync(order.Id);
        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedOrder);
        Assert.Equal("paid", updatedOrder.Status);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);

        // Drain outbox so messages from this test are completed and don't leak into subsequent test suites
        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Lifecycle_PaymentFailed_TransitionsAttemptToFailedAndPreservesOrderPending()
    {
        string attemptId = $"att_failed_{Guid.NewGuid():N}";
        string providerSessionId = $"cs_failed_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerSessionId,
            orderNumber: "ORD-FAILED-003",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_failed_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "{{providerSessionId}}",
                "attributes": {
                  "status": "failed",
                  "failed_code": "card_declined",
                  "failed_message": "Card was declined by bank."
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        Order? updatedOrder = await orderRepo.GetByIdAsync(order.Id);
        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedOrder);
        Assert.Equal("pending", updatedOrder.Status);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Failed, updatedAttempt.Status);
        Assert.Equal("card_declined", updatedAttempt.FailureCode);
        Assert.Equal("Card was declined by bank.", updatedAttempt.FailureMessage);
    }

    [Fact]
    public async Task Webhook_Lifecycle_QrphExpired_TransitionsAttemptToExpiredAndInvokesGatewayExpiration()
    {
        string attemptId = $"att_expired_{Guid.NewGuid():N}";
        string providerSessionId = $"cs_expired_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerSessionId,
            orderNumber: "ORD-EXPIRED-004",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_expired_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "qrph.expired",
              "data": {
                "id": "{{providerSessionId}}",
                "attributes": {
                  "status": "expired"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Expired, updatedAttempt.Status);

        // Verify gateway ExpireCheckoutSessionAsync was called
        Assert.Contains(providerSessionId, _factory.Gateway.ExpiredSessionCalls);
    }

    [Fact]
    public async Task Webhook_Lifecycle_PaymentRefunded_TransitionsOrderToRefunded()
    {
        string attemptId = $"att_refunded_{Guid.NewGuid():N}";
        string providerPaymentId = $"pay_refund_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerPaymentId,
            orderNumber: "ORD-REFUNDED-005",
            initialOrderStatus: "paid",
            initialAttemptStatus: PaymentAttemptStatus.Succeeded);

        string eventId = $"evt_refunded_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.refunded",
              "data": {
                "id": "ref_trans_001",
                "attributes": {
                  "payment_id": "{{providerPaymentId}}",
                  "amount": 150000,
                  "status": "refunded"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        Order? updatedOrder = await orderRepo.GetByIdAsync(order.Id);

        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Refunded, updatedOrder.Status);
    }

    [Fact]
    public async Task Webhook_Lifecycle_PaymentRefundUpdated_TransitionsOrderToRefunded()
    {
        string attemptId = $"att_refupd_{Guid.NewGuid():N}";
        string providerPaymentId = $"pay_refupd_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerPaymentId,
            orderNumber: "ORD-REFUPD-006",
            initialOrderStatus: "paid",
            initialAttemptStatus: PaymentAttemptStatus.Succeeded);

        string eventId = $"evt_refupd_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.refund.updated",
              "data": {
                "id": "ref_trans_002",
                "attributes": {
                  "payment_id": "{{providerPaymentId}}",
                  "amount": 150000,
                  "status": "refunded"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        Order? updatedOrder = await orderRepo.GetByIdAsync(order.Id);

        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Refunded, updatedOrder.Status);
    }

    // =========================================================================
    // Acceptance Criteria (f): Tampered and Expired Signatures
    // Tampered HMAC-SHA256 signature and expired timestamps (> 5 min) correctly
    // return HTTP 400 Bad Request with InvalidSignature.
    // =========================================================================

    [Fact]
    public async Task Webhook_TamperedHmacSignature_Returns400BadRequest()
    {
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
        // Generate tampered signature
        string tamperedSignature = WebhookSigner.CreateSignatureHeader(
            payload,
            WebhookSecret,
            timestamp,
            tamper: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
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

    [Fact]
    public async Task Webhook_SignatureCreatedWithWrongSecret_Returns400BadRequest()
    {
        string eventId = $"evt_wrongsecret_{Guid.NewGuid():N}";
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
        string signatureWithAttackerSecret = WebhookSigner.CreateSignatureHeader(
            payload,
            "whsec_attacker_unauthorized_key_9999",
            timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signatureWithAttackerSecret);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(6)]    // 6 minutes (> 5 min tolerance)
    [InlineData(10)]   // 10 minutes
    [InlineData(60)]   // 1 hour
    [InlineData(1440)] // 24 hours
    public async Task Webhook_ExpiredTimestamp_Returns400BadRequest(int minutesInPast)
    {
        string eventId = $"evt_expired_{minutesInPast}_{Guid.NewGuid():N}";
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

        long expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-minutesInPast).ToUnixTimeSeconds();
        string expiredSignature = WebhookSigner.CreateSignatureHeader(
            payload,
            WebhookSecret,
            expiredTimestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
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

    [Fact]
    public async Task Webhook_FutureTimestampOutsideTolerance_Returns400BadRequest()
    {
        string eventId = $"evt_future_{Guid.NewGuid():N}";
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

        // 10 minutes into future (> 5 min tolerance)
        long futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        string futureSignature = WebhookSigner.CreateSignatureHeader(
            payload,
            WebhookSecret,
            futureTimestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", futureSignature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Webhook_TamperedPayloadBody_Returns400BadRequest()
    {
        string originalPayload = """{"data":{"id":"evt_body_orig","attributes":{"type":"payment.paid"}}}""";
        string alteredPayload = """{"data":{"id":"evt_body_altered","attributes":{"type":"payment.paid"}}}""";

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signatureForOriginal = WebhookSigner.CreateSignatureHeader(
            originalPayload,
            WebhookSecret,
            timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(alteredPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signatureForOriginal);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Webhook_MissingSignatureHeader_Returns400BadRequest()
    {
        string eventId = $"evt_nosig_{Guid.NewGuid():N}";
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
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

    [Theory]
    [InlineData("invalid_signature_format")]
    [InlineData("t=12345678")]
    [InlineData("te=deadbeef")]
    [InlineData("t=not_a_number,te=deadbeef")]
    public async Task Webhook_MalformedSignatureHeader_Returns400BadRequest(string malformedHeader)
    {
        ArgumentNullException.ThrowIfNull(malformedHeader);
        string eventId = $"evt_malformed_{Guid.NewGuid():N}";
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", malformedHeader);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("received").GetBoolean());
        Assert.Equal("InvalidSignature", root.GetProperty("error").GetString());
    }

    // =========================================================================
    // Acceptance Criteria: Milestone M1 Step b Deep Webhook Correlation & State Locking
    // =========================================================================

    [Fact]
    public async Task Webhook_Correlation_ViaMetadataAttemptId_ResolvesAndTransitionsToSucceeded()
    {
        string attemptId = $"att_meta_{Guid.NewGuid():N}";
        string dummySessionId = $"cs_unrelated_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: dummySessionId,
            orderNumber: $"ORD-META-{Guid.NewGuid():N}",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_meta_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_unrelated_12345",
                "attributes": {
                  "status": "paid",
                  "metadata": {
                    "attempt_id": "{{attemptId}}"
                  }
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal("processed", root.GetProperty("processing_status").GetString());

        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Order? updatedOrder = await orderRepo.GetByIdAsync(order.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);

        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Paid, updatedOrder.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Correlation_ViaNestedPaymentIntentObjectId_ResolvesAndTransitionsToSucceeded()
    {
        string attemptId = $"att_pi_{Guid.NewGuid():N}";
        string paymentIntentId = $"pi_nested_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: paymentIntentId,
            orderNumber: $"ORD-PI-{Guid.NewGuid():N}",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_pi_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_qr_999",
                "attributes": {
                  "status": "paid",
                  "payment_intent": {
                    "id": "{{paymentIntentId}}",
                    "type": "payment_intent",
                    "attributes": {
                      "status": "succeeded"
                    }
                  }
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal("processed", root.GetProperty("processing_status").GetString());

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Correlation_ViaNestedSourceId_ResolvesAndTransitionsToSucceeded()
    {
        string attemptId = $"att_src_{Guid.NewGuid():N}";
        string sourceId = $"src_nested_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: sourceId,
            orderNumber: $"ORD-SRC-{Guid.NewGuid():N}",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_src_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_source_888",
                "attributes": {
                  "status": "paid",
                  "source": {
                    "id": "{{sourceId}}",
                    "type": "source"
                  }
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal("processed", root.GetProperty("processing_status").GetString());

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Correlation_PaymentFailed_ViaNestedPaymentIntentError_TransitionsToFailed()
    {
        string attemptId = $"att_pifail_{Guid.NewGuid():N}";
        string paymentIntentId = $"pi_failed_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: paymentIntentId,
            orderNumber: $"ORD-PIFAIL-{Guid.NewGuid():N}",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_pifail_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "pay_failed_777",
                "attributes": {
                  "status": "failed",
                  "payment_intent": {
                    "id": "{{paymentIntentId}}",
                    "type": "payment_intent",
                    "attributes": {
                      "status": "failed",
                      "last_payment_error": {
                        "code": "insufficient_funds",
                        "message": "The account has insufficient funds."
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Failed, updatedAttempt.Status);
        Assert.Equal("insufficient_funds", updatedAttempt.FailureCode);
        Assert.Equal("The account has insufficient funds.", updatedAttempt.FailureMessage);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_TerminalStateImmutability_PaymentFailedOnAlreadySucceeded_PreservesSucceededStatus()
    {
        string attemptId = $"att_lock_{Guid.NewGuid():N}";
        string providerSessionId = $"cs_lock_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerSessionId,
            orderNumber: $"ORD-LOCK-{Guid.NewGuid():N}",
            initialOrderStatus: "paid",
            initialAttemptStatus: PaymentAttemptStatus.Succeeded);

        string eventId = $"evt_lock_fail_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "{{providerSessionId}}",
                "attributes": {
                  "status": "failed",
                  "failed_code": "delayed_failure",
                  "failed_message": "Delayed payment failure received out of order."
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);
        Order? updatedOrder = await orderRepo.GetByIdAsync(order.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);
        Assert.Null(updatedAttempt.FailureCode);

        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Paid, updatedOrder.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_TerminalStateImmutability_QrphExpiredOnAlreadySucceeded_PreservesSucceededStatus()
    {
        string attemptId = $"att_lock_exp_{Guid.NewGuid():N}";
        string providerSessionId = $"cs_lock_exp_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: providerSessionId,
            orderNumber: $"ORD-LOCKEXP-{Guid.NewGuid():N}",
            initialOrderStatus: "paid",
            initialAttemptStatus: PaymentAttemptStatus.Succeeded);

        string eventId = $"evt_lock_exp_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "qrph.expired",
              "data": {
                "id": "{{providerSessionId}}",
                "attributes": {
                  "status": "expired"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAttempt.Status);
        Assert.Null(updatedAttempt.FailureCode);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Lifecycle_SourceChargeable_TransitionsAttemptToProcessing()
    {
        string attemptId = $"att_src_chg_{Guid.NewGuid():N}";
        string sourceId = $"src_chg_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId: attemptId,
            providerPaymentId: sourceId,
            orderNumber: $"ORD-CHG-{Guid.NewGuid():N}",
            initialOrderStatus: "pending",
            initialAttemptStatus: PaymentAttemptStatus.RequiresAction);

        string eventId = $"evt_src_chg_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "source.chargeable",
              "data": {
                "id": "{{sourceId}}",
                "attributes": {
                  "status": "chargeable"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        PaymentAttempt? updatedAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(updatedAttempt);
        Assert.Equal(PaymentAttemptStatus.Processing, updatedAttempt.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_UnresolvableAttempt_Returns200OkWithIgnoredStatus()
    {
        string eventId = $"evt_unresolvable_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_completely_unknown_99999",
                "attributes": {
                  "status": "paid",
                  "checkout_session_id": "cs_nonexistent_99999"
                }
              }
            }
          }
        }
        """;

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = jsonDoc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal("ignored", root.GetProperty("processing_status").GetString());
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private async Task<(Order order, PaymentAttempt attempt)> SeedOrderAndAttemptAsync(
        string attemptId,
        string providerPaymentId,
        string orderNumber,
        Money? amount = null,
        string initialOrderStatus = "pending",
        PaymentAttemptStatus initialAttemptStatus = PaymentAttemptStatus.RequiresAction)
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var authoritativeAmount = amount ?? Money.Php(1500_00);
        var nowUtc = DateTimeOffset.UtcNow;

        var order = Order.Rehydrate(
            id: $"ord_{Guid.NewGuid():N}",
            userId: "test_user",
            orderNumber: orderNumber,
            title: "Comprehensive Test Item",
            category: OrderCategory.ProductOrder,
            referenceCode: "TEST-ITEM-001",
            amount: authoritativeAmount,
            status: initialOrderStatus,
            metadataJson: "{}",
            createdAt: nowUtc,
            updatedAt: nowUtc);

        await orderRepo.SaveAsync(order);

        var attempt = PaymentAttempt.Rehydrate(
            id: attemptId,
            orderId: order.Id,
            provider: "paymongo",
            paymentMethod: "qrph",
            providerPaymentId: providerPaymentId,
            amount: authoritativeAmount,
            status: initialAttemptStatus,
            idempotencyKey: $"idem_{Guid.NewGuid():N}",
            checkoutUrl: $"https://checkout.paymongo.com/{providerPaymentId}",
            failureCode: null,
            failureMessage: null,
            expiresAt: null, // Left null so background expiration worker does not sweep it during concurrent test runs
            paidAt: null,
            createdAt: nowUtc,
            updatedAt: nowUtc);

        await attemptRepo.SaveAsync(attempt);

        return (order, attempt);
    }
}

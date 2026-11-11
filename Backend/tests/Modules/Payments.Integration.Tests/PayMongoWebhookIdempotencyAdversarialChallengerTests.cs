#pragma warning disable CA1849 // Call async methods when in an async method
#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Messaging;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Application.Ports;
using Rge.Payments.Contracts.Events;
using Rge.Payments.Domain.Errors;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Adversarial Empirical Challenger Suite 2:
/// Idempotency, Outbox Isolation, and Terminal State Machine Locking.
/// Stress-tests:
/// 1. Repeated delivery of identical payment.paid webhooks (same provider_event_id).
/// 2. Repeated delivery of payment.paid webhooks with distinct provider_event_ids for the same attempt/order.
/// 3. Outbox isolation guaranteeing OrderPaidIntegrationEvent is NEVER duplicated.
/// 4. Terminal state locking: Succeeded attempts cannot transition to Failed, Expired, Cancelled, or Processing.
/// 5. Safe HTTP 200 Ignored handling for unresolvable payloads, malformed structures, and unknown event types without crashing or 500.
/// 6. Malformed/out-of-range timestamp handling in payloads.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PayMongoWebhookIdempotencyAdversarialChallengerTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private const string WebhookSecret = "whsec_test_mock_secret_key_12345678";
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<string, OutboxMessage> _inMemoryOutbox;

    public PayMongoWebhookIdempotencyAdversarialChallengerTests(AccountingWebApplicationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _client = factory.CreateClient();

        var field = typeof(OutboxStore).GetField("InMemoryOutbox", BindingFlags.NonPublic | BindingFlags.Static);
        _inMemoryOutbox = (ConcurrentDictionary<string, OutboxMessage>)field!.GetValue(null)!;
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private int CountOrderPaidEventsForOrder(string orderId)
    {
        return _inMemoryOutbox.Values
            .Count(m => (m.EventType == OrderPaidIntegrationEvent.Type || m.EventType == "order.paid") &&
                        m.PayloadJson.Contains(orderId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(Order order, PaymentAttempt attempt)> SeedOrderAndAttemptAsync(
        string attemptId,
        string providerPaymentId,
        string orderNumber,
        PaymentAttemptStatus initialAttemptStatus = PaymentAttemptStatus.RequiresAction)
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var authoritativeAmount = Money.Php(2500_00);
        var nowUtc = DateTimeOffset.UtcNow;

        var order = Order.Rehydrate(
            id: $"ord_{Guid.NewGuid():N}",
            userId: "test_challenger_user",
            orderNumber: orderNumber,
            title: "Challenger Idempotency Test Item",
            category: OrderCategory.ProductOrder,
            referenceCode: "CHALLENGER-IDEMP-001",
            amount: authoritativeAmount,
            status: "pending",
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
            expiresAt: null,
            paidAt: initialAttemptStatus == PaymentAttemptStatus.Succeeded ? nowUtc : null,
            createdAt: nowUtc,
            updatedAt: nowUtc);

        await attemptRepo.SaveAsync(attempt);

        return (order, attempt);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string payload)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        return await _client.SendAsync(request);
    }

    // =========================================================================
    // 1. Repeated Delivery: Same provider_event_id
    // =========================================================================

    [Fact]
    public async Task RepeatedDelivery_SameProviderEventId_Returns200Ignored_OutboxEnqueuedExactlyOnce()
    {
        string attemptId = $"att_rep_same_{Guid.NewGuid():N}";
        string csId = $"cs_rep_same_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(attemptId, csId, "ORD-REP-SAME-001");

        string eventId = $"evt_same_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "paid"
                }
              }
            }
          }
        }
        """;

        // First delivery
        using HttpResponseMessage resp1 = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        using var doc1 = await JsonDocument.ParseAsync(await resp1.Content.ReadAsStreamAsync());
        Assert.Equal("processed", doc1.RootElement.GetProperty("processing_status").GetString());

        // Verify outbox has exactly 1 entry for this order
        int countAfterFirst = CountOrderPaidEventsForOrder(order.Id);
        Assert.Equal(1, countAfterFirst);

        // Second delivery with identical eventId
        using HttpResponseMessage resp2 = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        using var doc2 = await JsonDocument.ParseAsync(await resp2.Content.ReadAsStreamAsync());
        Assert.Equal("ignored", doc2.RootElement.GetProperty("processing_status").GetString());

        // Third delivery with identical eventId
        using HttpResponseMessage resp3 = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
        using var doc3 = await JsonDocument.ParseAsync(await resp3.Content.ReadAsStreamAsync());
        Assert.Equal("ignored", doc3.RootElement.GetProperty("processing_status").GetString());

        // Outbox entry MUST STILL be exactly 1
        int countAfterRepeats = CountOrderPaidEventsForOrder(order.Id);
        Assert.Equal(1, countAfterRepeats);
    }

    // =========================================================================
    // 2. Repeated Delivery: Distinct provider_event_id for same Payment/Order
    // =========================================================================

    [Fact]
    public async Task RepeatedDelivery_DifferentProviderEventId_NoDuplicateOutboxEvent()
    {
        string attemptId = $"att_rep_diff_{Guid.NewGuid():N}";
        string csId = $"cs_rep_diff_{Guid.NewGuid():N}";
        string payId = $"pay_rep_diff_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(attemptId, csId, "ORD-REP-DIFF-001");

        // Event 1: checkout_session.payment.paid
        string eventId1 = $"evt_cs_diff_{Guid.NewGuid():N}";
        string payload1 = $$"""
        {
          "data": {
            "id": "{{eventId1}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "paid",
                  "payment_id": "{{payId}}"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp1 = await PostWebhookAsync(payload1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal(1, CountOrderPaidEventsForOrder(order.Id));

        // Event 2: payment.paid with DIFFERENT event ID but referring to the same payment/session
        string eventId2 = $"evt_pay_diff_{Guid.NewGuid():N}";
        string payload2 = $$"""
        {
          "data": {
            "id": "{{eventId2}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "{{payId}}",
                "attributes": {
                  "status": "paid",
                  "checkout_session_id": "{{csId}}"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp2 = await PostWebhookAsync(payload2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        // Verify outbox entry is NEVER duplicated even across distinct event IDs
        int countAfterSecondEvent = CountOrderPaidEventsForOrder(order.Id);
        Assert.Equal(1, countAfterSecondEvent);

        // Verify attempt and order are still Succeeded and Paid
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        Order? finalOrder = await orderRepo.GetByIdAsync(order.Id);
        PaymentAttempt? finalAttempt = await attemptRepo.GetByIdAsync(attempt.Id);

        Assert.NotNull(finalOrder);
        Assert.Equal("paid", finalOrder.Status);
        Assert.NotNull(finalAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, finalAttempt.Status);
    }

    // =========================================================================
    // 3. Concurrent Webhook Delivery: Outbox Never Duplicates
    // =========================================================================

    [Fact]
    public async Task ConcurrentWebhookDelivery_MultiplePaymentPaidEvents_OutboxNeverDuplicates()
    {
        string attemptId = $"att_concurrent_{Guid.NewGuid():N}";
        string csId = $"cs_concurrent_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(attemptId, csId, "ORD-CONCURRENT-001");

        // Fire 5 concurrent webhook deliveries with distinct event IDs simultaneously
        var tasks = Enumerable.Range(1, 5).Select(i =>
        {
            string evtId = $"evt_concurrent_{i}_{Guid.NewGuid():N}";
            string payload = $$"""
            {
              "data": {
                "id": "{{evtId}}",
                "type": "event",
                "attributes": {
                  "type": "checkout_session.payment.paid",
                  "data": {
                    "id": "{{csId}}",
                    "attributes": {
                      "status": "paid"
                    }
                  }
                }
              }
            }
            """;
            return PostWebhookAsync(payload);
        }).ToArray();

        HttpResponseMessage[] responses = await Task.WhenAll(tasks);

        // Every response must be HTTP 200 OK
        foreach (var resp in responses)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            resp.Dispose();
        }

        // Verify that duplicate outbox events are NEVER created under concurrency
        int count = CountOrderPaidEventsForOrder(order.Id);
        Assert.Equal(1, count);
    }

    // =========================================================================
    // 4. Terminal State Locking: Succeeded Attempt Cannot Transition Out
    // =========================================================================

    [Fact]
    public async Task TerminalStateLocking_SucceededAttempt_LatePaymentFailed_PreservesSucceededStatus()
    {
        string attemptId = $"att_lock_fail_{Guid.NewGuid():N}";
        string csId = $"cs_lock_fail_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId, csId, "ORD-LOCK-FAIL-001", PaymentAttemptStatus.Succeeded);

        string eventId = $"evt_late_fail_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "failed",
                  "failed_code": "card_declined",
                  "failed_message": "Insufficient funds"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        PaymentAttempt? updated = await attemptRepo.GetByIdAsync(attemptId);

        Assert.NotNull(updated);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updated.Status);
        Assert.Null(updated.FailureCode);
        Assert.Null(updated.FailureMessage);

        // Verify that ExpireCheckoutSessionAsync was NOT called on gateway
        Assert.DoesNotContain(csId, _factory.Gateway.ExpiredSessionCalls);
    }

    [Fact]
    public async Task TerminalStateLocking_SucceededAttempt_LateQrphExpired_PreservesSucceededStatus()
    {
        string attemptId = $"att_lock_exp_{Guid.NewGuid():N}";
        string csId = $"cs_lock_exp_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId, csId, "ORD-LOCK-EXP-001", PaymentAttemptStatus.Succeeded);

        string eventId = $"evt_late_exp_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "qrph.expired",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "expired"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        PaymentAttempt? updated = await attemptRepo.GetByIdAsync(attemptId);

        Assert.NotNull(updated);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updated.Status);
        Assert.Null(updated.FailureCode);
    }

    [Fact]
    public async Task TerminalStateLocking_SucceededAttempt_LateSourceChargeable_PreservesSucceededStatus()
    {
        string attemptId = $"att_lock_chg_{Guid.NewGuid():N}";
        string srcId = $"src_lock_chg_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(
            attemptId, srcId, "ORD-LOCK-CHG-001", PaymentAttemptStatus.Succeeded);

        string eventId = $"evt_late_chg_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "source.chargeable",
              "data": {
                "id": "{{srcId}}",
                "attributes": {
                  "status": "chargeable"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
        PaymentAttempt? updated = await attemptRepo.GetByIdAsync(attemptId);

        Assert.NotNull(updated);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updated.Status);
    }

    // =========================================================================
    // 5. Domain State Machine: Exhaustive Terminal Locking Proof
    // =========================================================================

    [Theory]
    [InlineData(PaymentAttemptStatus.Pending)]
    [InlineData(PaymentAttemptStatus.RequiresAction)]
    [InlineData(PaymentAttemptStatus.Processing)]
    [InlineData(PaymentAttemptStatus.Failed)]
    [InlineData(PaymentAttemptStatus.Expired)]
    [InlineData(PaymentAttemptStatus.Cancelled)]
    [InlineData(PaymentAttemptStatus.Unknown)]
    public void DomainStateMachine_OnceSucceeded_CannotTransitionToAnyNonSucceededState(PaymentAttemptStatus targetStatus)
    {
        bool canTransition = PaymentAttemptStateMachine.CanTransitionAttempt(
            PaymentAttemptStatus.Succeeded, targetStatus);
        Assert.False(canTransition, $"Succeeded attempt must never be allowed to transition to {targetStatus}");

        var attempt = PaymentAttempt.CreatePending("ord_test", "qrph", Money.Php(100_00), "idem_1", DateTimeOffset.UtcNow);
        attempt.MarkSucceeded(DateTimeOffset.UtcNow);
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);

        // Attempting direct transitions
        if (targetStatus == PaymentAttemptStatus.Failed)
        {
            var res = attempt.MarkFailed("code", "msg", DateTimeOffset.UtcNow);
            Assert.True(res.IsFailure);
        }
        else if (targetStatus == PaymentAttemptStatus.Expired)
        {
            var res = attempt.MarkExpired(DateTimeOffset.UtcNow);
            Assert.True(res.IsFailure);
        }
        else if (targetStatus == PaymentAttemptStatus.Processing)
        {
            var res = attempt.MarkProcessing(DateTimeOffset.UtcNow);
            Assert.True(res.IsFailure);
        }

        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
    }

    // =========================================================================
    // 6. Unresolvable Payloads & Fallbacks: Always Returns HTTP 200 Ignored
    // =========================================================================

    [Fact]
    public async Task UnresolvablePayload_CompletelyUnknownIds_Returns200WithIgnored_Never500()
    {
        string eventId = $"evt_unknown_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "cs_nonexistent_resource_id_99999",
                "attributes": {
                  "status": "paid",
                  "payment_id": "pay_nonexistent_99999"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        JsonElement root = doc.RootElement;
        Assert.True(root.GetProperty("received").GetBoolean());
        Assert.Equal("ignored", root.GetProperty("processing_status").GetString());
    }

    [Fact]
    public async Task UnresolvablePayload_CorruptedInnerStructures_Returns200WithIgnored_Never500()
    {
        string eventId = $"evt_corrupted_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_corrupted_123",
                "attributes": {
                  "status": "paid",
                  "source": ["unexpected", "array", "type"],
                  "payment_intent": 12345678,
                  "checkout_session": true,
                  "payments": "not_an_array"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("ignored", doc.RootElement.GetProperty("processing_status").GetString());
    }

    [Fact]
    public async Task UnresolvablePayload_UnsupportedEventType_Returns200WithIgnored_Never500()
    {
        string eventId = $"evt_unsupported_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "customer.subscription.created",
              "data": {
                "id": "sub_123456",
                "attributes": {
                  "status": "active"
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp = await PostWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("ignored", doc.RootElement.GetProperty("processing_status").GetString());
    }

    // =========================================================================
    // 7. Malformed / Out-of-Range Timestamp in Webhook Payload: Does it Crash with 500?
    // =========================================================================

    [Theory]
    [InlineData(9223372036854775807L)] // long.MaxValue
    [InlineData(1731245678000L)]         // 13-digit Unix millisecond timestamp (year 56834)
    public async Task WebhookPayload_OutOfRangePaidAtTimestamp_Challenge(long timestampValue)
    {
        string attemptId = $"att_ts_{Guid.NewGuid():N}";
        string csId = $"cs_ts_{Guid.NewGuid():N}";
        var (order, attempt) = await SeedOrderAndAttemptAsync(attemptId, csId, $"ORD-TS-{Guid.NewGuid():N}");

        string eventId = $"evt_ts_{Guid.NewGuid():N}";
        string payload = $$"""
        {
          "data": {
            "id": "{{eventId}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "paid",
                  "paid_at": {{timestampValue}}
                }
              }
            }
          }
        }
        """;

        using HttpResponseMessage resp = await PostWebhookAsync(payload);

        // An out-of-range timestamp MUST NOT crash the server with HTTP 500
        Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
    }
}

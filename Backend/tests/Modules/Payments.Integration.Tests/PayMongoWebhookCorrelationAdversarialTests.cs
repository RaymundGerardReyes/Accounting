#pragma warning disable CA1849 // Call async methods when in an async method
#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Infrastructure.Outbox;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.Orders;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Tests.Common.Webhooks;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Adversarial empirical challenge tests for PayMongo webhook candidate correlation extraction,
/// 9-level resolution cascade precedence, nested source/payment_intent noise resilience,
/// unexpected JSON types, and out-of-order delivery state locking.
/// </summary>
[Collection("PaymentsIntegration")]
public sealed class PayMongoWebhookCorrelationAdversarialTests : IClassFixture<AccountingWebApplicationFactory>, IDisposable
{
    private const string WebhookSecret = "whsec_test_mock_secret_key_12345678";
    private readonly AccountingWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PayMongoWebhookCorrelationAdversarialTests(AccountingWebApplicationFactory factory)
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
            // Best effort cleanup
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
            // Best effort cleanup
        }
    }

    // =========================================================================
    // 1. Level 1 Precedence Over All Other 8 Cascade Levels
    // =========================================================================

    [Fact]
    public async Task Webhook_Correlation_CascadeLevel1_MetadataAttemptIdPrecedenceOverAllMismatchedIds()
    {
        // Seed 9 distinct attempts corresponding to the 9 cascade levels
        var (ord1, att1) = await SeedOrderAndAttemptAsync($"att_l1_{Guid.NewGuid():N}", $"cs_l1_{Guid.NewGuid():N}", $"ORD-L1-{Guid.NewGuid():N}");
        var (ord2, att2) = await SeedOrderAndAttemptAsync($"att_l2_{Guid.NewGuid():N}", $"cs_l2_top_{Guid.NewGuid():N}", $"ORD-L2-{Guid.NewGuid():N}");
        var (ord3, att3) = await SeedOrderAndAttemptAsync($"att_l3_{Guid.NewGuid():N}", $"cs_l3_inner_{Guid.NewGuid():N}", $"ORD-L3-{Guid.NewGuid():N}");
        var (ord4, att4) = await SeedOrderAndAttemptAsync($"att_l4_{Guid.NewGuid():N}", $"pay_l4_{Guid.NewGuid():N}", $"ORD-L4-{Guid.NewGuid():N}");
        var (ord5, att5) = await SeedOrderAndAttemptAsync($"att_l5_{Guid.NewGuid():N}", $"src_l5_{Guid.NewGuid():N}", $"ORD-L5-{Guid.NewGuid():N}");
        var (ord6, att6) = await SeedOrderAndAttemptAsync($"att_l6_{Guid.NewGuid():N}", $"pi_l6_{Guid.NewGuid():N}", $"ORD-L6-{Guid.NewGuid():N}");
        var (ord7, att7) = await SeedOrderAndAttemptAsync($"att_l7_{Guid.NewGuid():N}", $"cs_l7_{Guid.NewGuid():N}", $"ORD-L7-{Guid.NewGuid():N}", idempotencyKey: $"idem_l7_{Guid.NewGuid():N}");
        var (ord8, att8) = await SeedOrderAndAttemptAsync($"att_l8_{Guid.NewGuid():N}", $"cs_l8_{Guid.NewGuid():N}", $"ORD-L8-{Guid.NewGuid():N}");
        var (ord9, att9) = await SeedOrderAndAttemptAsync($"att_l9_{Guid.NewGuid():N}", $"cs_l9_{Guid.NewGuid():N}", $"ORD-L9-{Guid.NewGuid():N}");

        string eventId = $"evt_prec_{Guid.NewGuid():N}";
        // Construct webhook payload containing candidate IDs for ALL 9 levels simultaneously
        string payload = $$"""
        {
          "data": {
            "id": "{{att2.ProviderPaymentId}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_ignored_level2",
                "attributes": {
                  "status": "paid",
                  "checkout_session_id": "{{att3.ProviderPaymentId}}",
                  "payment_id": "{{att4.ProviderPaymentId}}",
                  "source_id": "{{att5.ProviderPaymentId}}",
                  "payment_intent_id": "{{att6.ProviderPaymentId}}",
                  "order_id": "{{ord8.Id}}",
                  "order_number": "{{ord9.OrderNumber}}",
                  "description": "Noise description that should not overwrite",
                  "metadata": {
                    "attempt_id": "{{att1.Id}}",
                    "idempotency_key": "{{att7.IdempotencyKey}}",
                    "order_id": "{{ord8.Id}}",
                    "order_number": "{{ord9.OrderNumber}}"
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
        Assert.Equal("processed", jsonDoc.RootElement.GetProperty("processing_status").GetString());
        Assert.Equal(att1.Id, jsonDoc.RootElement.GetProperty("payment_attempt_id").GetString());

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        // Level 1 attempt MUST be Succeeded
        PaymentAttempt? updatedAtt1 = await attemptRepo.GetByIdAsync(att1.Id);
        Assert.NotNull(updatedAtt1);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt1.Status);

        // All other levels (2 through 9) MUST remain in initial RequiresAction state
        PaymentAttempt? updatedAtt2 = await attemptRepo.GetByIdAsync(att2.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt2?.Status);

        PaymentAttempt? updatedAtt3 = await attemptRepo.GetByIdAsync(att3.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt3?.Status);

        PaymentAttempt? updatedAtt4 = await attemptRepo.GetByIdAsync(att4.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt4?.Status);

        PaymentAttempt? updatedAtt5 = await attemptRepo.GetByIdAsync(att5.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt5?.Status);

        PaymentAttempt? updatedAtt6 = await attemptRepo.GetByIdAsync(att6.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt6?.Status);

        PaymentAttempt? updatedAtt7 = await attemptRepo.GetByIdAsync(att7.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt7?.Status);

        PaymentAttempt? updatedAtt8 = await attemptRepo.GetByIdAsync(att8.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt8?.Status);

        PaymentAttempt? updatedAtt9 = await attemptRepo.GetByIdAsync(att9.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt9?.Status);

        await DrainOutboxAsync();
    }

    // =========================================================================
    // 2. Cascade Hierarchy: Fallthrough When Higher Levels are Absent
    // =========================================================================

    [Fact]
    public async Task Webhook_Correlation_CascadeLevel2_ResourceIdPrecedenceOverLowerLevels()
    {
        // When metadata.attempt_id is missing, Level 2 (resourceId) must take precedence
        var (ord2, att2) = await SeedOrderAndAttemptAsync($"att_fall2_{Guid.NewGuid():N}", $"cs_fall2_{Guid.NewGuid():N}", $"ORD-FALL2-{Guid.NewGuid():N}");
        var (ord3, att3) = await SeedOrderAndAttemptAsync($"att_fall3_{Guid.NewGuid():N}", $"cs_fall3_{Guid.NewGuid():N}", $"ORD-FALL3-{Guid.NewGuid():N}");
        var (ord5, att5) = await SeedOrderAndAttemptAsync($"att_fall5_{Guid.NewGuid():N}", $"src_fall5_{Guid.NewGuid():N}", $"ORD-FALL5-{Guid.NewGuid():N}");

        string payload = $$"""
        {
          "data": {
            "id": "evt_fall2_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "{{att2.ProviderPaymentId}}",
                "attributes": {
                  "status": "paid",
                  "checkout_session_id": "{{att3.ProviderPaymentId}}",
                  "source_id": "{{att5.ProviderPaymentId}}"
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

        PaymentAttempt? updatedAtt2 = await attemptRepo.GetByIdAsync(att2.Id);
        Assert.NotNull(updatedAtt2);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt2.Status);

        PaymentAttempt? updatedAtt3 = await attemptRepo.GetByIdAsync(att3.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt3?.Status);

        PaymentAttempt? updatedAtt5 = await attemptRepo.GetByIdAsync(att5.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt5?.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Correlation_CascadeLevel5_SourcePrecedenceOverLowerLevels()
    {
        // When Levels 1-4 are absent, Level 5 (source_id) must resolve over Level 6, 7, 8, 9
        var (ord5, att5) = await SeedOrderAndAttemptAsync($"att_fall5b_{Guid.NewGuid():N}", $"src_fall5b_{Guid.NewGuid():N}", $"ORD-FALL5B-{Guid.NewGuid():N}");
        var (ord6, att6) = await SeedOrderAndAttemptAsync($"att_fall6b_{Guid.NewGuid():N}", $"pi_fall6b_{Guid.NewGuid():N}", $"ORD-FALL6B-{Guid.NewGuid():N}");
        var (ord8, att8) = await SeedOrderAndAttemptAsync($"att_fall8b_{Guid.NewGuid():N}", $"cs_fall8b_{Guid.NewGuid():N}", $"ORD-FALL8B-{Guid.NewGuid():N}");

        string payload = $$"""
        {
          "data": {
            "id": "evt_fall5b_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_nonexistent_{{Guid.NewGuid():N}}",
                "attributes": {
                  "status": "paid",
                  "source": {
                    "id": "{{att5.ProviderPaymentId}}"
                  },
                  "payment_intent": {
                    "id": "{{att6.ProviderPaymentId}}"
                  },
                  "metadata": {
                    "order_id": "{{ord8.Id}}"
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

        PaymentAttempt? updatedAtt5 = await attemptRepo.GetByIdAsync(att5.Id);
        Assert.NotNull(updatedAtt5);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt5.Status);

        PaymentAttempt? updatedAtt6 = await attemptRepo.GetByIdAsync(att6.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt6?.Status);

        PaymentAttempt? updatedAtt8 = await attemptRepo.GetByIdAsync(att8.Id);
        Assert.Equal(PaymentAttemptStatus.RequiresAction, updatedAtt8?.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Correlation_CascadeLevel9_ReferenceNumberOrderResolution()
    {
        // When only order_number is present in metadata, Level 9 must resolve order then latest attempt
        string orderNum = $"ORD-L9-{Guid.NewGuid():N}";
        var (ord9, att9) = await SeedOrderAndAttemptAsync($"att_l9_res_{Guid.NewGuid():N}", $"cs_l9_res_{Guid.NewGuid():N}", orderNum);

        string payload = $$"""
        {
          "data": {
            "id": "evt_l9_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_unknown_{{Guid.NewGuid():N}}",
                "attributes": {
                  "status": "paid",
                  "metadata": {
                    "order_number": "{{orderNum}}"
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

        PaymentAttempt? updatedAtt9 = await attemptRepo.GetByIdAsync(att9.Id);
        Assert.NotNull(updatedAtt9);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt9.Status);

        await DrainOutboxAsync();
    }

    // =========================================================================
    // 3. Deeply Nested Objects with Extra Noise & Unrecognized Sibling Fields
    // =========================================================================

    [Fact]
    public async Task Webhook_Correlation_DeeplyNested_PaymentIntentAndSourceWithExtremeNoise()
    {
        string sourceId = $"src_deep_noise_{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync($"att_noise_{Guid.NewGuid():N}", sourceId, $"ORD-NOISE-{Guid.NewGuid():N}");

        // Payload with 5 levels of nesting, noise arrays, booleans, nulls, and unicode properties
        string payload = $$"""
        {
          "data": {
            "id": "evt_noise_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "extra_unrecognized_top_field": [ 1, 2, { "sub": true } ],
              "data": {
                "id": "pay_noise_outer",
                "attributes": {
                  "status": "paid",
                  "unicode_key_测试": "🚀🌟💥",
                  "payments": [
                    { "noise": true },
                    {
                      "id": "pay_dummy_first",
                      "attributes": {
                        "extra": null,
                        "source": {
                          "id": "{{sourceId}}",
                          "attributes": {
                            "type": "gcash",
                            "deep_noise": {
                              "l1": {
                                "l2": {
                                  "l3": [ true, false, null, 12345 ]
                                }
                              }
                            }
                          }
                        }
                      }
                    }
                  ]
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

        PaymentAttempt? updatedAtt = await attemptRepo.GetByIdAsync(att.Id);
        Assert.NotNull(updatedAtt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_Correlation_PaymentFailed_DeeplyNestedLastPaymentError_ExtractsFailureDetails()
    {
        string piId = $"pi_nested_fail_{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync($"att_nfail_{Guid.NewGuid():N}", piId, $"ORD-NFAIL-{Guid.NewGuid():N}");

        string payload = $$"""
        {
          "data": {
            "id": "evt_nfail_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "pay_fail_outer",
                "attributes": {
                  "status": "failed",
                  "payment_intent": {
                    "id": "{{piId}}",
                    "attributes": {
                      "status": "awaiting_payment_method",
                      "last_payment_error": {
                        "code": "insufficient_funds",
                        "message": "The customer balance is insufficient for this charge.",
                        "extra_array": [1, "two", null]
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

        PaymentAttempt? updatedAtt = await attemptRepo.GetByIdAsync(att.Id);
        Assert.NotNull(updatedAtt);
        Assert.Equal(PaymentAttemptStatus.Failed, updatedAtt.Status);
        Assert.Equal("insufficient_funds", updatedAtt.FailureCode);
        Assert.Equal("The customer balance is insufficient for this charge.", updatedAtt.FailureMessage);

        await DrainOutboxAsync();
    }

    // =========================================================================
    // 4. Unexpected JSON Types (Booleans, Arrays, Nulls for Scalar Fields)
    // =========================================================================

    [Theory]
    [InlineData("{\"metadata\": true}")]
    [InlineData("{\"metadata\": [1, 2, 3]}")]
    [InlineData("{\"metadata\": null}")]
    [InlineData("{\"metadata\": \"string_instead_of_object\"}")]
    [InlineData("{\"checkout_session_id\": false}")]
    [InlineData("{\"checkout_session_id\": [\"cs_1\", \"cs_2\"]}")]
    [InlineData("{\"checkout_session\": true}")]
    [InlineData("{\"source\": [\"src_array\"]}")]
    [InlineData("{\"payment_intent\": false}")]
    [InlineData("{\"payments\": \"not_an_array\"}")]
    [InlineData("{\"payments\": [null, 123, true, \"scalar\"]}")]
    [InlineData("{\"paid_at\": -500}")]
    [InlineData("{\"paid_at\": \"yesterday\"}")]
    [InlineData("{\"paid_at\": null}")]
    public async Task Webhook_AdversarialTypes_MalformedScalarFieldsDoNotCrashHandler(string malformedFragment)
    {
        ArgumentNullException.ThrowIfNull(malformedFragment);
        string attemptId = $"att_type_resil_{Guid.NewGuid():N}";
        string csId = $"cs_type_resil_{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync(attemptId, csId, $"ORD-TYPE-{Guid.NewGuid():N}");

        // Splice malformed JSON fragment alongside a valid metadata attempt_id
        string payload = $$"""
        {
          "data": {
            "id": "evt_type_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "paid",
                  "metadata": {
                    "attempt_id": "{{attemptId}}"
                  },
                  {{malformedFragment.TrimStart('{').TrimEnd('}')}}
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
        // Must succeed with 200 OK and not crash with 500
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        PaymentAttempt? updatedAtt = await attemptRepo.GetByIdAsync(att.Id);
        Assert.NotNull(updatedAtt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt.Status);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Webhook_AdversarialTimestamp_MillisecondTimestampDoesNotCrashWith500()
    {
        string attemptId = $"att_ts_{Guid.NewGuid():N}";
        string csId = $"cs_ts_{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync(attemptId, csId, $"ORD-TS-{Guid.NewGuid():N}");

        string payload = $$"""
        {
          "data": {
            "id": "evt_ts_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "paid",
                  "paid_at": 1731245678000,
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
    }

    [Fact]
    public async Task Webhook_RootArrayPayload_DoesNotCrashWith500()
    {
        string payload = "[1, 2, 3]";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, timestamp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Paymongo-Signature", signature);

        using HttpResponseMessage response = await _client.SendAsync(request);
        // It should return 400 Bad Request and NOT crash with 500 Internal Server Error
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_MalformedEnvelope_MissingDataAttributesSafelyIgnored()
    {
        // Payload has data.id but missing attributes entirely
        string payload = $$"""
        {
          "data": {
            "id": "evt_no_attr_{{Guid.NewGuid():N}}",
            "type": "event"
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
        Assert.Equal("ignored", jsonDoc.RootElement.GetProperty("processing_status").GetString());
    }

    // =========================================================================
    // 5. Out-of-Order Delivery: payment.paid -> payment.failed -> qrph.expired
    // =========================================================================

    [Fact]
    public async Task Webhook_OutOfOrder_PaidFollowedByFailedAndExpired_LocksTerminalState()
    {
        string attemptId = $"att_ooo_{Guid.NewGuid():N}";
        string csId = $"cs_ooo_{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync(attemptId, csId, $"ORD-OOO-{Guid.NewGuid():N}");

        // 1. Send payment.paid
        string paidPayload = $$"""
        {
          "data": {
            "id": "evt_ooo_paid_{{Guid.NewGuid():N}}",
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

        long t1 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(paidPayload, Encoding.UTF8, "application/json")
        })
        {
            req1.Headers.Add("Paymongo-Signature", WebhookSigner.CreateSignatureHeader(paidPayload, WebhookSecret, t1));
            using HttpResponseMessage res1 = await _client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        }

        // Verify Attempt transitioned to Succeeded
        using (var scope1 = _factory.Services.CreateScope())
        {
            var attemptRepo = scope1.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
            PaymentAttempt? attPaid = await attemptRepo.GetByIdAsync(att.Id);
            Assert.NotNull(attPaid);
            Assert.Equal(PaymentAttemptStatus.Succeeded, attPaid.Status);
        }

        // 2. Immediately send payment.failed for the same session
        string failedPayload = $$"""
        {
          "data": {
            "id": "evt_ooo_failed_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.failed",
              "data": {
                "id": "{{csId}}",
                "attributes": {
                  "status": "failed",
                  "failed_code": "card_declined",
                  "failed_message": "Do not honor"
                }
              }
            }
          }
        }
        """;

        long t2 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(failedPayload, Encoding.UTF8, "application/json")
        })
        {
            req2.Headers.Add("Paymongo-Signature", WebhookSigner.CreateSignatureHeader(failedPayload, WebhookSecret, t2));
            using HttpResponseMessage res2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        }

        // 3. Immediately send qrph.expired for the same session
        string expiredPayload = $$"""
        {
          "data": {
            "id": "evt_ooo_exp_{{Guid.NewGuid():N}}",
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

        long t3 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var req3 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(expiredPayload, Encoding.UTF8, "application/json")
        })
        {
            req3.Headers.Add("Paymongo-Signature", WebhookSigner.CreateSignatureHeader(expiredPayload, WebhookSecret, t3));
            using HttpResponseMessage res3 = await _client.SendAsync(req3);
            Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
        }

        // Verify Terminal State Locking: Attempt MUST remain Succeeded, FailureCode must remain null
        using (var scopeFinal = _factory.Services.CreateScope())
        {
            var attemptRepo = scopeFinal.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();
            var orderRepo = scopeFinal.ServiceProvider.GetRequiredService<IOrderRepository>();

            PaymentAttempt? finalAttempt = await attemptRepo.GetByIdAsync(att.Id);
            Assert.NotNull(finalAttempt);
            Assert.Equal(PaymentAttemptStatus.Succeeded, finalAttempt.Status);
            Assert.Null(finalAttempt.FailureCode);
            Assert.Null(finalAttempt.FailureMessage);

            Order? finalOrder = await orderRepo.GetByIdAsync(ord.Id);
            Assert.NotNull(finalOrder);
            Assert.Equal(OrderStatus.Paid, finalOrder.Status);
        }

        await DrainOutboxAsync();
    }

    // =========================================================================
    // 6. Concurrent Rapid-Fire Delivery Under Mixed Lifecycle Events
    // =========================================================================

    [Fact]
    public async Task Webhook_OutOfOrder_ConcurrentMixedEvents_PreservesTerminalIntegrity()
    {
        string attemptId = $"att_conc_{Guid.NewGuid():N}";
        string csId = $"cs_conc_{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync(attemptId, csId, $"ORD-CONC-{Guid.NewGuid():N}");

        // 1. Establish terminal Succeeded state
        string initPaidPayload = $$"""
        {
          "data": {
            "id": "evt_conc_init_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "checkout_session.payment.paid",
              "data": {
                "id": "{{csId}}",
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

        long initTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using (var initReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
        {
            Content = new StringContent(initPaidPayload, Encoding.UTF8, "application/json")
        })
        {
            initReq.Headers.Add("Paymongo-Signature", WebhookSigner.CreateSignatureHeader(initPaidPayload, WebhookSecret, initTs));
            using HttpResponseMessage initRes = await _client.SendAsync(initReq);
            Assert.Equal(HttpStatusCode.OK, initRes.StatusCode);
        }

        // 2. Fire rapid concurrent conflicting lifecycle events (failures, expirations, charges)
        var events = new (string eventType, string status)[]
        {
            ("source.chargeable", "chargeable"),
            ("checkout_session.payment.paid", "paid"),
            ("payment.failed", "failed"),
            ("qrph.expired", "expired"),
            ("checkout_session.payment.paid", "paid"),
            ("source.chargeable", "chargeable"),
            ("payment.failed", "failed"),
            ("qr_code.expired", "expired"),
        };

        var tasks = events.Select(async (evt, index) =>
        {
            string payload = $$"""
            {
              "data": {
                "id": "evt_conc_{{index}}_{{Guid.NewGuid():N}}",
                "type": "event",
                "attributes": {
                  "type": "{{evt.eventType}}",
                  "data": {
                    "id": "{{csId}}",
                    "attributes": {
                      "status": "{{evt.status}}",
                      "metadata": {
                        "attempt_id": "{{attemptId}}"
                      }
                    }
                  }
                }
              }
            }
            """;

            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/paymongo")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Paymongo-Signature", WebhookSigner.CreateSignatureHeader(payload, WebhookSecret, ts));

            using HttpResponseMessage res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        });

        await Task.WhenAll(tasks);

        using var scope = _factory.Services.CreateScope();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        PaymentAttempt? finalAttempt = await attemptRepo.GetByIdAsync(att.Id);
        Assert.NotNull(finalAttempt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, finalAttempt.Status);

        await DrainOutboxAsync();
    }

    // =========================================================================
    // 7. Metadata Field Aliases: CamelCase and SnakeCase Variants
    // =========================================================================

    [Theory]
    [InlineData("attempt_id")]
    [InlineData("payment_attempt_id")]
    [InlineData("attemptId")]
    [InlineData("paymentAttemptId")]
    public async Task Webhook_Metadata_AttemptIdAliases_AllResolveAttempt(string keyAlias)
    {
        ArgumentNullException.ThrowIfNull(keyAlias);
        string attemptId = $"att_alias_{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync(attemptId, $"cs_alias_{Guid.NewGuid():N}", $"ORD-ALIAS-{Guid.NewGuid():N}");

        string payload = $$"""
        {
          "data": {
            "id": "evt_alias_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_dummy_alias",
                "attributes": {
                  "status": "paid",
                  "metadata": {
                    "{{keyAlias}}": "{{attemptId}}"
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

        PaymentAttempt? updatedAtt = await attemptRepo.GetByIdAsync(att.Id);
        Assert.NotNull(updatedAtt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt.Status);

        await DrainOutboxAsync();
    }

    [Theory]
    [InlineData("order_number")]
    [InlineData("orderNumber")]
    [InlineData("external_reference_number")]
    [InlineData("externalReferenceNumber")]
    [InlineData("reference_number")]
    [InlineData("referenceNumber")]
    public async Task Webhook_Metadata_OrderNumberAliases_AllResolveAttempt(string orderKeyAlias)
    {
        ArgumentNullException.ThrowIfNull(orderKeyAlias);
        string orderNum = $"ORD-ALIAS-{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync($"att_ord_alias_{Guid.NewGuid():N}", $"cs_ord_alias_{Guid.NewGuid():N}", orderNum);

        string payload = $$"""
        {
          "data": {
            "id": "evt_ord_alias_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_dummy_ord_alias",
                "attributes": {
                  "status": "paid",
                  "metadata": {
                    "{{orderKeyAlias}}": "{{orderNum}}"
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

        PaymentAttempt? updatedAtt = await attemptRepo.GetByIdAsync(att.Id);
        Assert.NotNull(updatedAtt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt.Status);

        await DrainOutboxAsync();
    }

    // =========================================================================
    // 8. Order Reference Guard: Description Never Overwrites Order Number
    // =========================================================================

    [Fact]
    public async Task Webhook_OrderReference_DescriptionDoesNotOverwriteOrderNumber()
    {
        string orderNum = $"ORD-GUARD-{Guid.NewGuid():N}";
        var (ord, att) = await SeedOrderAndAttemptAsync($"att_guard_{Guid.NewGuid():N}", $"cs_guard_{Guid.NewGuid():N}", orderNum);

        // Payload has both metadata.order_number and innerAttrs.description
        string payload = $$"""
        {
          "data": {
            "id": "evt_guard_{{Guid.NewGuid():N}}",
            "type": "event",
            "attributes": {
              "type": "payment.paid",
              "data": {
                "id": "pay_dummy_guard",
                "attributes": {
                  "status": "paid",
                  "description": "Bogus Description Value That Is Not An Order Number",
                  "metadata": {
                    "order_number": "{{orderNum}}"
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

        PaymentAttempt? updatedAtt = await attemptRepo.GetByIdAsync(att.Id);
        Assert.NotNull(updatedAtt);
        Assert.Equal(PaymentAttemptStatus.Succeeded, updatedAtt.Status);

        await DrainOutboxAsync();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Order order, PaymentAttempt attempt)> SeedOrderAndAttemptAsync(
        string attemptId,
        string providerPaymentId,
        string orderNumber,
        string? idempotencyKey = null,
        Money? amount = null,
        string initialOrderStatus = "pending",
        PaymentAttemptStatus initialAttemptStatus = PaymentAttemptStatus.RequiresAction)
    {
        using var scope = _factory.Services.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

        var authoritativeAmount = amount ?? Money.Php(2500_00);
        var nowUtc = DateTimeOffset.UtcNow;

        var order = Order.Rehydrate(
            id: $"ord_{Guid.NewGuid():N}",
            userId: "challenger_user",
            orderNumber: orderNumber,
            title: "Adversarial Correlation Test Item",
            category: OrderCategory.ProductOrder,
            referenceCode: "ADV-ITEM-001",
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
            idempotencyKey: idempotencyKey ?? $"idem_{Guid.NewGuid():N}",
            checkoutUrl: $"https://checkout.paymongo.com/{providerPaymentId}",
            failureCode: null,
            failureMessage: null,
            expiresAt: null,
            paidAt: null,
            createdAt: nowUtc,
            updatedAt: nowUtc);

        await attemptRepo.SaveAsync(attempt);

        return (order, attempt);
    }
}

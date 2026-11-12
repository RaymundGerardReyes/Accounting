using System.Text.Json;
using Rge.Payments.Infrastructure.PayMongo;
using Xunit;

namespace Rge.Contract.Parity.Tests;

public sealed class PayMongoRealGatewayProofTests
{
    [Fact]
    public void RealPayMongoJson_WhenPaymentFailsOnActiveSession_EvaluatesAsFailed_NotActive()
    {
        // Authentic PayMongo GET /v1/checkout_sessions/{cs_id} response when a payment attempt fails:
        // Notice top-level status is STILL "active" and payments[] is empty, while payment_intent has last_payment_error!
        const string realPayMongoFailedResponse = """
        {
          "data": {
            "id": "cs_real_failed_test_123",
            "type": "checkout_session",
            "attributes": {
              "checkout_url": "https://checkout.paymongo.com/cs_real_failed_test_123",
              "status": "active",
              "payment_method_used": "card",
              "payments": [],
              "payment_intent": {
                "id": "pi_real_123",
                "type": "payment_intent",
                "attributes": {
                  "status": "awaiting_payment_method",
                  "last_payment_error": {
                    "code": "card_declined",
                    "detail": "Your card was declined by the issuing bank.",
                    "failed_code": "card_declined",
                    "failed_message": "Your card was declined by the issuing bank."
                  }
                }
              }
            }
          }
        }
        """;

        var envelope = JsonSerializer.Deserialize<PayMongoCheckoutSessionEnvelope>(realPayMongoFailedResponse);
        Assert.NotNull(envelope?.Data);

        var evaluated = PayMongoClient.EvaluateCheckoutSessionStatus(envelope.Data);

        // Must detect "failed" instead of returning "active"
        Assert.Equal("failed", evaluated.Status);
        Assert.Equal("card_declined", evaluated.FailureCode);
        Assert.Contains("declined", evaluated.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealPayMongoJson_WhenSessionIsExpiredWithPendingNestedPayment_EvaluatesAsExpired()
    {
        // Authentic PayMongo response where Checkout Session is expired, even if a nested payment entry exists
        const string realPayMongoExpiredResponse = """
        {
          "data": {
            "id": "cs_real_expired_test_456",
            "type": "checkout_session",
            "attributes": {
              "checkout_url": "https://checkout.paymongo.com/cs_real_expired_test_456",
              "status": "expired",
              "payment_intent": {
                "id": "pi_real_456",
                "type": "payment_intent",
                "attributes": {
                  "status": "cancelled"
                }
              },
              "payments": [
                {
                  "id": "pay_pending_old",
                  "attributes": {
                    "status": "pending"
                  }
                }
              ]
            }
          }
        }
        """;

        var envelope = JsonSerializer.Deserialize<PayMongoCheckoutSessionEnvelope>(realPayMongoExpiredResponse);
        Assert.NotNull(envelope?.Data);

        var evaluated = PayMongoClient.EvaluateCheckoutSessionStatus(envelope.Data);

        // Must prioritize "expired" over the stale nested "pending" payment
        Assert.Equal("expired", evaluated.Status);
    }

    [Fact]
    public void RealPayMongoJson_WhenSourceExpiresOnActiveSession_TransitionsToAwaitingPaymentMethod_RemainsActiveWithRecoverableCode()
    {
        const string sourceActionJson = """
        {
          "data": {
            "id": "cs_source_drop_test_789",
            "type": "checkout_session",
            "attributes": {
              "status": "active",
              "payment_intent": {
                "id": "pi_source_789",
                "type": "payment_intent",
                "attributes": {
                  "status": "awaiting_next_action"
                }
              }
            }
          }
        }
        """;

        var actionEnvelope = JsonSerializer.Deserialize<PayMongoCheckoutSessionEnvelope>(sourceActionJson);
        Assert.NotNull(actionEnvelope?.Data);
        var initialEvaluation = PayMongoClient.EvaluateCheckoutSessionStatus(actionEnvelope.Data);
        Assert.Equal("awaiting_next_action", initialEvaluation.Status);

        // When customer lets a QR/source expire on an active Checkout Session, PayMongo resets payment_intent to awaiting_payment_method
        const string sourceExpiredResetJson = """
        {
          "data": {
            "id": "cs_source_drop_test_789",
            "type": "checkout_session",
            "attributes": {
              "status": "active",
              "payment_intent": {
                "id": "pi_source_789",
                "type": "payment_intent",
                "attributes": {
                  "status": "awaiting_payment_method"
                }
              }
            }
          }
        }
        """;

        var resetEnvelope = JsonSerializer.Deserialize<PayMongoCheckoutSessionEnvelope>(sourceExpiredResetJson);
        Assert.NotNull(resetEnvelope?.Data);
        var finalEvaluation = PayMongoClient.EvaluateCheckoutSessionStatus(resetEnvelope.Data);

        // Must preserve the active Checkout Session with recoverable qrph_source_expired code
        Assert.Equal("awaiting_next_action", finalEvaluation.Status);
        Assert.Equal("qrph_source_expired", finalEvaluation.FailureCode);
    }

    [Theory]
    [InlineData("qrph", new[] { "qrph" })]
    [InlineData("gcash", new[] { "gcash" })]
    [InlineData("paymaya", new[] { "paymaya" })]
    [InlineData("maya", new[] { "paymaya" })]
    [InlineData("card", new[] { "card" })]
    [InlineData("gcash_maya", new[] { "gcash", "paymaya" })]
    public void MapPaymentMethodTypes_MapsFourCustomerChoicesAndLegacyAliasCleanly(string method, string[] expectedTypes)
    {
        var actual = PayMongoClient.MapPaymentMethodTypes(method);
        Assert.Equal(expectedTypes, actual);
    }
}


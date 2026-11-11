using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Payments.Infrastructure.PayMongo;
using Xunit;

namespace Rge.Contract.Parity.Tests;

public sealed class PaymentAttemptWorkflowLifecycleTests
{
    [Theory]
    [InlineData("src_FXrhoSDvtUNzin16cZL9p3VN", PayMongoWorkflowKind.LegacySource)]
    [InlineData("pi_Uq7doY12NpjwysLk8gmaK3ai", PayMongoWorkflowKind.QrPhPaymentIntent)]
    [InlineData("pm_test_qrph_12345", PayMongoWorkflowKind.QrPhPaymentIntent)]
    [InlineData("cs_f3f50d639d00467a1338a89f", PayMongoWorkflowKind.HostedCheckoutSession)]
    [InlineData("", PayMongoWorkflowKind.HostedCheckoutSession)]
    [InlineData(null, PayMongoWorkflowKind.HostedCheckoutSession)]
    public void DetectWorkflow_IdentifiesCorrectWorkflowKind(string? resourceId, PayMongoWorkflowKind expected)
    {
        var actual = PaymentAttemptStateMachine.DetectWorkflow(resourceId);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LegacySource_WhenExpired_CannotTransitionToNonTerminal()
    {
        bool canTransition = PaymentAttemptStateMachine.CanTransitionAttempt(
            PaymentAttemptStatus.Expired,
            PaymentAttemptStatus.RequiresAction,
            PayMongoWorkflowKind.LegacySource);

        Assert.False(canTransition, "Expired legacy source must never transition back to active.");
    }

    [Theory]
    [InlineData("pending", PaymentAttemptStatus.RequiresAction)]
    [InlineData("chargeable", PaymentAttemptStatus.Processing)]
    [InlineData("paid", PaymentAttemptStatus.Succeeded)]
    [InlineData("expired", PaymentAttemptStatus.Expired)]
    [InlineData("cancelled", PaymentAttemptStatus.Failed)]
    [InlineData("failed", PaymentAttemptStatus.Failed)]
    public void MapFromLegacySourceStatus_MapsCorrectly(string sourceStatus, PaymentAttemptStatus expected)
    {
        var mapped = PaymentAttemptStateMachine.MapFromLegacySourceStatus(sourceStatus);
        Assert.Equal(expected, mapped);
    }

    [Theory]
    [InlineData("awaiting_payment_method", PaymentAttemptStatus.RequiresAction)]
    [InlineData("awaiting_next_action", PaymentAttemptStatus.RequiresAction)]
    [InlineData("processing", PaymentAttemptStatus.Processing)]
    [InlineData("succeeded", PaymentAttemptStatus.Succeeded)]
    [InlineData("cancelled", PaymentAttemptStatus.Cancelled)]
    [InlineData("failed", PaymentAttemptStatus.Failed)]
    public void MapFromPaymentIntentStatus_MapsCorrectly(string intentStatus, PaymentAttemptStatus expected)
    {
        var mapped = PaymentAttemptStateMachine.MapFromPaymentIntentStatus(intentStatus);
        Assert.Equal(expected, mapped);
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WithDirectSourceId_QueriesSourcesEndpoint()
    {
        string srcId = "src_FXrhoSDvtUNzin16cZL9p3VN";
        string sourceJson = $$"""
        {
            "data": {
                "id": "{{srcId}}",
                "type": "source",
                "attributes": {
                    "amount": 10000,
                    "currency": "PHP",
                    "status": "expired",
                    "type": "gcash"
                }
            }
        }
        """;

        using var handler = new MockHttpMessageHandler();
        handler.MapRoute($"sources/{srcId}", HttpStatusCode.OK, sourceJson);

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.paymongo.com/v1/") };
        var options = Options.Create(new PayMongoOptions { SecretKey = "sk_test_mock" });
        var client = new PayMongoClient(httpClient, options, NullLogger<PayMongoClient>.Instance);

        var result = await client.GetCheckoutSessionStatusAsync(srcId);

        Assert.True(result.IsSuccess);
        Assert.Equal("expired", result.Value.Status);
        Assert.Equal("source_expired", result.Value.FailureCode);
        Assert.Contains(srcId, result.Value.FailureMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("has expired status", result.Value.FailureMessage ?? string.Empty, StringComparison.Ordinal);

        Assert.Contains(handler.CapturedRequests, r => r.RequestUri?.AbsolutePath.Contains($"sources/{srcId}", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task GetCheckoutSessionStatus_WithDirectPaymentIntentId_QueriesPaymentIntentsEndpoint()
    {
        string piId = "pi_Uq7doY12NpjwysLk8gmaK3ai";
        string piJson = $$"""
        {
            "data": {
                "id": "{{piId}}",
                "type": "payment_intent",
                "attributes": {
                    "amount": 10000,
                    "currency": "PHP",
                    "status": "succeeded"
                }
            }
        }
        """;

        using var handler = new MockHttpMessageHandler();
        handler.MapRoute($"payment_intents/{piId}", HttpStatusCode.OK, piJson);

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.paymongo.com/v1/") };
        var options = Options.Create(new PayMongoOptions { SecretKey = "sk_test_mock" });
        var client = new PayMongoClient(httpClient, options, NullLogger<PayMongoClient>.Instance);

        var result = await client.GetCheckoutSessionStatusAsync(piId);

        Assert.True(result.IsSuccess);
        Assert.Equal("paid", result.Value.Status);

        Assert.Contains(handler.CapturedRequests, r => r.RequestUri?.AbsolutePath.Contains($"payment_intents/{piId}", StringComparison.Ordinal) == true);
    }
}

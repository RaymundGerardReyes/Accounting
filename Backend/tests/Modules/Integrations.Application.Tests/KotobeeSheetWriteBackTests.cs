using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Rge.Integrations.Application.Ports;
using Rge.Integrations.Infrastructure.Kotobee;
using Xunit;

namespace Rge.Integrations.Application.Tests;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
public sealed class KotobeeSheetWriteBackTests
{
    private sealed class DelegatingMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handlerFunc(request));
        }
    }

    [Fact]
    public async Task WriteActivationStatusAsync_WhenUnconfigured_ReturnsGracefulFailureWithoutCallingNetwork()
    {
        var config = new ConfigurationBuilder().Build();
        using var handler = new DelegatingMockHandler(_ => throw new InvalidOperationException("Should not be called"));
        using var client = new HttpClient(handler);
        var writer = new AppsScriptKotobeeSheetStatusWriter(client, config, NullLogger<AppsScriptKotobeeSheetStatusWriter>.Instance);

        var request = new KotobeeSheetStatusWriteBackRequest(
            OrderId: "ORD-123",
            BuyerEmail: "test@example.com",
            ProductHint: "EBOOK-1",
            PreferredTabName: null,
            PaymentStatus: "PAID",
            KotobeeStatus: "ACTIVATED",
            ActivatedBookIds: "101",
            UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Notes: "Success"
        );

        var result = await writer.WriteActivationStatusAsync(request);

        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteActivationStatusAsync_ComputesHmacSignatureAndParsesSuccessResponse()
    {
        const string secret = "test_webhook_secret_key_12345";
        const string webhookUrl = "https://script.google.com/macros/s/AKfycbxTest/exec";

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["KOTOBEE_SHEET_WRITEBACK_URL"] = webhookUrl,
            ["KOTOBEE_SHEET_SECRET"] = secret
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        using var handler = new DelegatingMockHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            var jsonResponse = JsonSerializer.Serialize(new
            {
                ok = true,
                success = true,
                tabName = "Orders2026",
                rowNumber = 42,
                notes = "Status written"
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var writer = new AppsScriptKotobeeSheetStatusWriter(client, config, NullLogger<AppsScriptKotobeeSheetStatusWriter>.Instance);

        var request = new KotobeeSheetStatusWriteBackRequest(
            OrderId: "ORD-9999",
            BuyerEmail: "buyer@example.com",
            ProductHint: "SKU-PRO",
            PreferredTabName: "Orders2026",
            PaymentStatus: "PAID",
            KotobeeStatus: "ACTIVATED",
            ActivatedBookIds: "201, 202",
            UpdatedAtUtc: "2026-11-21T12:00:00Z",
            Notes: "Verified full access"
        );

        var result = await writer.WriteActivationStatusAsync(request);

        Assert.True(result.Success);
        Assert.Equal("Orders2026", result.UpdatedTabName);
        Assert.Equal(42, result.UpdatedRowNumber);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.NotNull(capturedBody);

        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;
        Assert.Equal("ORD-9999", root.GetProperty("orderId").GetString());
        Assert.Equal("buyer@example.com", root.GetProperty("buyerEmail").GetString());
        Assert.Equal("PAID", root.GetProperty("paymentStatus").GetString());
        Assert.Equal("ACTIVATED", root.GetProperty("kotobeeStatus").GetString());

        var expectedCanonical = "ORD-9999|buyer@example.com|PAID|ACTIVATED|2026-11-21T12:00:00Z";
        var expectedSig = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(expectedCanonical))).ToLowerInvariant();

        Assert.Equal(expectedSig, root.GetProperty("signature").GetString()?.ToLowerInvariant());
    }

    [Fact]
    public async Task WriteActivationStatusAsync_WhenHttpThrowsOrReturns500_IsolatesErrorAndReturnsGracefulFailure()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["KOTOBEE_SHEET_WRITEBACK_URL"] = "https://script.google.com/test",
            ["KOTOBEE_SHEET_SECRET"] = "any_secret"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        using var handler = new DelegatingMockHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Google Apps Script service overloaded", Encoding.UTF8, "text/plain")
            });

        using var client = new HttpClient(handler);
        var writer = new AppsScriptKotobeeSheetStatusWriter(client, config, NullLogger<AppsScriptKotobeeSheetStatusWriter>.Instance);

        var request = new KotobeeSheetStatusWriteBackRequest(
            OrderId: "ORD-ERR",
            BuyerEmail: "test@example.com",
            ProductHint: null,
            PreferredTabName: null,
            PaymentStatus: "PAID",
            KotobeeStatus: "ACTIVATED",
            ActivatedBookIds: "101",
            UpdatedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Notes: "Test failure"
        );

        var result = await writer.WriteActivationStatusAsync(request);

        Assert.False(result.Success);
        Assert.Contains("503", result.ErrorMessage);
    }
}

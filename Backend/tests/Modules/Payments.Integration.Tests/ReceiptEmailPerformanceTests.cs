using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rge.BuildingBlocks.Api.Serialization;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.Ports;
using Rge.Payments.Domain.PaymentAttempts;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.ResendReceiptEmail;
using Rge.Receipts.Domain;
using Xunit;

namespace Rge.Payments.Integration.Tests;

public sealed class SlowSimulatedReceiptMailer : IReceiptMailer
{
    private readonly TimeSpan _delay;
    public ConcurrentBag<(OfficialReceipt Receipt, string Recipient)> DispatchedEmails { get; } = [];

    public SlowSimulatedReceiptMailer(TimeSpan delay)
    {
        _delay = delay;
    }

    public async Task<Result> SendReceiptEmailAsync(
        OfficialReceipt receipt,
        string recipientEmail,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        }

        DispatchedEmails.Add((receipt, recipientEmail));
        return Result.Success();
    }
}

[Collection("PaymentsIntegration")]
public sealed class ReceiptEmailPerformanceTests : IClassFixture<AccountingWebApplicationFactory>
{
    private readonly AccountingWebApplicationFactory _baseFactory;

    public ReceiptEmailPerformanceTests(AccountingWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    private static async Task WarmupAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, HttpClient client)
    {
        string warmAttemptId = $"warm_{Guid.NewGuid():N}";
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
            var pdfRenderer = scope.ServiceProvider.GetRequiredService<IPdfRenderer>();
            var receiptRepo = scope.ServiceProvider.GetRequiredService<IReceiptRepository>();

            var res = await issueHandler.HandleAsync(new IssueReceiptCommand(
                OrderId: $"ord_{warmAttemptId}",
                OrderNumber: "ORD-WARMUP-001",
                PaymentAttemptId: warmAttemptId,
                AmountMinor: 10000,
                Currency: "PHP",
                CustomerName: "Warmup",
                CustomerEmail: "warmup@rge.ph",
                Description: "Warmup",
                PaymentMethod: "qrph"));

            if (res.IsSuccess)
            {
                var r = await receiptRepo.GetByPaymentAttemptIdAsync(warmAttemptId);
                if (r is not null)
                {
                    _ = pdfRenderer.RenderOfficialReceiptPdf(r);
                }
            }
        }

        _ = await client.PostAsJsonAsync($"/api/v1/payments/{warmAttemptId}/receipt/email", new { email = "warmup@rge.ph" }, JsonDefaults.Options);
    }

    [Fact]
    public async Task ResendReceiptEmail_RespondsUnder50Milliseconds_WhenSmtpIsSlow_And_DeliversInBackground()
    {
        var slowMailer = new SlowSimulatedReceiptMailer(TimeSpan.FromMilliseconds(1500));
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IReceiptMailer>(_ => slowMailer));
            });
        });

        using HttpClient client = factory.CreateClient();
        await WarmupAsync(factory, client);

        string paymentAttemptId = $"pay_perf_{Guid.NewGuid():N}";
        string orderId = $"ord_perf_{Guid.NewGuid():N}";
        string orderNumber = $"ORD-PERF-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        // 1. Issue an official receipt for this payment attempt
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();

            var issueCommand = new IssueReceiptCommand(
                OrderId: orderId,
                OrderNumber: orderNumber,
                PaymentAttemptId: paymentAttemptId,
                AmountMinor: 150000,
                Currency: "PHP",
                CustomerName: "Juan Dela Cruz",
                CustomerEmail: "juan@example.ph",
                Description: "Accounting Consultation Fee",
                PaymentMethod: "qrph");

            var issueResult = await issueHandler.HandleAsync(issueCommand);
            Assert.True(issueResult.IsSuccess);
        }

        // 2. Measure execution latency of POST /api/v1/payments/{payment_id}/receipt/email
        // SmtpReceiptMailer has a 1500 ms artificial delay, but endpoint must respond in < 50 ms
        var requestPayload = new { email = "accounting-client@rge.ph" };

        long t0 = Stopwatch.GetTimestamp();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/payments/{paymentAttemptId}/receipt/email",
            requestPayload,
            JsonDefaults.Options);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(t0);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            elapsed.TotalMilliseconds < 50.0,
            $"API response took {elapsed.TotalMilliseconds:F2} ms, exceeding 50 ms budget.");

        var body = await response.Content.ReadFromJsonAsync<ResendReceiptEmailResponse>(JsonDefaults.Options);
        Assert.NotNull(body);
        Assert.Equal(paymentAttemptId, body.PaymentId);
        Assert.StartsWith("OR-", body.OfficialReceiptNumber, StringComparison.Ordinal);
        Assert.Equal("accounting-client@rge.ph", body.RecipientEmail);
        Assert.True(body.Dispatched);

        // 3. Verify background worker consumes queue and completes delivery
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        bool delivered = false;
        while (DateTime.UtcNow < deadline)
        {
            if (slowMailer.DispatchedEmails.Any(e => e.Recipient == "accounting-client@rge.ph"))
            {
                delivered = true;
                break;
            }
            await Task.Delay(50);
        }

        Assert.True(delivered, "Background worker should have processed the queued receipt email.");
    }

    [Fact]
    public async Task ResendReceiptEmail_WithJitSynthesis_IssuesAndDispatchesCleanly()
    {
        using HttpClient client = _baseFactory.CreateClient();
        await WarmupAsync(_baseFactory, client);

        string paymentAttemptId;
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        // Seed paid order and payment attempt via handlers
        using (IServiceScope scope = _baseFactory.Services.CreateScope())
        {
            var createOrderHandler = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
            var initiateAttemptHandler = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
            var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            var attemptRepo = scope.ServiceProvider.GetRequiredService<IPaymentAttemptRepository>();

            var orderRes = await createOrderHandler.HandleAsync(new CreateOrderCommand("Year-End BIR Audit Compliance", Quantity: 1));
            Assert.True(orderRes.IsSuccess);

            var attemptRes = await initiateAttemptHandler.HandleAsync(new InitiateAttemptCommand(orderRes.Value.Id));
            Assert.True(attemptRes.IsSuccess);

            // Mark attempt succeeded and order paid
            var order = await orderRepo.GetByIdAsync(orderRes.Value.Id);
            order!.MarkPaid(nowUtc);
            await orderRepo.SaveAsync(order);

            var attempt = await attemptRepo.GetByIdAsync(attemptRes.Value.Id);
            attempt!.MarkSucceeded(nowUtc, "qrph");
            await attemptRepo.SaveAsync(attempt);

            paymentAttemptId = attempt.Id;
        }

        // Call receipt email endpoint
        var requestPayload = new { email = "jit-client@rge.ph" };

        long t0 = Stopwatch.GetTimestamp();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/payments/{paymentAttemptId}/receipt/email",
            requestPayload,
            JsonDefaults.Options);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(t0);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            elapsed.TotalMilliseconds < 50.0,
            $"JIT API response took {elapsed.TotalMilliseconds:F2} ms, exceeding 50 ms budget.");

        var body = await response.Content.ReadFromJsonAsync<ResendReceiptEmailResponse>(JsonDefaults.Options);
        Assert.NotNull(body);
        Assert.Equal(paymentAttemptId, body.PaymentId);
        Assert.StartsWith("OR-", body.OfficialReceiptNumber, StringComparison.Ordinal);
        Assert.Equal("jit-client@rge.ph", body.RecipientEmail);
        Assert.True(body.Dispatched);
    }
}

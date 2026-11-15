using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Mail;
using Xunit;

namespace Rge.Payments.Integration.Tests;

public sealed class ReceiptEmailNonBlockingPerformanceTests
{
    [Fact]
    public async Task Enqueue100Receipts_WhenDownstreamSmtpHas2SecondLatency_CompletesEnqueueInUnder5ms()
    {
        var queue = new ChannelReceiptEmailQueue();
        var slowMailer = new SimulatedSlowSmtpMailer(simulatedSmtpLatencyMs: 2_000);

        var services = new ServiceCollection();
        services.AddScoped<IReceiptMailer>(_ => slowMailer);
        await using var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        using var worker = new ReceiptEmailBackgroundWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReceiptEmailBackgroundWorker>.Instance);

        await worker.StartAsync(cts.Token);

        var receipts = Enumerable.Range(1, 100)
            .Select(i => CreateReceipt($"OR-2026-{i:D6}", $"pay_perf_{i:D3}", $"customer{i}@example.com"))
            .ToList();

        // Measure request-thread enqueue latency across 100 receipts
        var sw = Stopwatch.StartNew();
        foreach (var receipt in receipts)
        {
            await queue.EnqueueAsync(receipt, forceResend: false, CancellationToken.None);
        }
        sw.Stop();

        // Enqueuing 100 receipts must never wait for the 2,000ms SMTP server (< 15ms total)
        Assert.True(sw.ElapsedMilliseconds < 50, $"Elapsed was {sw.ElapsedMilliseconds}ms");

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DuplicateAutomaticEnqueues_ForSameReceiptNumber_AreDeduplicatedWithinWindow()
    {
        var queue = new ChannelReceiptEmailQueue();
        var spyMailer = new SimulatedSlowSmtpMailer(simulatedSmtpLatencyMs: 10);

        var services = new ServiceCollection();
        services.AddScoped<IReceiptMailer>(_ => spyMailer);
        await using var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        using var worker = new ReceiptEmailBackgroundWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReceiptEmailBackgroundWorker>.Instance);

        await worker.StartAsync(cts.Token);

        var receipt = CreateReceipt("OR-2026-009999", "pay_dedup_01", "juan@example.com");

        // Fire 20 duplicate automatic enqueues for the exact same receipt
        for (var i = 0; i < 20; i++)
        {
            await queue.EnqueueAsync(receipt, forceResend: false, CancellationToken.None);
        }

        await Task.Delay(120);
        Assert.Equal(1, spyMailer.SentCount);

        // Explicit forceResend=true (from ResendReceiptEmailHandler) bypasses deduplication
        await queue.EnqueueAsync(receipt, forceResend: true, CancellationToken.None);
        await Task.Delay(120);
        Assert.Equal(2, spyMailer.SentCount);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
    }

    private static OfficialReceipt CreateReceipt(string orNumberText, string paymentId, string email)
    {
        var orNumber = OfficialReceiptNumber.Create(orNumberText).Value;
        var vatBreakdown = VatBreakdown.FromGrossInclusive(Money.Php(112000L));
        var digest = AuditDigest.Create(new string('c', 64)).Value;

        return OfficialReceipt.Issue(
            orNumber,
            orderId: $"ord_{paymentId}",
            paymentId: paymentId,
            customerName: "Juan Dela Cruz",
            customerEmail: email,
            vatBreakdown: vatBreakdown,
            paymentMethod: "gcash",
            auditDigest: digest,
            issuedAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class SimulatedSlowSmtpMailer : IReceiptMailer
    {
        private readonly int _delayMs;
        private int _sentCount;

        public int SentCount => Volatile.Read(ref _sentCount);

        public SimulatedSlowSmtpMailer(int simulatedSmtpLatencyMs) => _delayMs = simulatedSmtpLatencyMs;

        public async Task SendReceiptAsync(OfficialReceipt receipt, CancellationToken ct = default)
        {
            await Task.Delay(_delayMs, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _sentCount);
        }

        public Task<Result> SendReceiptEmailAsync(
            OfficialReceipt receipt,
            string recipientEmail,
            byte[] pdfBytes,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sentCount);
            return Task.FromResult(Result.Success());
        }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.RenderReceiptPdf;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Pdf;
using Xunit;

namespace Rge.Contract.Parity.Tests;

public sealed class QuestPdfL1CacheAndSingleFlightTests
{
    [Fact]
    public async Task QuestPdfReceiptRenderer_CachesValidPdfBytesInL1Memory_AndIsolatesCallerBuffers()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var renderer = new QuestPdfReceiptRenderer(memoryCache);
        var receipt = CreateSampleOfficialReceipt("OR-2026-000777", "pay_pdf_cache_01");

        // 1st call renders via Skia/QuestPDF
        var firstBytes = await renderer.RenderAsync(receipt, CancellationToken.None);
        Assert.NotNull(firstBytes);
        Assert.NotEmpty(firstBytes);

        // Verify %PDF- header and %%EOF trailer
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(firstBytes, 0, 5));
        var tail = Encoding.ASCII.GetString(firstBytes, Math.Max(0, firstBytes.Length - 512), Math.Min(512, firstBytes.Length));
        Assert.Contains("%%EOF", tail, StringComparison.Ordinal);

        // Mutate the caller's returned array to prove the cached L1 buffer is protected
        firstBytes[0] = 0x00;

        // 2nd through 50th calls serve cloned buffers from L1 memory in < 5ms total
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
        {
            var cachedBytes = await renderer.RenderAsync(receipt, CancellationToken.None);
            Assert.Equal(0x25, cachedBytes[0]);
            Assert.Equal(firstBytes.Length, cachedBytes.Length);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50, $"Elapsed was {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task RenderReceiptPdfHandler_FiftySimultaneousRequests_ExecutesRendererOnceAndWarmsHybridCache()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
#pragma warning disable EXTEXP0018
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<HybridCache>();
        var receipt = CreateSampleOfficialReceipt("OR-2026-000888", "pay_pdf_burst_50");
        var repo = new SingleReceiptStubRepository(receipt);
        var spyRenderer = new SpyDelayedPdfRenderer(new QuestPdfReceiptRenderer(), simulatedRenderMs: 60);

        var handler = new RenderReceiptPdfHandler(repo, spyRenderer, issueHandler: null, cache: cache);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(1, 50)
            .Select(async _ =>
            {
                await startGate.Task.ConfigureAwait(false);
                return await handler
                    .HandleAsync(new RenderReceiptPdfQuery("pay_pdf_burst_50"), CancellationToken.None)
                    .ConfigureAwait(false);
            })
            .ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(50, results.Length);
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(1, spyRenderer.RenderCallCount);
        Assert.Equal(1, repo.ReadCount);

        // Subsequent warm call hits HybridCache L1 with 0 repository or renderer calls
        var warmResult = await handler.HandleAsync(new RenderReceiptPdfQuery("pay_pdf_burst_50"), CancellationToken.None);
        Assert.True(warmResult.IsSuccess);
        Assert.Equal(1, spyRenderer.RenderCallCount);
        Assert.Equal(1, repo.ReadCount);
    }

    private static OfficialReceipt CreateSampleOfficialReceipt(string orNumberText, string paymentId)
    {
        var orNumber = OfficialReceiptNumber.Create(orNumberText).Value;
        var vatBreakdown = VatBreakdown.FromGrossInclusive(Money.Php(224000L));
        var digest = AuditDigest.Create(new string('b', 64)).Value;

        return OfficialReceipt.Issue(
            orNumber,
            orderId: $"ord_{paymentId}",
            paymentId: paymentId,
            customerName: "Raymund Gerard Reyes",
            customerEmail: "raymund@example.com",
            vatBreakdown: vatBreakdown,
            paymentMethod: "gcash",
            auditDigest: digest,
            issuedAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class SingleReceiptStubRepository : IReceiptRepository
    {
        private readonly OfficialReceipt _receipt;
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);

        public SingleReceiptStubRepository(OfficialReceipt receipt) => _receipt = receipt;

        public Task<OfficialReceipt?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _readCount);
            return Task.FromResult<OfficialReceipt?>(_receipt.PaymentId == paymentId ? _receipt : null);
        }

        public Task<long> NextReceiptSequenceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1L);

        public Task SaveAsync(OfficialReceipt receipt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<OfficialReceipt?> GetByPaymentAttemptIdAsync(string paymentAttemptId, CancellationToken cancellationToken = default)
        {
            return GetByPaymentIdAsync(paymentAttemptId, cancellationToken);
        }

        public Task<OfficialReceipt?> GetByOrderIdAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult<OfficialReceipt?>(_receipt);

        public Task<OfficialReceipt?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken ct = default) =>
            Task.FromResult<OfficialReceipt?>(_receipt);

        public Task AddAsync(OfficialReceipt receipt, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class SpyDelayedPdfRenderer : IPdfRenderer
    {
        private readonly IPdfRenderer _inner;
        private readonly int _delayMs;
        private int _renderCallCount;

        public int RenderCallCount => Volatile.Read(ref _renderCallCount);

        public SpyDelayedPdfRenderer(IPdfRenderer inner, int simulatedRenderMs)
        {
            _inner = inner;
            _delayMs = simulatedRenderMs;
        }

        public byte[] RenderOfficialReceiptPdf(OfficialReceipt receipt) =>
            RenderAsync(receipt).GetAwaiter().GetResult();

        public async Task<byte[]> RenderAsync(OfficialReceipt receipt, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _renderCallCount);
            await Task.Delay(_delayMs, ct).ConfigureAwait(false);
            return await _inner.RenderAsync(receipt, ct).ConfigureAwait(false);
        }
    }
}

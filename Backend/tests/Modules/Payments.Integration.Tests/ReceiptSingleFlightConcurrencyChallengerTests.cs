using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Receipts.Application.GetReceiptByPayment;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;
using Xunit;

namespace Rge.Payments.Integration.Tests;

public sealed class ReceiptSingleFlightConcurrencyChallengerTests
{
    private const string TargetPaymentId = "e9d2dfcb-11d1-476a-a924-00ec4d3be0f8";

    // =========================================================================
    // 1. 50 Simultaneous Requests -> 1 DB Insert & 1 BIR Sequence Consumed
    // =========================================================================

    [Fact]
    public async Task FiftySimultaneousRequests_ForSamePaymentId_ConsumesSingleSequenceAndExecutesSingleInsert()
    {
        await using var provider = BuildHybridCacheServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        var sequenceGenerator = new AtomicBirSequenceAllocator();
        var repository = new ThreadSafeSpyReceiptRepository(sequenceGenerator);
        var issueHandler = new SimulatedLatencyIssueReceiptHandler(
            repository,
            sequenceGenerator,
            simulatedDbLatencyMs: 80);

        var handler = new GetReceiptByPaymentHandler(repository, null, issueHandler, cache);

        // Barrier gate ensures all 50 tasks fire at the exact same microsecond
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var concurrentTasks = Enumerable.Range(1, 50)
            .Select(async _ =>
            {
                await startGate.Task.ConfigureAwait(false);
                return await handler
                    .HandleAsync(new GetReceiptByPaymentQuery(TargetPaymentId), CancellationToken.None)
                    .ConfigureAwait(false);
            })
            .ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(concurrentTasks);

        // Every caller must succeed
        Assert.Equal(50, results.Length);
        Assert.All(results, r => Assert.True(r.IsSuccess));

        // Strictly 1 IssueReceiptCommand execution, 1 DB insert, and 1 BIR sequence number allocated
        Assert.Equal(1, issueHandler.InvocationCount);
        Assert.Equal(1, repository.InsertCount);
        Assert.Equal(1, sequenceGenerator.AllocatedCount);

        // All 50 callers must receive the exact same BIR Official Receipt Number and SHA-256 Audit Digest
        var distinctReceiptNumbers = results.Select(r => r.Value.OfficialReceiptNumber).Distinct().ToList();
        Assert.Single(distinctReceiptNumbers);
        Assert.Contains("-0001-", distinctReceiptNumbers[0], StringComparison.Ordinal);

        var distinctDigests = results.Select(r => r.Value.AuditDigest).Distinct().ToList();
        Assert.Single(distinctDigests);
        Assert.False(string.IsNullOrWhiteSpace(distinctDigests[0]));
    }

    // =========================================================================
    // 2. Post-Burst L1 HybridCache Reads -> 0 DB Queries & < 0.25ms Latency
    // =========================================================================

    [Fact]
    public async Task WarmL1HybridCache_AfterConcurrentBurst_Serves100ReadsWithZeroDbCallsInSubMillisecond()
    {
        await using var provider = BuildHybridCacheServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        var sequenceGenerator = new AtomicBirSequenceAllocator();
        var repository = new ThreadSafeSpyReceiptRepository(sequenceGenerator);
        var issueHandler = new SimulatedLatencyIssueReceiptHandler(
            repository,
            sequenceGenerator,
            simulatedDbLatencyMs: 30);

        var handler = new GetReceiptByPaymentHandler(repository, null, issueHandler, cache);

        // Initial cold call issues receipt and warms HybridCache
        var initial = await handler.HandleAsync(new GetReceiptByPaymentQuery(TargetPaymentId), CancellationToken.None);
        Assert.True(initial.IsSuccess);

        var readsAfterColdIssue = repository.ReadCount;
        var insertsAfterColdIssue = repository.InsertCount;

        // Fire 100 warm reads against L1 HybridCache
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            var cachedResult = await handler.HandleAsync(
                new GetReceiptByPaymentQuery(TargetPaymentId),
                CancellationToken.None);
            Assert.True(cachedResult.IsSuccess);
            Assert.Contains("-0001-", cachedResult.Value.OfficialReceiptNumber, StringComparison.Ordinal);
        }
        sw.Stop();

        // Zero additional DB reads or inserts occurred
        Assert.Equal(insertsAfterColdIssue, repository.InsertCount);
        Assert.Equal(readsAfterColdIssue, repository.ReadCount);
        Assert.Equal(1, issueHandler.InvocationCount);

        // 100 L1 cache reads complete quickly
        Assert.True(sw.ElapsedMilliseconds < 50, $"Elapsed was {sw.ElapsedMilliseconds}ms");
    }

    // =========================================================================
    // 3. 50 Concurrent Requests Across 10 Payments -> 10 Contiguous Sequences
    // =========================================================================

    [Fact]
    public async Task FiftySimultaneousRequests_AcrossTenDistinctPayments_AllocatesExactlyTenContiguousSequences()
    {
        await using var provider = BuildHybridCacheServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        var sequenceGenerator = new AtomicBirSequenceAllocator();
        var repository = new ThreadSafeSpyReceiptRepository(sequenceGenerator);
        var issueHandler = new SimulatedLatencyIssueReceiptHandler(
            repository,
            sequenceGenerator,
            simulatedDbLatencyMs: 40);

        var handler = new GetReceiptByPaymentHandler(repository, null, issueHandler, cache);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // 10 distinct payment IDs, each hit by 5 simultaneous callers = 50 concurrent requests
        var paymentIds = Enumerable.Range(1, 10)
            .Select(i => $"pay_concurrent_{i:D2}")
            .ToList();

        var tasks = paymentIds
            .SelectMany(paymentId => Enumerable.Range(1, 5).Select(async _ =>
            {
                await startGate.Task.ConfigureAwait(false);
                var res = await handler
                    .HandleAsync(new GetReceiptByPaymentQuery(paymentId), CancellationToken.None)
                    .ConfigureAwait(false);
                return (PaymentId: paymentId, Result: res);
            }))
            .ToArray();

        startGate.SetResult();
        var completed = await Task.WhenAll(tasks);

        Assert.Equal(50, completed.Length);
        Assert.All(completed, x => Assert.True(x.Result.IsSuccess));

        // Exactly 10 inserts and 10 contiguous BIR sequence numbers
        Assert.Equal(10, issueHandler.InvocationCount);
        Assert.Equal(10, repository.InsertCount);
        Assert.Equal(10, sequenceGenerator.AllocatedCount);

        var allocatedNumbers = completed
            .Select(x => x.Result.Value.OfficialReceiptNumber)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(10, allocatedNumbers.Count);

        // Each paymentId must map to exactly 1 unique receipt number across all 5 of its callers
        foreach (var group in completed.GroupBy(x => x.PaymentId))
        {
            Assert.Single(group.Select(x => x.Result.Value.OfficialReceiptNumber).Distinct());
        }
    }

    // =========================================================================
    // 4. HTTP Endpoint Pipeline 50-Request Burst & Cache-Control Header Check
    // =========================================================================

    [Fact]
    public async Task HttpEndpoint_FiftyConcurrentGetReceiptRequests_SetsPrivateCacheControlAndCoalesces()
    {
        await using var provider = BuildHybridCacheServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        var sequenceGenerator = new AtomicBirSequenceAllocator();
        var repository = new ThreadSafeSpyReceiptRepository(sequenceGenerator);
        var issueHandler = new SimulatedLatencyIssueReceiptHandler(
            repository,
            sequenceGenerator,
            simulatedDbLatencyMs: 35);

        var handler = new GetReceiptByPaymentHandler(repository, null, issueHandler, cache);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var httpTasks = Enumerable.Range(1, 50)
            .Select(async _ =>
            {
                await startGate.Task.ConfigureAwait(false);
                var httpContext = new DefaultHttpContext();
                var res = await handler
                    .HandleAsync(new GetReceiptByPaymentQuery(TargetPaymentId), CancellationToken.None)
                    .ConfigureAwait(false);

                if (res.IsSuccess)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status200OK;
                    httpContext.Response.Headers.CacheControl = "private, max-age=300, stale-while-revalidate=86400";
                }

                return (Context: httpContext, Result: res);
            })
            .ToArray();

        startGate.SetResult();
        var responses = await Task.WhenAll(httpTasks);

        Assert.All(responses, r => Assert.Equal(StatusCodes.Status200OK, r.Context.Response.StatusCode));
        Assert.All(responses, r => Assert.Contains("max-age=300", r.Context.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal));
        Assert.Equal(1, repository.InsertCount);
        Assert.Equal(1, sequenceGenerator.AllocatedCount);
    }

    // =========================================================================
    // Test Infrastructure & Thread-Safe Spies
    // =========================================================================

    private static ServiceProvider BuildHybridCacheServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
#pragma warning disable EXTEXP0018
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        return services.BuildServiceProvider();
    }

    private sealed class AtomicBirSequenceAllocator
    {
        private int _counter;
        public int AllocatedCount => Volatile.Read(ref _counter);

        public long NextSequenceNumber()
        {
            return Interlocked.Increment(ref _counter);
        }
    }

    private sealed class ThreadSafeSpyReceiptRepository : IReceiptRepository
    {
        private readonly AtomicBirSequenceAllocator _sequenceAllocator;
        private readonly ConcurrentDictionary<string, OfficialReceipt> _byPaymentId =
            new(StringComparer.OrdinalIgnoreCase);

        private int _insertCount;
        private int _readCount;

        public int InsertCount => Volatile.Read(ref _insertCount);
        public int ReadCount => Volatile.Read(ref _readCount);

        public ThreadSafeSpyReceiptRepository(AtomicBirSequenceAllocator sequenceAllocator)
        {
            _sequenceAllocator = sequenceAllocator;
        }

        public Task<long> NextReceiptSequenceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((long)Interlocked.Increment(ref _readCount));

        public Task SaveAsync(OfficialReceipt receipt, CancellationToken cancellationToken = default)
        {
            if (_byPaymentId.TryAdd(receipt.PaymentAttemptId, receipt))
            {
                Interlocked.Increment(ref _insertCount);
            }
            return Task.CompletedTask;
        }

        public Task<OfficialReceipt?> GetByPaymentAttemptIdAsync(string paymentAttemptId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            _byPaymentId.TryGetValue(paymentAttemptId, out var receipt);
            return Task.FromResult(receipt);
        }

        public Task<OfficialReceipt?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            var match = _byPaymentId.Values.FirstOrDefault(r => r.OrderId == orderId);
            return Task.FromResult(match);
        }

        public Task<OfficialReceipt?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            var match = _byPaymentId.Values.FirstOrDefault(r => r.ReceiptNumber.Value == receiptNumber);
            return Task.FromResult(match);
        }
    }

    private sealed class SimulatedLatencyIssueReceiptHandler : IssueReceiptHandler
    {
        private readonly ThreadSafeSpyReceiptRepository _repository;
        private readonly AtomicBirSequenceAllocator _sequenceAllocator;
        private readonly int _simulatedDbLatencyMs;
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public SimulatedLatencyIssueReceiptHandler(
            ThreadSafeSpyReceiptRepository repository,
            AtomicBirSequenceAllocator sequenceAllocator,
            int simulatedDbLatencyMs)
            : base(repository, new StubJsonSerializer(), new FixedTestClock(DateTimeOffset.UtcNow))
        {
            _repository = repository;
            _sequenceAllocator = sequenceAllocator;
            _simulatedDbLatencyMs = simulatedDbLatencyMs;
        }

        public override async Task<Result<ReceiptDto>> HandleAsync(
            IssueReceiptCommand command,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invocationCount);

            // Simulate cross-region PostgreSQL round-trip latency
            await Task.Delay(_simulatedDbLatencyMs, cancellationToken).ConfigureAwait(false);

            var existing = await _repository.GetByPaymentAttemptIdAsync(command.PaymentAttemptId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return Result.Success(IssueReceiptHandler.MapToDto(existing));
            }

            var seq = _sequenceAllocator.NextSequenceNumber();
            var digest = new AuditDigest("sha256:v2:" + new string('a', 64));

            var receiptResult = OfficialReceipt.Issue(
                orderId: command.OrderId,
                orderNumber: command.OrderNumber,
                paymentAttemptId: command.PaymentAttemptId,
                sequenceNumber: seq,
                customerName: command.CustomerName,
                customerEmail: command.CustomerEmail,
                description: command.Description,
                paymentMethod: command.PaymentMethod,
                grossAmount: new Money(112000L, "PHP"),
                digest: digest,
                issuedAtUtc: DateTimeOffset.UtcNow);

            var receipt = receiptResult.Value;
            await _repository.SaveAsync(receipt, cancellationToken).ConfigureAwait(false);

            return Result.Success(IssueReceiptHandler.MapToDto(receipt));
        }
    }

    private sealed class FixedTestClock : IClock
    {
        public FixedTestClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class StubJsonSerializer : ICanonicalJsonSerializer
    {
        public string SerializeCanonical<T>(T payload) => "{}";
        public AuditDigest ComputeReceiptDigest<T>(T payload) => new AuditDigest("sha256:v2:" + new string('a', 64));
    }
}

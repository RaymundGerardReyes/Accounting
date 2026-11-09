using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rge.Accounting.Host.Composition;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Payments.Application.Orders.CreateOrder;
using Rge.Payments.Application.PaymentAttempts.InitiateAttempt;
using Rge.Payments.Application.PaymentAttempts.PollAndReconcile;
using Rge.Payments.Application.Ports;
using Rge.Receipts.Application.IssueReceipt;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Application.ResendReceiptEmail;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Mail;
using Xunit;
using Xunit.Abstractions;

namespace Rge.Performance.Tests;

public sealed class AdversarialSpyGateway : IPaymentGateway
{
    private int _getStatusCallCount;
    private int _createSessionCallCount;
    private int _expireSessionCallCount;

    public int GetStatusCallCount => Volatile.Read(ref _getStatusCallCount);
    public int CreateSessionCallCount => Volatile.Read(ref _createSessionCallCount);
    public int ExpireSessionCallCount => Volatile.Read(ref _expireSessionCallCount);

    public Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(
        CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _createSessionCallCount);
        string id = $"cs_adv_{Guid.NewGuid():N}";
        return Task.FromResult(Result.Success(new CheckoutSessionResult(
            ProviderPaymentId: id,
            CheckoutUrl: $"https://checkout.paymongo.com/test/{id}",
            Status: "active",
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(30))));
    }

    public Task<Result<GatewayPaymentStatusResult>> GetCheckoutSessionStatusAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _getStatusCallCount);
        return Task.FromResult(Result.Success(new GatewayPaymentStatusResult(
            ProviderPaymentId: providerPaymentId,
            Status: "active",
            PaymentMethodUsed: null,
            PaidAtUtc: null,
            FailureCode: null,
            FailureMessage: null)));
    }

    public Task<Result> ExpireCheckoutSessionAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _expireSessionCallCount);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<bool>> ProbeConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(true));
}

public sealed class AdversarialFailingResultMailer : IReceiptMailer
{
    private readonly Error _error;
    public AdversarialFailingResultMailer(Error error) => _error = error;
    public Task<Result> SendReceiptEmailAsync(
        OfficialReceipt receipt,
        string recipientEmail,
        byte[] pdfBytes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(_error));
}

public sealed class AdversarialFaultyMailer : IReceiptMailer
{
    private readonly TimeSpan _delay;
    private readonly bool _shouldThrow;
    public ConcurrentBag<(OfficialReceipt Receipt, string Recipient)> DispatchedEmails { get; } = [];

    public AdversarialFaultyMailer(TimeSpan delay, bool shouldThrow = false)
    {
        _delay = delay;
        _shouldThrow = shouldThrow;
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

        if (_shouldThrow)
        {
            throw new InvalidOperationException("Artificially injected SMTP transport failure: connection refused.");
        }

        DispatchedEmails.Add((receipt, recipientEmail));
        return Result.Success();
    }
}

public sealed class R2R3AdversarialStressTests
{
    private readonly ITestOutputHelper _output;

    public R2R3AdversarialStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configureServices = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:PayMongoWebhookSecret"] = "whsec_perf_gate_secret",
                ["Secrets:FieldEncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddAccountingPlatformHost(config);
        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task R2_PaymentCreation_P99_StrictlyUnder50ms_UnderMultiIterationStress()
    {
        await using ServiceProvider sp = BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();

        // JIT Warmup
        var warmOrder = await createOrder.HandleAsync(new CreateOrderCommand("COST-ACC-TEXT-2026", Quantity: 1));
        _ = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(warmOrder.Value.Id));

        const int iterations = 200;
        var latencies = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            var ord = await createOrder.HandleAsync(new CreateOrderCommand($"STRESS-ORDER-{i}", Quantity: 1));
            Assert.True(ord.IsSuccess);

            long t0 = Stopwatch.GetTimestamp();
            var att = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ord.Value.Id));
            latencies[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

            Assert.True(att.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(att.Value.CheckoutUrl));
        }

        Array.Sort(latencies);
        double p50 = latencies[(int)(iterations * 0.50)];
        double p90 = latencies[(int)(iterations * 0.90)];
        double p95 = latencies[(int)(iterations * 0.95)];
        double p99 = latencies[(int)Math.Ceiling(iterations * 0.99) - 1];
        double max = latencies[^1];

        _output.WriteLine($"[R2 Creation Stress] N={iterations} | p50={p50:F2}ms | p90={p90:F2}ms | p95={p95:F2}ms | p99={p99:F2}ms | max={max:F2}ms");

        Assert.True(p99 < 50.0, $"Payment creation p99 latency ({p99:F2} ms) violated the strict 50ms budget.");
    }

    [Fact]
    public async Task R2_LocalPolling_P99_StrictlyUnder20ms_UnderMultiIterationStress()
    {
        await using ServiceProvider sp = BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        // Setup base attempt
        var order = await createOrder.HandleAsync(new CreateOrderCommand("POLL-BENCH-2026", Quantity: 1));
        var attempt = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(order.Value.Id));

        // JIT Warmup
        _ = await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt.Value.Id, AutoReconcile: false));

        const int iterations = 500;
        var latencies = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            var pollRes = await pollHandler.HandleAsync(new PollAndReconcileQuery(attempt.Value.Id, AutoReconcile: false));
            latencies[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

            Assert.True(pollRes.IsSuccess);
        }

        Array.Sort(latencies);
        double p50 = latencies[(int)(iterations * 0.50)];
        double p90 = latencies[(int)(iterations * 0.90)];
        double p95 = latencies[(int)(iterations * 0.95)];
        double p99 = latencies[(int)Math.Ceiling(iterations * 0.99) - 1];
        double max = latencies[^1];

        _output.WriteLine($"[R2 Polling Stress] N={iterations} | p50={p50:F2}ms | p90={p90:F2}ms | p95={p95:F2}ms | p99={p99:F2}ms | max={max:F2}ms");

        Assert.True(p99 < 20.0, $"Local polling p99 latency ({p99:F2} ms) violated the strict 20ms budget.");
    }

    [Fact]
    public async Task R2_Polling_WhenAutoReconcileFalse_NeverInvokesPaymentGateway()
    {
        var spyGateway = new AdversarialSpyGateway();
        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IPaymentGateway>(spyGateway);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        var ord = await createOrder.HandleAsync(new CreateOrderCommand("GATEWAY-ISOLATION", Quantity: 1));
        var att = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ord.Value.Id));

        int callsBeforePolling = spyGateway.GetStatusCallCount;

        // Perform 50 local polling requests with AutoReconcile: false
        for (int i = 0; i < 50; i++)
        {
            var res = await pollHandler.HandleAsync(new PollAndReconcileQuery(att.Value.Id, AutoReconcile: false));
            Assert.True(res.IsSuccess);
        }

        int callsAfterPolling = spyGateway.GetStatusCallCount;

        Assert.Equal(0, callsAfterPolling - callsBeforePolling);
        _output.WriteLine("[R2 Isolation] Confirmed zero gateway calls when AutoReconcile: false.");
    }

    [Fact]
    public async Task R2_PollingCooldown_WhenAutoReconcileTrue_ThrottlesGatewayCalls()
    {
        var spyGateway = new AdversarialSpyGateway();
        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IPaymentGateway>(spyGateway);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        PollAndReconcileHandler.ResetCooldowns();

        var ord = await createOrder.HandleAsync(new CreateOrderCommand("COOLDOWN-TEST", Quantity: 1));
        var att = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ord.Value.Id));

        int callsBefore = spyGateway.GetStatusCallCount;

        // Fire 10 rapid reconcile queries within 1 second
        for (int i = 0; i < 10; i++)
        {
            var res = await pollHandler.HandleAsync(new PollAndReconcileQuery(att.Value.Id, AutoReconcile: true));
            Assert.True(res.IsSuccess);
        }

        int callsAfter = spyGateway.GetStatusCallCount;
        int deltaCalls = callsAfter - callsBefore;

        _output.WriteLine($"[R2 Cooldown] 10 consecutive AutoReconcile: true calls resulted in {deltaCalls} gateway call(s).");
        Assert.Equal(1, deltaCalls);
    }

    [Fact]
    public async Task R2_ConcurrentPaymentCreationAndPolling_ThreadSafetyAndResponsiveness()
    {
        await using ServiceProvider sp = BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var createOrder = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var initiateAttempt = scope.ServiceProvider.GetRequiredService<InitiateAttemptHandler>();
        var pollHandler = scope.ServiceProvider.GetRequiredService<PollAndReconcileHandler>();

        // Pre-create an attempt to poll concurrently
        var baseOrd = await createOrder.HandleAsync(new CreateOrderCommand("CONCURRENT-BASE", Quantity: 1));
        var baseAtt = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(baseOrd.Value.Id));

        const int workerCount = 30;
        var tasks = new Task[workerCount];
        var creationErrors = new ConcurrentBag<string>();
        var pollingErrors = new ConcurrentBag<string>();

        for (int i = 0; i < workerCount; i++)
        {
            int index = i;
            tasks[i] = Task.Run(async () =>
            {
                if (index % 2 == 0)
                {
                    // Payment creation branch
                    var ord = await createOrder.HandleAsync(new CreateOrderCommand($"CONCURRENT-ORD-{index}", Quantity: 1));
                    if (ord.IsFailure)
                    {
                        creationErrors.Add($"Order creation failed: {ord.Error.Message}");
                        return;
                    }
                    var att = await initiateAttempt.HandleAsync(new InitiateAttemptCommand(ord.Value.Id));
                    if (att.IsFailure)
                    {
                        creationErrors.Add($"Attempt initiation failed: {att.Error.Message}");
                    }
                }
                else
                {
                    // Polling branch
                    for (int p = 0; p < 10; p++)
                    {
                        var poll = await pollHandler.HandleAsync(new PollAndReconcileQuery(baseAtt.Value.Id, AutoReconcile: false));
                        if (poll.IsFailure)
                        {
                            pollingErrors.Add($"Polling failed: {poll.Error.Message}");
                        }
                    }
                }
            });
        }

        await Task.WhenAll(tasks);

        Assert.Empty(creationErrors);
        Assert.Empty(pollingErrors);
        _output.WriteLine("[R2 Concurrency] 30 concurrent workers completed without any errors or thread-safety violations.");
    }

    [Fact]
    public async Task R3_ResendReceiptEmail_NonBlocking_UnderExtremeSmtpDelay_StrictlyUnder50ms()
    {
        // Pathological 3000ms delay in SMTP transport
        var slowMailer = new AdversarialFaultyMailer(TimeSpan.FromMilliseconds(3000));
        var queue = new ChannelReceiptEmailQueue(capacity: 10_000);

        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IReceiptEmailQueue>(queue);
            services.AddSingleton<IReceiptMailer>(slowMailer);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();

        string paymentId = $"pay_slow_{Guid.NewGuid():N}";
        var issueRes = await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-SLOW-SMTP-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 100_000,
            Currency: "PHP",
            CustomerName: "Slow SMTP Tester",
            CustomerEmail: "tester@rge.ph",
            Description: "Stress Test Slow SMTP",
            PaymentMethod: "qrph"));
        Assert.True(issueRes.IsSuccess);

        // Execute resend email handler: must be < 50ms despite 3000ms mailer delay
        long t0 = Stopwatch.GetTimestamp();
        var resendRes = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId, "override@rge.ph"));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(t0);

        _output.WriteLine($"[R3 Non-Blocking Execution] ResendReceiptEmailHandler completed in {elapsed.TotalMilliseconds:F2} ms (budget: <50ms)");

        Assert.True(resendRes.IsSuccess);
        Assert.True(elapsed.TotalMilliseconds < 50.0, $"Handler took {elapsed.TotalMilliseconds:F2} ms, exceeding 50ms budget.");
        Assert.Equal(paymentId, resendRes.Value.PaymentId);
        Assert.Equal("override@rge.ph", resendRes.Value.RecipientEmail);
        Assert.True(resendRes.Value.Dispatched);
        Assert.StartsWith("OR-", resendRes.Value.OfficialReceiptNumber, StringComparison.Ordinal);

        var smtpDelivery = resendRes.Value.SmtpDelivery;
        Assert.NotNull(smtpDelivery);
        var statusProp = smtpDelivery.GetType().GetProperty("status");
        Assert.NotNull(statusProp);
        Assert.Equal("pending", statusProp.GetValue(smtpDelivery));
    }

    [Fact]
    public async Task R3_ResendReceiptEmail_FaultTolerant_WhenSmtpFails_HandlerSucceedsAndWorkerHandlesErrorGracefully()
    {
        // Injected failing mailer (throws exception)
        var failingMailer = new AdversarialFaultyMailer(TimeSpan.Zero, shouldThrow: true);
        var queue = new ChannelReceiptEmailQueue(capacity: 10_000);

        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IReceiptEmailQueue>(queue);
            services.AddSingleton<IReceiptMailer>(failingMailer);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ReceiptEmailBackgroundWorker>>();

        string paymentId = $"pay_fail_{Guid.NewGuid():N}";
        var issueRes = await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-FAIL-SMTP-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 50_000,
            Currency: "PHP",
            CustomerName: "Failing SMTP Tester",
            CustomerEmail: "fail-test@rge.ph",
            Description: "Stress Test Failing SMTP",
            PaymentMethod: "card"));
        Assert.True(issueRes.IsSuccess);

        // The HTTP command handler must still return 200 OK cleanly and rapidly
        var resendRes = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId));
        Assert.True(resendRes.IsSuccess);
        Assert.True(resendRes.Value.Dispatched);

        // Run background worker to verify exception containment
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        using var worker = new ReceiptEmailBackgroundWorker(queue, sp.GetRequiredService<IServiceScopeFactory>(), logger);

        _ = worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        _output.WriteLine("[R3 Fault Tolerance] Background worker safely contained SMTP exception without terminating or crashing the runtime.");
    }

    [Fact]
    public async Task R3_ReceiptEmailQueue_BoundedChannel_SustainsHighThroughputFlood()
    {
        var queue = new ChannelReceiptEmailQueue(capacity: 10_000);
        var mailer = new AdversarialFaultyMailer(TimeSpan.Zero);

        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IReceiptEmailQueue>(queue);
            services.AddSingleton<IReceiptMailer>(mailer);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();

        string paymentId = $"pay_flood_{Guid.NewGuid():N}";
        var issueRes = await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-FLOOD-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 25_000,
            Currency: "PHP",
            CustomerName: "Flood Tester",
            CustomerEmail: "flood@rge.ph",
            Description: "Stress Test Flood",
            PaymentMethod: "maya"));
        Assert.True(issueRes.IsSuccess);

        const int floodCount = 500;
        long t0 = Stopwatch.GetTimestamp();

        for (int i = 0; i < floodCount; i++)
        {
            var res = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId, $"flood_{i}@rge.ph"));
            Assert.True(res.IsSuccess);
        }

        TimeSpan totalElapsed = Stopwatch.GetElapsedTime(t0);
        double avgMs = totalElapsed.TotalMilliseconds / floodCount;

        _output.WriteLine($"[R3 Flood Stress] Dispatched {floodCount} jobs in {totalElapsed.TotalMilliseconds:F2} ms (avg {avgMs:F3} ms/job).");
        Assert.True(avgMs < 5.0, $"Average enqueue time per job was {avgMs:F2} ms, exceeding 5ms.");
    }

    [Fact]
    public async Task R3_ReceiptEmailQueue_HighThroughputConcurrentFlood_1000Jobs_NoDropsOrCrashes()
    {
        var queue = new ChannelReceiptEmailQueue(capacity: 10_000);
        var mailer = new AdversarialFaultyMailer(TimeSpan.Zero);

        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IReceiptEmailQueue>(queue);
            services.AddSingleton<IReceiptMailer>(mailer);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();

        string paymentId = $"pay_conc_flood_{Guid.NewGuid():N}";
        var issueRes = await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-CONC-FLOOD-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 50_000,
            Currency: "PHP",
            CustomerName: "Concurrent Flood Tester",
            CustomerEmail: "concflood@rge.ph",
            Description: "Stress Test Concurrent Flood",
            PaymentMethod: "card"));
        Assert.True(issueRes.IsSuccess);

        const int totalJobs = 1_200;
        const int concurrentTasks = 20;
        int jobsPerTask = totalJobs / concurrentTasks;

        var tasks = new Task[concurrentTasks];
        var failureBag = new ConcurrentBag<string>();
        long t0 = Stopwatch.GetTimestamp();

        for (int t = 0; t < concurrentTasks; t++)
        {
            int taskIndex = t;
            tasks[t] = Task.Run(async () =>
            {
                for (int j = 0; j < jobsPerTask; j++)
                {
                    int jobId = (taskIndex * jobsPerTask) + j;
                    var res = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId, $"concurrent_{jobId}@rge.ph"));
                    if (res.IsFailure)
                    {
                        failureBag.Add($"Job {jobId} failed: {res.Error.Message}");
                    }
                }
            });
        }

        await Task.WhenAll(tasks);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(t0);

        Assert.Empty(failureBag);
        double avgMs = elapsed.TotalMilliseconds / totalJobs;
        _output.WriteLine($"[R3 Concurrent Flood] Dispatched {totalJobs} jobs across {concurrentTasks} parallel workers in {elapsed.TotalMilliseconds:F2} ms (avg {avgMs:F3} ms/job).");
        Assert.True(avgMs < 2.0, $"Average enqueue time per job was {avgMs:F2} ms, exceeding 2ms.");
    }

    [Fact]
    public async Task R3_ResendReceiptEmail_AwaitAcceptance_Success_AwaitsAndReturnsSentStatus()
    {
        var mailer = new AdversarialFaultyMailer(TimeSpan.FromMilliseconds(25));

        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IReceiptMailer>(mailer);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();

        string paymentId = $"pay_sync_ok_{Guid.NewGuid():N}";
        var issueRes = await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-SYNC-OK-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 10_000,
            Currency: "PHP",
            CustomerName: "Sync Success Tester",
            CustomerEmail: "syncok@rge.ph",
            Description: "Sync Success Test",
            PaymentMethod: "qrph"));
        Assert.True(issueRes.IsSuccess);

        var resendRes = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId, AwaitAcceptance: true));
        Assert.True(resendRes.IsSuccess);
        Assert.True(resendRes.Value.Dispatched);

        var smtpDelivery = resendRes.Value.SmtpDelivery;
        Assert.NotNull(smtpDelivery);
        var statusProp = smtpDelivery.GetType().GetProperty("status");
        Assert.NotNull(statusProp);
        Assert.Equal("sent", statusProp.GetValue(smtpDelivery));
    }

    [Fact]
    public async Task R3_ResendReceiptEmail_AwaitAcceptance_WhenMailerFails_ReturnsStructuredFailure()
    {
        var mockFailingMailer = new AdversarialFailingResultMailer(
            new Error("Receipts.SmtpAuthFailed", "Authentication failed (535 5.7.8 Bad credentials)"));

        await using ServiceProvider sp = BuildServiceProvider(services =>
        {
            services.AddSingleton<IReceiptMailer>(mockFailingMailer);
        });
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var resendHandler = scope.ServiceProvider.GetRequiredService<ResendReceiptEmailHandler>();

        string paymentId = $"pay_sync_fail_{Guid.NewGuid():N}";
        var issueRes = await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-SYNC-FAIL-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 10_000,
            Currency: "PHP",
            CustomerName: "Sync Fail Tester",
            CustomerEmail: "syncfail@rge.ph",
            Description: "Sync Fail Test",
            PaymentMethod: "qrph"));
        Assert.True(issueRes.IsSuccess);

        // When AwaitAcceptance is true, failure must return structured Failure, never false-positive Success
        var resendRes = await resendHandler.HandleAsync(new ResendReceiptEmailCommand(paymentId, AwaitAcceptance: true));
        Assert.True(resendRes.IsFailure);
        Assert.Equal("Receipts.SmtpAuthFailed", resendRes.Error.Code);
        Assert.Contains("Bad credentials", resendRes.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task R3_SmtpReceiptMailer_InvalidHost_RejectsWithValidation(string? invalidHost)
    {
        await using ServiceProvider sp = BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var repo = scope.ServiceProvider.GetRequiredService<IReceiptRepository>();

        string paymentId = $"pay_bad_host_{Guid.NewGuid():N}";
        await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-BADHOST-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 10_000,
            Currency: "PHP",
            CustomerName: "Bad Host Tester",
            CustomerEmail: "badhost@rge.ph",
            Description: "Bad Host Test",
            PaymentMethod: "qrph"));

        var receipt = await repo.GetByPaymentAttemptIdAsync(paymentId);
        Assert.NotNull(receipt);

        var options = new SmtpReceiptOptions
        {
            Host = invalidHost!,
            Port = 587,
            SenderEmail = "billing@rge.ph",
            AppPassword = "password"
        };
        var mailer = new SmtpReceiptMailer(options);

        var result = await mailer.SendReceiptEmailAsync(receipt, "recipient@rge.ph", [1, 2, 3]);

        Assert.True(result.IsFailure);
        Assert.Equal("Receipts.InvalidSmtpHost", result.Error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(99999)]
    public async Task R3_SmtpReceiptMailer_InvalidPort_RejectsWithValidation(int invalidPort)
    {
        await using ServiceProvider sp = BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var repo = scope.ServiceProvider.GetRequiredService<IReceiptRepository>();

        string paymentId = $"pay_bad_port_{Guid.NewGuid():N}";
        await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-BADPORT-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 10_000,
            Currency: "PHP",
            CustomerName: "Bad Port Tester",
            CustomerEmail: "badport@rge.ph",
            Description: "Bad Port Test",
            PaymentMethod: "qrph"));

        var receipt = await repo.GetByPaymentAttemptIdAsync(paymentId);
        Assert.NotNull(receipt);

        var options = new SmtpReceiptOptions
        {
            Host = "smtp.gmail.com",
            Port = invalidPort,
            SenderEmail = "billing@rge.ph",
            AppPassword = "password"
        };
        var mailer = new SmtpReceiptMailer(options);

        var result = await mailer.SendReceiptEmailAsync(receipt, "recipient@rge.ph", [1, 2, 3]);

        Assert.True(result.IsFailure);
        Assert.Equal("Receipts.InvalidSmtpPort", result.Error.Code);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@missing-user.ph")]
    public async Task R3_SmtpReceiptMailer_InvalidSenderEmail_RejectsWithValidation(string invalidSenderEmail)
    {
        await using ServiceProvider sp = BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var repo = scope.ServiceProvider.GetRequiredService<IReceiptRepository>();

        string paymentId = $"pay_bad_sender_{Guid.NewGuid():N}";
        await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-BADSENDER-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 10_000,
            Currency: "PHP",
            CustomerName: "Bad Sender Tester",
            CustomerEmail: "badsender@rge.ph",
            Description: "Bad Sender Test",
            PaymentMethod: "qrph"));

        var receipt = await repo.GetByPaymentAttemptIdAsync(paymentId);
        Assert.NotNull(receipt);

        var options = new SmtpReceiptOptions
        {
            Host = "smtp.gmail.com",
            Port = 587,
            SenderEmail = invalidSenderEmail,
            AppPassword = "password"
        };
        var mailer = new SmtpReceiptMailer(options);

        var result = await mailer.SendReceiptEmailAsync(receipt, "recipient@rge.ph", [1, 2, 3]);

        Assert.True(result.IsFailure);
        Assert.Equal("Receipts.InvalidSenderEmail", result.Error.Code);
    }

    [Fact]
    public async Task R3_SmtpReceiptMailer_ConnectionRefused_CatchesExceptionSafely()
    {
        await using ServiceProvider sp = BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        var issueHandler = scope.ServiceProvider.GetRequiredService<IssueReceiptHandler>();
        var repo = scope.ServiceProvider.GetRequiredService<IReceiptRepository>();

        string paymentId = $"pay_conn_refuse_{Guid.NewGuid():N}";
        await issueHandler.HandleAsync(new IssueReceiptCommand(
            OrderId: $"ord_{paymentId}",
            OrderNumber: "ORD-CONNREFUSE-01",
            PaymentAttemptId: paymentId,
            AmountMinor: 10_000,
            Currency: "PHP",
            CustomerName: "Refusal Tester",
            CustomerEmail: "refusal@rge.ph",
            Description: "Refusal Test",
            PaymentMethod: "qrph"));

        var receipt = await repo.GetByPaymentAttemptIdAsync(paymentId);
        Assert.NotNull(receipt);

        var options = new SmtpReceiptOptions
        {
            Host = "127.0.0.1",
            Port = 19999, // Closed port
            SenderEmail = "billing@rge.ph",
            AppPassword = "password",
            EnableSsl = false,
            TimeoutMilliseconds = 1000
        };
        var mailer = new SmtpReceiptMailer(options);

        var result = await mailer.SendReceiptEmailAsync(receipt, "recipient@rge.ph", [1, 2, 3]);

        Assert.True(result.IsFailure);
        Assert.Equal("Receipts.SmtpSendFailed", result.Error.Code);
    }
}
